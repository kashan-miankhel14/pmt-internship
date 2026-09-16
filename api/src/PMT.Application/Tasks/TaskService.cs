using FluentValidation;
using Microsoft.Extensions.Logging;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Common.Realtime;
using PMT.Application.Notifications;
using PMT.Application.Notifications.Dtos;
using PMT.Application.Tasks.Dtos;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Application.Tasks;

public sealed class TaskService(
    ITaskRepository repository,
    IValidator<UpsertTaskRequest> validator,
    ICurrentUserService currentUser,
    IWorkflowEngine workflowEngine,
    IWorkflowRepository workflowRepository,
    NotificationService notifications,
    EntityChangeBroadcaster broadcaster,
    ILogger<TaskService> logger)
{
    public async Task<PagedResult<TaskDto>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);
        return new PagedResult<TaskDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<TaskDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => Map(await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(TaskItem), id));

    public async Task<long> CreateAsync(UpsertTaskRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = new TaskItem { InsertedBy = currentUser.UserId };
        Apply(entity, request);
        if (request.Status == TaskStatus.Done)
            entity.CompletedDate = DateTime.UtcNow;
        var id = await repository.CreateAsync(entity, cancellationToken);

        if (entity.AssignedToUserId is { } assignee)
            await NotifyAssignedAsync(assignee, entity.ProjectId, id, entity.Title, cancellationToken);

        await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.Task, id, EntityChangeActions.Created, cancellationToken);
        return id;
    }

    public async Task UpdateAsync(long id, UpsertTaskRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(TaskItem), id);
        var previousStatus = entity.Status;
        // Captured before Apply overwrites it: only a change of hands is worth a notification, a
        // save that leaves the same person on the task is not.
        var previousAssignee = entity.AssignedToUserId;

        if (!await TryApplyWorkflowAsync(entity, previousStatus, request.Status, request.Comment, cancellationToken)
            && !TaskStatusRules.IsAllowed(previousStatus, request.Status))
        {
            throw new PMT.Domain.Exceptions.ValidationException(new[] { $"Task status cannot change from {previousStatus} to {request.Status}." });
        }

        Apply(entity, request);
        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        entity.CompletedDate = request.Status switch
        {
            TaskStatus.Done => entity.CompletedDate ?? DateTime.UtcNow,
            _ when previousStatus == TaskStatus.Done => null,
            _ => entity.CompletedDate
        };
        if (!await repository.UpdateAsync(entity, cancellationToken))
            throw new NotFoundException(nameof(TaskItem), id);

        if (entity.AssignedToUserId is { } assignee && assignee != previousAssignee)
            await NotifyAssignedAsync(assignee, entity.ProjectId, id, entity.Title, cancellationToken);

        if (entity.Status == TaskStatus.Done && previousStatus != TaskStatus.Done)
        {
            var actorId = currentUser.UserId;
            var targetUserId = entity.InsertedBy > 0 && entity.InsertedBy != actorId
                ? entity.InsertedBy
                : (entity.AssignedToUserId > 0 && entity.AssignedToUserId != actorId ? entity.AssignedToUserId : null);

            if (targetUserId is { } notifyUser)
                await NotifyCompletedAsync(notifyUser, entity.ProjectId, id, entity.Title, cancellationToken);
        }

        await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.Task, id, EntityChangeActions.Updated, cancellationToken);
    }

    /// <summary>
    /// Tells the new assignee the task is theirs.
    /// </summary>
    private async Task NotifyAssignedAsync(long assigneeUserId, long projectId, long taskId, string title, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                assigneeUserId,
                "task.assigned",
                "Task assigned",
                $"You were assigned task #{taskId}: {title}",
                $"/tasks?projectId={projectId}&taskId={taskId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify user {UserId} about task {TaskId}.", assigneeUserId, taskId);
        }
    }

    private async Task NotifyCompletedAsync(long recipientUserId, long projectId, long taskId, string title, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                recipientUserId,
                "task.completed",
                "Task completed",
                $"Task #{taskId} ({title}) has been marked as Done.",
                $"/tasks?projectId={projectId}&taskId={taskId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify user {UserId} that task {TaskId} was completed.", recipientUserId, taskId);
        }
    }

    private async Task<bool> TryApplyWorkflowAsync(TaskItem entity, TaskStatus previous, TaskStatus next, string? comment, CancellationToken cancellationToken)
    {
        try
        {
            var workflow = await workflowRepository.GetWorkflowByProjectAsync(entity.ProjectId, cancellationToken);
            if (workflow is null) return false;

            var fromCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Task, previous);
            var toCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Task, next);
            if (fromCode == toCode) return false;

            var result = await workflowEngine.ExecuteAsync(WorkflowEntityKind.Task, entity.ProjectId, entity.Id, fromCode, toCode,
                new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = entity.AssignedToUserId }, cancellationToken);

            entity.Status = next;
            entity.WorkflowStatusId = result.ToStatusId;
            entity.CompletedDate = result.StampDoneDate
                ? entity.CompletedDate ?? DateTime.UtcNow
                : previous == TaskStatus.Done ? null : entity.CompletedDate;

            await workflowRepository.SaveHistoryAsync(new IssueHistory
            {
                EntityType = "Task",
                EntityId = entity.Id,
                WorkflowTransitionId = result.Transition.Id,
                FieldName = "Status",
                OldValue = previous.ToString(),
                NewValue = next.ToString(),
                Comment = comment,
                ChangedByUser = currentUser.UserId,
                InsertedBy = currentUser.UserId
            }, cancellationToken);

            await workflowRepository.StampStatusAsync("Task", entity.Id, result.ToStatusId, currentUser.UserId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Workflow engine could not move task {TaskId} from {PreviousStatus} to {NextStatus}; falling back to the hardcoded status rules.", entity.Id, previous, next);

            return false;
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        // Read before the delete: the broadcast has to name the project the task belonged to, and
        // the row is soft-deleted (and no longer readable) once the procedure has run. The read is
        // best effort — it only feeds the broadcast, so a transient failure here must not turn a
        // delete that would have succeeded into a 500.
        TaskItem? entity;
        try
        {
            entity = await repository.GetByIdAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not read task {TaskId} before deleting it; the change broadcast will be skipped.", id);

            entity = null;
        }

        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(TaskItem), id);

        if (entity is not null)
            await broadcaster.PublishAsync(entity.ProjectId, EntityChangeTypes.Task, id, EntityChangeActions.Deleted, cancellationToken);
    }

    private async Task ValidateAsync(UpsertTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));
    }

    private static void Apply(TaskItem entity, UpsertTaskRequest request)
    {
        entity.ProjectId = request.ProjectId;
        entity.UserStoryId = request.UserStoryId;
        entity.Title = request.Title.Trim();
        entity.Description = request.Description?.Trim();
        entity.Status = request.Status;
        entity.Priority = request.Priority;
        entity.AssignedToUserId = request.AssignedToUserId;
        entity.TeamId = request.TeamId;
        entity.EstimatedHours = request.EstimatedHours;
        entity.ActualHours = request.ActualHours;
        entity.DueDate = request.DueDate;
        entity.Active = request.Active;
    }

    private static TaskDto Map(TaskItem x) => new(x.Id, x.ProjectId, x.UserStoryId, x.Title, x.Description, x.Status, x.Priority, x.AssignedToUserId, x.EstimatedHours, x.ActualHours, x.DueDate, x.CompletedDate, x.Active, x.TeamId);
}
