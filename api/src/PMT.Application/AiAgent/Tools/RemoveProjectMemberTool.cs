using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Revokes one user's direct membership of a project. Access the user still holds through a team
/// grant survives this, so the effective role should be re-checked when it matters.
/// </summary>
/// <remarks>
/// Destructive, so the orchestrator parks the call and only runs it after the user approves it
/// through /ai/agent/confirm. Revoking access is not reversible by the agent — the membership row
/// and the role it carried are gone, and re-adding the user is a separate decision — and it can
/// lock a person out of their work, so it is gated exactly like a delete rather than treated as
/// an ordinary update.
/// </remarks>
public sealed class RemoveProjectMemberTool(IProjectAccessRepository repository) : IAgentTool
{
    public string Name => "remove_project_member";

    public string Description =>
        "Remove a user from a project. This removes their direct membership only; access they hold "
        + "through a team that is granted the project is unaffected. The user is asked to confirm "
        + "before the removal actually happens.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => true;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Id of the project." },
            "userId": { "type": "integer", "description": "Id of the user to remove." }
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

        if (!await repository.RemoveMemberAsync(projectId.Value, userId.Value))
            return AgentToolResult.Fail($"User {userId} could not be removed from project {projectId}. They may not be a member.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"User #{userId} was removed from project #{projectId}.",
            projectId = projectId.Value,
            userId = userId.Value
        });
    }
}
