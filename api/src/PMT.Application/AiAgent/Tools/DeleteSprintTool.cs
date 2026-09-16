using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Deletes a sprint with confirmation.</summary>
public sealed class DeleteSprintTool(ISprintRepository repository) : IAgentTool
{
    public string Name => "delete_sprint";

    public string Description =>
        "Deletes a sprint from the project. Always ask the user for confirmation and name the sprint before calling this tool, as deleting a sprint removes the sprint container.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "sprintId": { "type": "integer", "description": "Id of the sprint to delete. Get it from search_sprints." }
          },
          "required": ["sprintId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => true;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var sprintId = ToolArguments.GetLong(arguments, "sprintId");
        if (sprintId is null or <= 0)
            return AgentToolResult.Fail("'sprintId' is required and must be a positive sprint id.");

        var sprint = await repository.GetByIdAsync(sprintId.Value, cancellationToken);
        if (sprint is null || sprint.IsDeleted)
            return AgentToolResult.Fail("Sprint not found.");

        if (!AgentToolScope.IsInScope(context, sprint.ProjectId))
            return AgentToolResult.Fail($"Sprint {sprintId} belongs to another project and is out of scope for this conversation.");

        var deleted = await repository.DeleteAsync(sprint.Id, context.UserId, cancellationToken);
        if (!deleted)
            return AgentToolResult.Fail($"Sprint '{sprint.Name}' could not be deleted.");

        return AgentToolResult.Ok(new
        {
            deleted = true,
            id = sprint.Id,
            name = sprint.Name,
            message = $"Sprint '{sprint.Name}' was deleted."
        });
    }
}
