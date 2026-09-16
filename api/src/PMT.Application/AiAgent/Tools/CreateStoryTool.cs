using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;
using PMT.Application.UserStories;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Creates a user story in a project the caller can reach.</summary>
public sealed class CreateStoryTool(
    IUserStoryRepository repository,
    ISprintRepository sprintRepository,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "create_story";

    public string Description =>
        "Create a new user story in a project. Resolve the projectId with search_projects first. "
        + "Returns the created story including its new id. Anna must have every required value from "
        + "the user before calling this: ask for anything missing with ask_for_fields, then read the "
        + "details back and wait for the user's yes.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Project the story belongs to." },
            "title": { "type": "string", "description": "Short summary of the story. Max 250 characters." },
            "description": { "type": "string", "description": "Full description of the story." },
            "acceptanceCriteria": { "type": "string", "description": "Conditions that must hold for the story to be accepted." },
            "priority": { "type": "integer", "description": "1 (highest) to 5 (lowest). Defaults to 3." },
            "storyPoints": { "type": "number", "description": "Relative size estimate. Must not be negative." },
            "assignedToUserId": { "type": "integer", "description": "User the story is assigned to." },
            "sprintId": { "type": "integer", "description": "Sprint to commit the story to. Resolve it with search_sprints. Omit to leave the story in the backlog." },
            "status": {
              "type": "string",
              "enum": ["Backlog", "Ready", "InProgress", "Review", "Done", "Cancelled"],
              "description": "Initial status. Defaults to Backlog."
            }
          },
          "required": ["projectId", "title"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.StoriesManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is null)
            return AgentToolResult.Fail("'projectId' is required. Use search_projects to resolve a project name to its id.");

        var (project, projectError) = await scope.ResolveProjectAsync(context, projectId.Value, cancellationToken);
        if (project is null) return AgentToolResult.Fail(projectError!);

        var title = ToolArguments.GetString(arguments, "title");
        if (AgentToolScope.ValidateTitle(title) is { } titleError)
            return AgentToolResult.Fail(titleError);

        var priority = ToolArguments.GetIntOrNull(arguments, "priority");
        if (AgentToolScope.ValidatePriority(priority) is { } priorityError)
            return AgentToolResult.Fail(priorityError);

        var storyPoints = ToolArguments.GetDecimal(arguments, "storyPoints");
        if (storyPoints is < 0)
            return AgentToolResult.Fail("'storyPoints' cannot be negative.");

        if (!ToolArguments.TryGetEnum<StoryStatus>(arguments, "status", out var status, out var statusError))
            return AgentToolResult.Fail(statusError!);

        var assigneeId = ToolArguments.GetLong(arguments, "assignedToUserId");
        if (await scope.ValidateUserAsync(assigneeId, "assignedToUserId", cancellationToken) is { } assigneeError)
            return AgentToolResult.Fail(assigneeError);

        // Absent means "backlog", which is the column's null. A supplied value still has to look
        // like an id: SprintId is not a foreign key (see 0017_SprintsAndBoards.sql), so a nonsense
        // number would otherwise be stored without complaint.
        var sprintId = ToolArguments.GetLong(arguments, "sprintId");
        if (sprintId is <= 0)
            return AgentToolResult.Fail("'sprintId' must be a positive sprint id. Omit it to leave the story in the backlog.");

        // A sprint id that is present must belong to the same project, mirroring the rule the
        // validator enforces on the REST path; writing through the repository here would otherwise
        // bypass it.
        if (sprintId is > 0)
        {
            var sprint = await sprintRepository.GetByIdAsync(sprintId.Value, cancellationToken);
            if (sprint is null || sprint.IsDeleted)
                return AgentToolResult.Fail($"Sprint {sprintId} was not found.");

            if (sprint.ProjectId != project.Id)
                return AgentToolResult.Fail($"Sprint {sprintId} does not belong to the story's project.");
        }

        var entity = new UserStory
        {
            ProjectId = project.Id,
            Title = title!,
            Description = ToolArguments.GetString(arguments, "description"),
            AcceptanceCriteria = ToolArguments.GetString(arguments, "acceptanceCriteria"),
            Status = status ?? StoryStatus.Backlog,
            Priority = priority ?? 3,
            StoryPoints = storyPoints,
            AssigneeUserId = assigneeId,
            SprintId = sprintId,
            Active = true,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The story could not be created.");

        // Re-read so the model sees what was actually persisted, including procedure defaults.
        var created = await repository.GetByIdAsync(id, cancellationToken);

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            projectId = created?.ProjectId ?? entity.ProjectId,
            title = created?.Title ?? entity.Title,
            description = created?.Description ?? entity.Description,
            acceptanceCriteria = created?.AcceptanceCriteria ?? entity.AcceptanceCriteria,
            status = (created?.Status ?? entity.Status).ToString(),
            priority = created?.Priority ?? entity.Priority,
            storyPoints = created?.StoryPoints ?? entity.StoryPoints,
            assignedToUserId = created?.AssigneeUserId ?? entity.AssigneeUserId,
            sprintId = created?.SprintId ?? entity.SprintId
        });
    }
}
