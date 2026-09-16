using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Issues;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using PMT.Domain.Exceptions;

namespace PMT.Application.Workflow;

/// <summary>
/// Thin application service over the workflow engine for the API surface. Humans and Anna both
/// end up here: the API transition endpoint calls <see cref="TransitionIssueAsync"/>, while the
/// human/UI and agent update paths go through the engine inside the work-item services.
/// </summary>
public sealed class WorkflowService(
    IWorkflowRepository repository,
    IIssueRepository issueRepository,
    IWorkflowEngine engine,
    ICurrentUserService currentUser)
{
    public async Task<IReadOnlyCollection<WorkflowStatusDto>> GetStatusesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var statuses = await repository.GetStatusesByProjectAsync(projectId, cancellationToken);
        return statuses.Select(s => new WorkflowStatusDto(s.Id, s.Code, s.Name, s.Category, s.IsInitial, s.Order)).ToArray();
    }

    public async Task<IReadOnlyCollection<WorkflowTransitionDto>> GetTransitionsAsync(long projectId, string? fromStatusCode, CancellationToken cancellationToken = default)
    {
        var transitions = await engine.EvaluateAsync(projectId, fromStatusCode ?? string.Empty, cancellationToken);
        return Map(transitions);
    }

    public async Task<IReadOnlyCollection<IssueHistoryDto>> GetHistoryAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
    {
        var rows = await repository.GetHistoryAsync(entityType, entityId, cancellationToken);
        return rows.Select(h => new IssueHistoryDto(h.Id, h.EntityType, h.EntityId, h.FieldName, h.OldValue, h.NewValue, h.Comment, h.ChangedByUser, h.ChangedAtUtc)).ToArray();
    }

    /// <summary>Executes a status transition on an issue using the project's workflow.</summary>
    public async Task<WorkflowResultDto> TransitionIssueAsync(long issueId, WorkflowEntityKind kind, string? toStatusCode, long? transitionId, string? comment, CancellationToken cancellationToken = default)
    {
        var issue = await issueRepository.GetByIdAsync(issueId, cancellationToken)
            ?? throw new NotFoundException(nameof(Issue), issueId);

        var previous = issue.Status;
        var fromCode = WorkflowStatusMap.GetStatusCode(kind, previous);

        var result = transitionId is not null
            ? await engine.ExecuteAsync(kind, issue.ProjectId, issue.Id, fromCode, transitionId.Value,
                Values(comment, issue), cancellationToken)
            : await engine.ExecuteAsync(kind, issue.ProjectId, issue.Id, fromCode, toStatusCode!,
                Values(comment, issue), cancellationToken);

        var next = WorkflowStatusMap.ToEntityStatus(kind, result.ToStatusCode);
        if (next is null)
            throw new ValidationException(new[] { $"Transition to '{result.ToStatusCode}' cannot be expressed on a {kind} status. Choose a reachable transition." });

        issue.Status = (IssueStatus)next;
        issue.WorkflowStatusId = result.ToStatusId;
        issue.ResolvedDate = result.StampDoneDate
            ? issue.ResolvedDate ?? DateTime.UtcNow
            : previous is IssueStatus.Resolved or IssueStatus.Closed ? null : issue.ResolvedDate;
        issue.UpdateDate = DateTime.UtcNow;
        issue.UpdatedBy = currentUser.UserId;

        if (!await issueRepository.UpdateAsync(issue, cancellationToken))
            throw new NotFoundException(nameof(Issue), issueId);

        await repository.SaveHistoryAsync(new IssueHistory
        {
            EntityType = "Issue",
            EntityId = issue.Id,
            WorkflowTransitionId = result.Transition.Id,
            FieldName = "Status",
            OldValue = previous.ToString(),
            NewValue = issue.Status.ToString(),
            Comment = comment,
            ChangedByUser = currentUser.UserId,
            InsertedBy = currentUser.UserId
        }, cancellationToken);

        await repository.StampStatusAsync("Issue", issue.Id, result.ToStatusId, currentUser.UserId, cancellationToken);

        var available = await engine.EvaluateAsync(issue.ProjectId, result.ToStatusCode, cancellationToken);
        return new WorkflowResultDto(result.ToStatusCode, result.ToStatusName, result.ToCategory, result.ToStatusId, Map(available));
    }

    private static IDictionary<string, object?> Values(string? comment, Issue issue) =>
        new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = issue.AssignedToUserId };

    private static IReadOnlyCollection<WorkflowTransitionDto> Map(IEnumerable<WorkflowTransition> transitions) =>
        transitions.Select(t => new WorkflowTransitionDto(
            t.Id, t.Name!,
            t.FromStatusCode ?? string.Empty, t.FromStatusName ?? string.Empty,
            t.ToStatusCode!, t.ToStatusName!, t.ToStatusCategory, t.Order)).ToArray();
}
