using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Grants one user direct access to a project under a project role.</summary>
/// <remarks>
/// The seeded roles are 1 = Project Admin, 2 = Member, 3 = Viewer, but ProjectRoles is an ordinary
/// table an administrator can extend, so only "positive id" is checked here; SP_PROJECT_MEMBER
/// rejects an id that does not resolve to a live role.
/// </remarks>
public sealed class AddProjectMemberTool(IProjectAccessRepository repository) : IAgentTool
{
    /// <summary>Seeded id of the Member role, used when the caller does not name one.</summary>
    private const long DefaultProjectRoleId = 2;

    public string Name => "add_project_member";

    public string Description =>
        "Add a user to a project with a project role. Returns success or error. Call "
        + "list_project_roles to discover the current role ids and names instead of assuming them, "
        + "and resolve the projectId with search_projects and the userId with search_users first.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Id of the project." },
            "userId": { "type": "integer", "description": "Id of the user to add." },
            "projectRoleId": { "type": "integer", "description": "Project role id: 1 = Project Admin, 2 = Member, 3 = Viewer. Defaults to 2." }
          },
          "required": ["projectId", "userId"]
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

        var userId = ToolArguments.GetLong(arguments, "userId");
        if (userId is null or <= 0)
            return AgentToolResult.Fail("'userId' is required and must be a positive user id.");

        var projectRoleId = ToolArguments.GetLong(arguments, "projectRoleId") ?? DefaultProjectRoleId;
        if (projectRoleId <= 0)
            return AgentToolResult.Fail("'projectRoleId' must be a positive role id: 1 = Project Admin, 2 = Member, 3 = Viewer.");

        if (!await repository.AddMemberAsync(projectId.Value, userId.Value, projectRoleId, context.UserId))
            return AgentToolResult.Fail(
                $"User {userId} could not be added to project {projectId}. They may already be a member, or role {projectRoleId} may not exist.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"User #{userId} was added to project #{projectId} with role id {projectRoleId}.",
            projectId = projectId.Value,
            userId = userId.Value,
            projectRoleId
        });
    }
}
