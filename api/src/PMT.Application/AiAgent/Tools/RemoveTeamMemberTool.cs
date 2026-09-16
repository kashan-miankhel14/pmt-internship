using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Removes one user from a team. Membership is a link row, so this affects only the team: the
/// user account and any direct project membership they hold are untouched.
/// </summary>
/// <remarks>
/// Destructive, so the orchestrator parks the call and only runs it after the user approves it
/// through /ai/agent/confirm. The link row is deleted outright rather than soft-deleted, and the
/// removal cascades to every project the team is granted, so it can revoke access far beyond the
/// team the model named. That reach is why it is confirmed rather than auto-executed.
/// </remarks>
public sealed class RemoveTeamMemberTool(ITeamRepository repository) : IAgentTool
{
    public string Name => "remove_team_member";

    public string Description =>
        "Remove a user from a team. The user account itself is not deleted, and any access they "
        + "hold on a project directly rather than through this team is unaffected. The user is "
        + "asked to confirm before the removal actually happens.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => true;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "teamId": { "type": "integer", "description": "Id of the team." },
            "userId": { "type": "integer", "description": "Id of the user to remove." }
          },
          "required": ["teamId", "userId"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var teamId = ToolArguments.GetLong(arguments, "teamId");
        if (teamId is null or <= 0)
            return AgentToolResult.Fail("'teamId' is required and must be a positive team id.");

        var userId = ToolArguments.GetLong(arguments, "userId");
        if (userId is null or <= 0)
            return AgentToolResult.Fail("'userId' is required and must be a positive user id.");

        var team = await repository.GetByIdAsync(teamId.Value);
        if (team is null) return AgentToolResult.Fail($"Team {teamId} was not found.");

        if (!await repository.RemoveMemberAsync(team.Id, userId.Value))
            return AgentToolResult.Fail($"User {userId} could not be removed from team {teamId}. They may not be a member.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"User #{userId} was removed from team #{team.Id} ({team.Key}).",
            teamId = team.Id,
            userId = userId.Value
        });
    }
}
