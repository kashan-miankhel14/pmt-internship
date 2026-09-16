using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;
using PMT.Application.UserStories;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Applies a partial update to a user story. Only the fields the model actually supplies are
/// touched; everything else keeps its stored value.
/// </summary>
/// <remarks>
/// Status changes are governed by the data-driven <see cref="WorkflowEngine"/> whenever the
/// project has a workflow scheme, and fall back to <see cref="UserStoryStatusRules"/> otherwise.
/// </remarks>
public sealed class UpdateStoryTool(
    IUserStoryRepository repository,
    ISprintRepository sprintRepository,
    IWorkflowEngine workflowEngine,
    IWorkflowRepository workflowRepository,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "update_story";

    public string Description =>
        "Update an existing user story. Supply only the fields you want to change. "
        + "Send null for assignedToUserId, storyPoints or sprintId to clear them. Anna states the "
        + "change and waits for the user's yes before calling this; use ask_for_fields when it is "
        + "unclear which fields should change or what to.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "storyId": { "type": "integer", "description": "Id of the story to update." },
            "title": { "type": "string", "description": "New title. Max 250 characters." },
            "description": { "type": "string", "description": "New description. Null clears it." },
            "acceptanceCriteria": { "type": "string", "description": "New acceptance criteria. Null clears it." },
            "priority": { "type": "integer", "description": "1 (highest) to 5 (lowest)." },
            "storyPoints": { "type": "number", "description": "Relative size estimate. Null clears it." },
            "assignedToUserId": { "type": "integer", "description": "New assignee. Null unassigns the story." },
            "sprintId": { "type": "integer", "description": "Sprint to commit the story to. Resolve it with search_sprints. Null moves the story back to the backlog." },
            "status": {
              "type": "string",
              "enum": ["Backlog", "Ready", "InProgress", "Review", "Done", "Cancelled"],
              "description": "New status. Must be a legal transition from the current status."
            },
            "comment": {
              "type": "string",
              "description": "Optional note recorded with the status change (required for some transitions such as Blocked)."
            }
          },
          "required": ["storyId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.StoriesManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var storyId = ToolArguments.GetLong(arguments, "storyId");
        if (storyId is null or <= 0)
            return AgentToolResult.Fail("'storyId' is required and must be a positive story id.");

        var story = await repository.GetByIdAsync(storyId.Value, cancellationToken);
        if (story is null || story.IsDeleted)
            return AgentToolResult.Fail($"UserStory {storyId} was not found.");

        if (!AgentToolScope.IsInScope(context, story.ProjectId))
            return AgentToolResult.Fail($"UserStory {storyId} belongs to another project and is out of scope for this conversation.");

        var changed = false;
        var comment = ToolArguments.GetString(arguments, "comment");

        if (ToolArguments.Has(arguments, "title"))
        {
            var title = ToolArguments.GetString(arguments, "title");
            if (AgentToolScope.ValidateTitle(title) is { } titleError)
                return AgentToolResult.Fail(titleError);

            story.Title = title!;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "description"))
        {
            story.Description = ToolArguments.GetString(arguments, "description");
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "acceptanceCriteria"))
        {
            story.AcceptanceCriteria = ToolArguments.GetString(arguments, "acceptanceCriteria");
            changed = true;
        }

        if (ToolArguments.Has(arguments, "priority"))
        {
            var priority = ToolArguments.GetIntOrNull(arguments, "priority");
            if (AgentToolScope.ValidatePriority(priority) is { } priorityError)
                return AgentToolResult.Fail(priorityError);

            story.Priority = priority!.Value;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "storyPoints"))
        {
            var storyPoints = ToolArguments.GetDecimal(arguments, "storyPoints");
            if (storyPoints is < 0)
                return AgentToolResult.Fail("'storyPoints' cannot be negative.");

            story.StoryPoints = storyPoints;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "assignedToUserId"))
        {
            var assigneeId = ToolArguments.GetLong(arguments, "assignedToUserId");
            if (await scope.ValidateUserAsync(assigneeId, "assignedToUserId", cancellationToken) is { } assigneeError)
                return AgentToolResult.Fail(assigneeError);

            story.AssigneeUserId = assigneeId;
            changed = true;
        }

        // Read with IsMentioned so an explicit null returns the story to the backlog while an
        // absent field leaves its sprint alone. The stored value rides along on every update
        // because the whole entity is written back, which is what keeps a partial edit from
        // quietly pulling a story out of its sprint.
        if (ToolArguments.IsMentioned(arguments, "sprintId"))
        {
            var sprintId = ToolArguments.GetLong(arguments, "sprintId");
            if (sprintId is <= 0)
                return AgentToolResult.Fail("'sprintId' must be a positive sprint id, or null to move the story back to the backlog.");

            // A sprint that is being assigned must belong to the same project as the story, the
            // same rule the validator enforces on the REST path; otherwise a model could commit a
            // story to a sprint of another project just by knowing its id.
            if (sprintId is > 0)
            {
                var sprint = await sprintRepository.GetByIdAsync(sprintId.Value, cancellationToken);
                if (sprint is null || sprint.IsDeleted)
                    return AgentToolResult.Fail($"Sprint {sprintId} was not found.");

                if (sprint.ProjectId != story.ProjectId)
                    return AgentToolResult.Fail($"Sprint {sprintId} does not belong to the story's project.");
            }

            story.SprintId = sprintId;
            changed = true;
        }

        if (ToolArguments.Has(arguments, "status"))
        {
            if (!ToolArguments.TryGetEnum<StoryStatus>(arguments, "status", out var status, out var statusError))
                return AgentToolResult.Fail(statusError!);

            var previousStatus = story.Status;
            var next = status!.Value;
            if (!await TryApplyWorkflowAsync(story, previousStatus, next, comment, context.UserId, cancellationToken))
            {
                if (!UserStoryStatusRules.IsAllowed(previousStatus, next))
                    return AgentToolResult.Fail($"Story status cannot change from {previousStatus} to {next}.");

                story.Status = next;
            }

            changed = true;
        }

        if (!changed)
            return AgentToolResult.Fail("No updatable fields were supplied. Provide at least one of: title, description, acceptanceCriteria, priority, storyPoints, assignedToUserId, sprintId, status.");

        story.UpdateDate = DateTime.UtcNow;
        story.UpdatedBy = context.UserId;

        if (!await repository.UpdateAsync(story, cancellationToken))
            return AgentToolResult.Fail($"UserStory {storyId} could not be updated.");

        return AgentToolResult.Ok(new
        {
            updated = true,
            id = story.Id,
            projectId = story.ProjectId,
            title = story.Title,
            description = story.Description,
            acceptanceCriteria = story.AcceptanceCriteria,
            status = story.Status.ToString(),
            priority = story.Priority,
            storyPoints = story.StoryPoints,
            assignedToUserId = story.AssigneeUserId,
            sprintId = story.SprintId
        });
    }

    private async Task<bool> TryApplyWorkflowAsync(
        UserStory story, StoryStatus previous, StoryStatus next, string? comment, long userId, CancellationToken cancellationToken)
    {
        var workflow = await workflowRepository.GetWorkflowByProjectAsync(story.ProjectId, cancellationToken);
        if (workflow is null) return false;

        var fromCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Story, previous);
        var toCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Story, next);
        if (fromCode == toCode) return false;

        var result = await workflowEngine.ExecuteAsync(WorkflowEntityKind.Story, story.ProjectId, story.Id, fromCode, toCode,
            new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = story.AssigneeUserId }, cancellationToken);

        story.Status = next;
        story.WorkflowStatusId = result.ToStatusId;

        await workflowRepository.SaveHistoryAsync(new IssueHistory
        {
            EntityType = WorkflowEntityKind.Story.ToEntityType(),
            EntityId = story.Id,
            WorkflowTransitionId = result.Transition.Id,
            FieldName = "Status",
            OldValue = previous.ToString(),
            NewValue = next.ToString(),
            Comment = comment,
            ChangedByUser = userId,
            InsertedBy = userId
        }, cancellationToken);

        await workflowRepository.StampStatusAsync(WorkflowEntityKind.Story.ToEntityType(), story.Id, result.ToStatusId, userId, cancellationToken);
        return true;
    }
}
