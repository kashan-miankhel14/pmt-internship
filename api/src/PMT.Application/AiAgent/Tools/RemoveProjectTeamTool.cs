using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Revokes a team's grant on a project. Members who were also added to the project directly keep
/// that access, so this does not necessarily lock everyone on the team out.
/// </summary>
/// <remarks>
/// Destructive, so the orchestrator parks the call and only runs it after the user approves it
/// through /ai/agent/confirm. This is the widest-reaching of the three revocation tools: one call
/// removes the project from everyone on the team who has no direct membership of their own, and
/// the agent cannot put the grant back with the roles it carried.
/// </remarks>
public sealed class RemoveProjectTeamTool(IProjectAccessRepository repository) : IAgentTool
{
    public string Name => "remove_project_team";

    public string Description =>
        "Remove a team's access from a project. Members who also hold direct membership of the "
        + "project keep that access. The team itself is not deleted. The user is asked to confirm "
        + "before the removal actually happens.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => true;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Id of the project." },
            "teamId": { "type": "integer", "description": "Id of the team whose access is removed." }
          },
          "required": ["projectId", "teamId"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is null or <= 0)
            return AgentToolResult.Fail("'projectId' is required and must be a positive project id.");

        if (!AgentToolScope.IsInScope(context, projectId.Value))
            return AgentToolResult.Fail($"This conversation is scoped to project {context.ProjectId}. Project {projectId} is out of scope.");

        var teamId = ToolArguments.GetLong(arguments, "teamId");
        if (teamId is null or <= 0)
            return AgentToolResult.Fail("'teamId' is required and must be a positive team id.");

        if (!await repository.RemoveTeamGrantAsync(projectId.Value, teamId.Value))
            return AgentToolResult.Fail($"Team {teamId} could not be removed from project {projectId}. The grant may not exist.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"Team #{teamId} no longer has access to project #{projectId}.",
            projectId = projectId.Value,
            teamId = teamId.Value
        });
    }
}
