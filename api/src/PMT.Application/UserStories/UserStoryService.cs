using FluentValidation;
using Microsoft.Extensions.Logging;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Common.Realtime;
using PMT.Application.Notifications;
using PMT.Application.Notifications.Dtos;
using PMT.Application.UserStories.Dtos;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;
using PMT.Domain.Enums;

namespace PMT.Application.UserStories;

public sealed class UserStoryService(
    IUserStoryRepository repository,
    IValidator<UpsertUserStoryRequest> validator,
    ICurrentUserService currentUser,
    IWorkflowEngine workflowEngine,
    IWorkflowRepository workflowRepository,
    NotificationService notifications,
    EntityChangeBroadcaster broadcaster,
    ILogger<UserStoryService> logger)
{
    public async Task<PagedResult<UserStoryDto>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);
        return new PagedResult<UserStoryDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<UserStoryDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => Map(await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(UserStory), id));

    public async Task<long> CreateAsync(UpsertUserStoryRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = new UserStory { InsertedBy = currentUser.UserId };
        Apply(entity, request);
        var id = await repository.CreateAsync(entity, cancellationToken);

        if (entity.AssigneeUserId is { } assignee)
            await NotifyAssignedAsync(assignee, entity.ProjectId, id, entity.Title, cancellationToken);

        await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.UserStory, id, EntityChangeActions.Created, cancellationToken);
        return id;
    }

    public async Task UpdateAsync(long id, UpsertUserStoryRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(UserStory), id);
        var previousStatus = entity.Status;
        var previousAssignee = entity.AssigneeUserId;

        if (!await TryApplyWorkflowAsync(entity, previousStatus, request.Status, request.Comment, cancellationToken)
            && !UserStoryStatusRules.IsAllowed(previousStatus, request.Status))
        {
            throw new PMT.Domain.Exceptions.ValidationException(new[] { $"User story status cannot change from {previousStatus} to {request.Status}." });
        }

        Apply(entity, request);
        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        if (!await repository.UpdateAsync(entity, cancellationToken))
            throw new NotFoundException(nameof(UserStory), id);

        if (entity.AssigneeUserId is { } assignee && assignee != previousAssignee)
            await NotifyAssignedAsync(assignee, entity.ProjectId, id, entity.Title, cancellationToken);

        if (entity.Status == StoryStatus.Done && previousStatus != StoryStatus.Done)
        {
            var actorId = currentUser.UserId;
            var targetUserId = entity.InsertedBy > 0 && entity.InsertedBy != actorId
                ? entity.InsertedBy
                : (entity.AssigneeUserId > 0 && entity.AssigneeUserId != actorId ? entity.AssigneeUserId : null);

            if (targetUserId is { } notifyUser)
                await NotifyCompletedAsync(notifyUser, entity.ProjectId, id, entity.Title, cancellationToken);
        }

        await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.UserStory, id, EntityChangeActions.Updated, cancellationToken);
    }

    private async Task NotifyAssignedAsync(long assigneeUserId, long projectId, long storyId, string title, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                assigneeUserId,
                "story.assigned",
                "Story assigned",
                $"You were assigned story #{storyId}: {title}",
                $"/stories?projectId={projectId}&storyId={storyId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify user {UserId} about story {StoryId}.", assigneeUserId, storyId);
        }
    }

    private async Task NotifyCompletedAsync(long recipientUserId, long projectId, long storyId, string title, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                recipientUserId,
                "story.completed",
                "Story completed",
                $"User story #{storyId} ({title}) has been marked as Done.",
                $"/stories?projectId={projectId}&storyId={storyId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify user {UserId} that story {StoryId} was completed.", recipientUserId, storyId);
        }
    }

    private async Task<bool> TryApplyWorkflowAsync(UserStory entity, StoryStatus previous, StoryStatus next, string? comment, CancellationToken cancellationToken)
    {
        try
        {
            var workflow = await workflowRepository.GetWorkflowByProjectAsync(entity.ProjectId, cancellationToken);
            if (workflow is null) return false;

            var fromCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Story, previous);
            var toCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Story, next);
            if (fromCode == toCode) return false;

            var result = await workflowEngine.ExecuteAsync(WorkflowEntityKind.Story, entity.ProjectId, entity.Id, fromCode, toCode,
                new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = entity.AssigneeUserId }, cancellationToken);

            entity.Status = next;
            entity.WorkflowStatusId = result.ToStatusId;

            await workflowRepository.SaveHistoryAsync(new IssueHistory
            {
                EntityType = WorkflowEntityKind.Story.ToEntityType(),
                EntityId = entity.Id,
                WorkflowTransitionId = result.Transition.Id,
                FieldName = "Status",
                OldValue = previous.ToString(),
                NewValue = next.ToString(),
                Comment = comment,
                ChangedByUser = currentUser.UserId,
                InsertedBy = currentUser.UserId
            }, cancellationToken);

            await workflowRepository.StampStatusAsync(WorkflowEntityKind.Story.ToEntityType(), entity.Id, result.ToStatusId, currentUser.UserId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Workflow engine could not move story {StoryId} from {PreviousStatus} to {NextStatus}; falling back to the hardcoded status rules.", entity.Id, previous, next);

            return false;
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        UserStory? entity;
        try
        {
            entity = await repository.GetByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not read story {StoryId} before deleting it; the change broadcast will be skipped.", id);

            entity = null;
        }

        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(UserStory), id);

        if (entity is not null)
            await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.UserStory, id, EntityChangeActions.Deleted, cancellationToken);
    }

    private async Task ValidateAsync(UpsertUserStoryRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));
    }

    private static void Apply(UserStory entity, UpsertUserStoryRequest request)
    {
        entity.ProjectId = request.ProjectId;
        entity.Title = request.Title.Trim();
        entity.Description = request.Description?.Trim();
        entity.AcceptanceCriteria = request.AcceptanceCriteria?.Trim();
        entity.Status = request.Status;
        entity.Priority = request.Priority;
        entity.StoryPoints = request.StoryPoints;
        entity.AssigneeUserId = request.AssignedToUserId;
        entity.TeamId = request.TeamId;
        entity.SprintId = request.SprintId;
        entity.Active = request.Active;
    }

    private static UserStoryDto Map(UserStory x) => new(x.Id, x.ProjectId, x.Title, x.Description, x.AcceptanceCriteria, x.Status, x.Priority, x.StoryPoints, x.AssigneeUserId, x.AssigneeUserId?.ToString(), x.Active, x.SprintId, x.TeamId);
}
