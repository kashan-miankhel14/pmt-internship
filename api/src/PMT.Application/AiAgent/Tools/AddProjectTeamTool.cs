using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Grants a whole team access to a project under a project role. Every current and future member
/// of the team inherits that role, which is what makes this cheaper than adding members one by one.
/// </summary>
public sealed class AddProjectTeamTool(IProjectAccessRepository repository) : IAgentTool
{
    /// <summary>Seeded id of the Member role, used when the caller does not name one.</summary>
    private const long DefaultProjectRoleId = 2;

    public string Name => "add_project_team";

    public string Description =>
        "Grant a team access to a project with a project role. Every member of the team inherits "
        + "the role. Call list_project_roles to discover the current role ids and names instead of "
        + "assuming them, and resolve the projectId with search_projects and the teamId with "
        + "search_teams first.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Id of the project." },
            "teamId": { "type": "integer", "description": "Id of the team to grant access to." },
            "projectRoleId": { "type": "integer", "description": "Project role id: 1 = Project Admin, 2 = Member, 3 = Viewer. Defaults to 2." }
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

        var projectRoleId = ToolArguments.GetLong(arguments, "projectRoleId") ?? DefaultProjectRoleId;
        if (projectRoleId <= 0)
            return AgentToolResult.Fail("'projectRoleId' must be a positive role id: 1 = Project Admin, 2 = Member, 3 = Viewer.");

        if (!await repository.AddTeamGrantAsync(projectId.Value, teamId.Value, projectRoleId, context.UserId))
            return AgentToolResult.Fail(
                $"Team {teamId} could not be granted access to project {projectId}. The grant may already exist, or role {projectRoleId} may not exist.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"Team #{teamId} was granted access to project #{projectId} with role id {projectRoleId}.",
            projectId = projectId.Value,
            teamId = teamId.Value,
            projectRoleId
        });
    }
}
