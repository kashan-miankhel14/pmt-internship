using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Tasks;
using PMT.Application.UserStories;
using PMT.Domain.Entities;
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Creates a task under an existing user story.</summary>
/// <remarks>
/// <b>userStoryId is required, not optional.</b> dbo.Task.StoryId is NOT NULL and carries
/// FK_task_userstory to dbo.UserStory(Id), and TaskItem.UserStoryId coerces null to 0 on the
/// way in. Accepting a null parent would therefore not create an orphan task — it would send
/// StoryId = 0 to the database and fail on the foreign key with an error the model cannot act
/// on. Rejecting it here produces a message the agent can actually recover from.
/// </remarks>
public sealed class CreateTaskTool(
    ITaskRepository repository,
    IUserStoryRepository stories,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "create_task";

    public string Description =>
        "Create a task under an existing user story. Both projectId and userStoryId are required; "
        + "use search_stories to find the parent story. New tasks start in the ToDo column. "
        + "Anna must have every required value from the user before calling this: ask for anything "
        + "missing with ask_for_fields, then read the details back and wait for the user's yes.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Project the task belongs to." },
            "userStoryId": { "type": "integer", "description": "Parent user story. Required: every task must belong to a story." },
            "title": { "type": "string", "description": "Short summary of the task. Max 250 characters." },
            "description": { "type": "string", "description": "Full description of the work." },
            "priority": { "type": "integer", "description": "1 (highest) to 5 (lowest). Defaults to 3." },
            "assignedToUserId": { "type": "integer", "description": "User the task is assigned to." },
            "estimatedHours": { "type": "number", "description": "Estimated effort in hours. Must not be negative." }
          },
          "required": ["projectId", "userStoryId", "title"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.TasksManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is null)
            return AgentToolResult.Fail("'projectId' is required. Use search_projects to resolve a project name to its id.");

        var (project, projectError) = await scope.ResolveProjectAsync(context, projectId.Value, cancellationToken);
        if (project is null) return AgentToolResult.Fail(projectError!);

        var storyId = ToolArguments.GetLong(arguments, "userStoryId");
        if (storyId is null or <= 0)
            return AgentToolResult.Fail("'userStoryId' is required: every task must belong to a user story. Use search_stories to find one.");

        var story = await stories.GetByIdAsync(storyId.Value, cancellationToken);
        if (story is null || story.IsDeleted)
            return AgentToolResult.Fail($"UserStory {storyId} was not found.");

        // Guard against a task that claims one project while hanging off another project's story.
        if (story.ProjectId != project.Id)
            return AgentToolResult.Fail($"UserStory {storyId} belongs to project {story.ProjectId}, not project {project.Id}.");

        var title = ToolArguments.GetString(arguments, "title");
        if (AgentToolScope.ValidateTitle(title) is { } titleError)
            return AgentToolResult.Fail(titleError);

        var priority = ToolArguments.GetIntOrNull(arguments, "priority");
        if (AgentToolScope.ValidatePriority(priority) is { } priorityError)
            return AgentToolResult.Fail(priorityError);

        var estimatedHours = ToolArguments.GetDecimal(arguments, "estimatedHours");
        if (estimatedHours is < 0)
            return AgentToolResult.Fail("'estimatedHours' cannot be negative.");

        var assigneeId = ToolArguments.GetLong(arguments, "assignedToUserId");
        if (await scope.ValidateUserAsync(assigneeId, "assignedToUserId", cancellationToken) is { } assigneeError)
            return AgentToolResult.Fail(assigneeError);

        var entity = new TaskItem
        {
            ProjectId = project.Id,
            StoryId = story.Id,
            Title = title!,
            Description = ToolArguments.GetString(arguments, "description"),
            Status = TaskStatus.ToDo,
            Priority = priority ?? 3,
            AssigneeUserId = assigneeId,
            EstimateHours = estimatedHours,
            Active = true,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The task could not be created.");

        var created = await repository.GetByIdAsync(id, cancellationToken);

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            projectId = created?.ProjectId ?? entity.ProjectId,
            storyId = created?.StoryId ?? entity.StoryId,
            title = created?.Title ?? entity.Title,
            description = created?.Description ?? entity.Description,
            status = (created?.Status ?? entity.Status).ToString(),
            priority = created?.Priority ?? entity.Priority,
            assignedToUserId = created?.AssigneeUserId ?? entity.AssigneeUserId,
            estimatedHours = created?.EstimateHours ?? entity.EstimateHours
        });
    }
}
