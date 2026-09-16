using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Tasks;
using PMT.Application.UserStories;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Applies a partial update to a task, including the status change that moves it between
/// Kanban columns.
/// </summary>
/// <remarks>
/// Status changes are governed by the data-driven <see cref="WorkflowEngine"/> whenever the
/// project has a workflow scheme, and fall back to <see cref="TaskStatusRules"/> otherwise.
/// CompletedDate is maintained the same way <c>TaskService</c> maintains it.
/// </remarks>
public sealed class UpdateTaskTool(
    ITaskRepository repository,
    IUserStoryRepository stories,
    IWorkflowEngine workflowEngine,
    IWorkflowRepository workflowRepository,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "update_task";

    public string Description =>
        "Update an existing task. Supply only the fields you want to change. Set status to move "
        + "the task between Kanban columns. Send null for assignedToUserId, estimatedHours or "
        + "dueDate to clear them. Anna states the change and waits for the user's yes before calling "
        + "this; use ask_for_fields when it is unclear which fields should change or what to.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "taskId": { "type": "integer", "description": "Id of the task to update." },
            "title": { "type": "string", "description": "New title. Max 250 characters." },
            "description": { "type": "string", "description": "New description. Null clears it." },
            "status": {
              "type": "string",
              "enum": ["ToDo", "InProgress", "Blocked", "Review", "Done", "Cancelled"],
              "description": "New Kanban column. Must be a legal transition from the current status."
            },
            "comment": {
              "type": "string",
              "description": "Optional note recorded with the status change (required for some transitions such as Blocked)."
            },
            "priority": { "type": "integer", "description": "1 (highest) to 5 (lowest)." },
            "assignedToUserId": { "type": "integer", "description": "New assignee. Null unassigns the task." },
            "estimatedHours": { "type": "number", "description": "Estimated effort in hours. Null clears it." },
            "actualHours": { "type": "number", "description": "Actual effort logged in hours. Null clears it." },
            "dueDate": { "type": "string", "description": "Due date in ISO-8601 format. Null clears it." },
            "userStoryId": { "type": "integer", "description": "Move the task under a different story in the same project." }
          },
          "required": ["taskId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.TasksManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var taskId = ToolArguments.GetLong(arguments, "taskId");
        if (taskId is null or <= 0)
            return AgentToolResult.Fail("'taskId' is required and must be a positive task id.");

        var task = await repository.GetByIdAsync(taskId.Value, cancellationToken);
        if (task is null || task.IsDeleted)
            return AgentToolResult.Fail($"Task {taskId} was not found.");

        if (!AgentToolScope.IsInScope(context, task.ProjectId))
            return AgentToolResult.Fail($"Task {taskId} belongs to another project and is out of scope for this conversation.");

        var previousStatus = task.Status;
        var changed = false;
        var comment = ToolArguments.GetString(arguments, "comment");

        if (ToolArguments.Has(arguments, "title"))
        {
            var title = ToolArguments.GetString(arguments, "title");
            if (AgentToolScope.ValidateTitle(title) is { } titleError)
                return AgentToolResult.Fail(titleError);

            task.Title = title!;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "description"))
        {
            task.Description = ToolArguments.GetString(arguments, "description");
            changed = true;
        }

        if (ToolArguments.Has(arguments, "priority"))
        {
            var priority = ToolArguments.GetIntOrNull(arguments, "priority");
            if (AgentToolScope.ValidatePriority(priority) is { } priorityError)
                return AgentToolResult.Fail(priorityError);

            task.Priority = priority!.Value;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "assignedToUserId"))
        {
            var assigneeId = ToolArguments.GetLong(arguments, "assignedToUserId");
            if (await scope.ValidateUserAsync(assigneeId, "assignedToUserId", cancellationToken) is { } assigneeError)
                return AgentToolResult.Fail(assigneeError);

            task.AssigneeUserId = assigneeId;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "estimatedHours"))
        {
            var estimatedHours = ToolArguments.GetDecimal(arguments, "estimatedHours");
            if (estimatedHours is < 0)
                return AgentToolResult.Fail("'estimatedHours' cannot be negative.");

            task.EstimateHours = estimatedHours;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "actualHours"))
        {
            var actualHours = ToolArguments.GetDecimal(arguments, "actualHours");
            if (actualHours is < 0)
                return AgentToolResult.Fail("'actualHours' cannot be negative.");

            task.ActualHours = actualHours;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "dueDate"))
        {
            if (ToolArguments.Has(arguments, "dueDate"))
            {
                var dueDate = ToolArguments.GetDateTime(arguments, "dueDate");
                if (dueDate is null)
                    return AgentToolResult.Fail("'dueDate' must be an ISO-8601 date, for example 2026-03-31.");

                task.DueDate = dueDate;
            }
            else
            {
                task.DueDate = null;
            }

            changed = true;
        }

        if (ToolArguments.Has(arguments, "userStoryId"))
        {
            var storyId = ToolArguments.GetLong(arguments, "userStoryId");
            if (storyId is null or <= 0)
                return AgentToolResult.Fail("'userStoryId' must be a positive story id.");

            var story = await stories.GetByIdAsync(storyId.Value, cancellationToken);
            if (story is null || story.IsDeleted)
                return AgentToolResult.Fail($"UserStory {storyId} was not found.");

            if (story.ProjectId != task.ProjectId)
                return AgentToolResult.Fail($"UserStory {storyId} belongs to project {story.ProjectId}, not project {task.ProjectId}. Tasks cannot be moved across projects.");

            task.StoryId = story.Id;
            changed = true;
        }

        if (ToolArguments.Has(arguments, "status"))
        {
            if (!ToolArguments.TryGetEnum<TaskStatus>(arguments, "status", out var status, out var statusError))
                return AgentToolResult.Fail(statusError!);

            var next = status!.Value;
            if (!await TryApplyWorkflowAsync(task, previousStatus, next, comment, context.UserId, cancellationToken))
            {
                if (!TaskStatusRules.IsAllowed(previousStatus, next))
                    return AgentToolResult.Fail($"Task status cannot change from {previousStatus} to {next}.");

                task.Status = next;
            }

            changed = true;
        }

        if (!changed)
            return AgentToolResult.Fail("No updatable fields were supplied. Provide at least one of: title, description, status, priority, assignedToUserId, estimatedHours, actualHours, dueDate, userStoryId.");

        task.UpdateDate = DateTime.UtcNow;
        task.UpdatedBy = context.UserId;

        // Same completion bookkeeping TaskService performs.
        task.CompletedDate = task.Status switch
        {
            TaskStatus.Done => task.CompletedDate ?? DateTime.UtcNow,
            _ when previousStatus == TaskStatus.Done => null,
            _ => task.CompletedDate
        };

        if (!await repository.UpdateAsync(task, cancellationToken))
            return AgentToolResult.Fail($"Task {taskId} could not be updated.");

        return AgentToolResult.Ok(new
        {
            updated = true,
            id = task.Id,
            projectId = task.ProjectId,
            storyId = task.StoryId,
            title = task.Title,
            description = task.Description,
            status = task.Status.ToString(),
            priority = task.Priority,
            assignedToUserId = task.AssigneeUserId,
            estimatedHours = task.EstimateHours,
            actualHours = task.ActualHours,
            dueDate = task.DueDate,
            completedDate = task.CompletedDate
        });
    }

    private async Task<bool> TryApplyWorkflowAsync(
        TaskItem task, TaskStatus previous, TaskStatus next, string? comment, long userId, CancellationToken cancellationToken)
    {
        var workflow = await workflowRepository.GetWorkflowByProjectAsync(task.ProjectId, cancellationToken);
        if (workflow is null) return false;

        var fromCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Task, previous);
        var toCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Task, next);
        if (fromCode == toCode) return false;

        var result = await workflowEngine.ExecuteAsync(WorkflowEntityKind.Task, task.ProjectId, task.Id, fromCode, toCode,
            new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = task.AssignedToUserId }, cancellationToken);

        task.Status = next;
        task.WorkflowStatusId = result.ToStatusId;

        await workflowRepository.SaveHistoryAsync(new IssueHistory
        {
            EntityType = "Task",
            EntityId = task.Id,
            WorkflowTransitionId = result.Transition.Id,
            FieldName = "Status",
            OldValue = previous.ToString(),
            NewValue = next.ToString(),
            Comment = comment,
            ChangedByUser = userId,
            InsertedBy = userId
        }, cancellationToken);

        await workflowRepository.StampStatusAsync("Task", task.Id, result.ToStatusId, userId, cancellationToken);
        return true;
    }
}
