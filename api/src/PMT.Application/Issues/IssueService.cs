using FluentValidation;
using Microsoft.Extensions.Logging;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Common.Realtime;
using PMT.Application.Issues.Dtos;
using PMT.Application.Notifications;
using PMT.Application.Notifications.Dtos;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;
using PMT.Domain.Enums;

namespace PMT.Application.Issues;

public sealed class IssueService(
    IIssueRepository repository,
    IValidator<UpsertIssueRequest> validator,
    ICurrentUserService currentUser,
    IWorkflowEngine workflowEngine,
    IWorkflowRepository workflowRepository,
    NotificationService notifications,
    EntityChangeBroadcaster broadcaster,
    ILogger<IssueService> logger)
{
    public async Task<PagedResult<IssueDto>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);
        return new PagedResult<IssueDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<IssueDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => Map(await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Issue), id));

    public async Task<long> CreateAsync(UpsertIssueRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = new Issue { InsertedBy = currentUser.UserId };
        Apply(entity, request);
        var id = await repository.CreateAsync(entity, cancellationToken);

        if (entity.AssignedToUserId is { } assignee)
            await NotifyAssignedAsync(assignee, entity.ProjectId, id, entity.Title, cancellationToken);

        await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.Issue, id, EntityChangeActions.Created, cancellationToken);
        return id;
    }

    public async Task UpdateAsync(long id, UpsertIssueRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Issue), id);
        var previousStatus = entity.Status;
        // Captured before Apply overwrites it: only a change of hands is worth a notification.
        var previousAssignee = entity.AssignedToUserId;

        if (await TryApplyWorkflowAsync(entity, WorkflowEntityKind.Issue, previousStatus, request.Status, request.Comment, cancellationToken))
        {
            // Engine applied workflow
        }
        else if (!IssueStatusRules.IsAllowed(previousStatus, request.Status))
        {
            throw new PMT.Domain.Exceptions.ValidationException(new[] { $"Issue status cannot change from {previousStatus} to {request.Status}." });
        }

        Apply(entity, request);
        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        entity.ResolvedDate = request.Status is IssueStatus.Resolved or IssueStatus.Closed
            ? entity.ResolvedDate ?? DateTime.UtcNow
            : previousStatus is IssueStatus.Resolved or IssueStatus.Closed ? null : entity.ResolvedDate;
        if (!await repository.UpdateAsync(entity, cancellationToken))
            throw new NotFoundException(nameof(Issue), id);

        if (entity.AssignedToUserId is { } assignee && assignee != previousAssignee)
            await NotifyAssignedAsync(assignee, entity.ProjectId, id, entity.Title, cancellationToken);

        if ((entity.Status == IssueStatus.Resolved || entity.Status == IssueStatus.Closed) && previousStatus != entity.Status)
        {
            var actorId = currentUser.UserId;
            var targetUserId = entity.ReportedByUserId > 0 && entity.ReportedByUserId != actorId
                ? entity.ReportedByUserId
                : (entity.AssignedToUserId > 0 && entity.AssignedToUserId != actorId ? entity.AssignedToUserId : null);

            if (targetUserId is { } notifyUser)
                await NotifyResolvedAsync(notifyUser, entity.ProjectId, id, entity.Title, entity.Status.ToString(), cancellationToken);
        }

        await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.Issue, id, EntityChangeActions.Updated, cancellationToken);
    }

    /// <summary>
    /// Tells the new assignee the issue is theirs.
    /// </summary>
    private async Task NotifyAssignedAsync(long assigneeUserId, long projectId, long issueId, string title, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                assigneeUserId,
                "issue.assigned",
                "Issue assigned",
                $"You were assigned issue #{issueId}: {title}",
                $"/issues?projectId={projectId}&issueId={issueId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify user {UserId} about issue {IssueId}.", assigneeUserId, issueId);
        }
    }

    private async Task NotifyResolvedAsync(long recipientUserId, long projectId, long issueId, string title, string statusName, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                recipientUserId,
                "issue.resolved",
                $"Issue {statusName}",
                $"Issue #{issueId} ({title}) has been marked as {statusName}.",
                $"/issues?projectId={projectId}&issueId={issueId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify user {UserId} that issue {IssueId} was {Status}.", recipientUserId, issueId, statusName);
        }
    }

    private async Task<bool> TryApplyWorkflowAsync(Issue entity, WorkflowEntityKind kind, IssueStatus previous, IssueStatus next, string? comment, CancellationToken cancellationToken)
    {
        try
        {
            var projectId = entity.ProjectId;
            var workflow = await workflowRepository.GetWorkflowByProjectAsync(projectId, cancellationToken);
            if (workflow is null)
                return false;

            var fromCode = WorkflowStatusMap.GetStatusCode(kind, previous);
            var toCode = WorkflowStatusMap.GetStatusCode(kind, next);
            if (fromCode == toCode)
                return false;

            var result = await workflowEngine.ExecuteAsync(kind, projectId, entity.Id, fromCode, toCode,
                new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = entity.AssignedToUserId }, cancellationToken);

            entity.Status = next;
            entity.WorkflowStatusId = result.ToStatusId;
            entity.ResolvedDate = result.StampDoneDate
                ? entity.ResolvedDate ?? DateTime.UtcNow
                : previous is IssueStatus.Resolved or IssueStatus.Closed ? null : entity.ResolvedDate;

            await workflowRepository.SaveHistoryAsync(new IssueHistory
            {
                EntityType = "Issue",
                EntityId = entity.Id,
                WorkflowTransitionId = result.Transition.Id,
                FieldName = "Status",
                OldValue = previous.ToString(),
                NewValue = next.ToString(),
                Comment = comment,
                ChangedByUser = currentUser.UserId,
                InsertedBy = currentUser.UserId
            }, cancellationToken);

            await workflowRepository.StampStatusAsync("Issue", entity.Id, result.ToStatusId, currentUser.UserId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Workflow engine could not move issue {IssueId} from {PreviousStatus} to {NextStatus}; falling back to the hardcoded status rules.", entity.Id, previous, next);

            return false;
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        // Read before the delete: the broadcast has to name the project the issue belonged to, and
        // the row is soft-deleted (and no longer readable) once the procedure has run. The fetch
        // already projects the project key, so no second lookup is needed. The read is best effort
        // — it only feeds the broadcast, so a transient failure here must not turn a delete that
        // would have succeeded into a 500.
        Issue? entity;
        try
        {
            entity = await repository.GetByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not read issue {IssueId} before deleting it; the change broadcast will be skipped.", id);

            entity = null;
        }

        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(Issue), id);

        if (entity is not null)
            await broadcaster.PublishForKeyAsync(entity.ProjectKey, EntityChangeTypes.Issue, id, EntityChangeActions.Deleted, cancellationToken);
    }

    private async Task ValidateAsync(UpsertIssueRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));
    }

    private static void Apply(Issue entity, UpsertIssueRequest request)
    {
        entity.ProjectId = request.ProjectId;
        entity.TaskId = request.TaskId;
        entity.Title = request.Title.Trim();
        entity.Description = request.Description?.Trim();
        entity.Severity = request.Severity;
        entity.Status = request.Status;
        entity.ReportedByUserId = request.ReportedByUserId ?? throw new PMT.Domain.Exceptions.ValidationException(["ReportedByUserId is required."]);
        entity.AssignedToUserId = request.AssignedToUserId;
        entity.TeamId = request.TeamId;
        entity.Active = request.Active;
    }

    private static IssueDto Map(Issue x) => new(x.Id, x.Number, x.ProjectId, x.TaskId, x.Title, x.Description, x.Severity, x.Status, x.ReportedByUserId, x.AssignedToUserId, x.ResolvedDate, x.Active, x.ProjectKey, x.TeamId);
}
