using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Adds one user to a team.</summary>
/// <remarks>
/// dbo.TeamMembers stores the role as a string behind CK_teammembers_role, so the accepted values
/// are modelled here as an enum purely to reuse <see cref="ToolArguments.TryGetEnum{TEnum}"/>: the
/// model gets the legal values back in the error instead of a check-constraint violation.
/// </remarks>
public sealed class AddTeamMemberTool(ITeamRepository repository) : IAgentTool
{
    /// <summary>The three values CK_teammembers_role permits.</summary>
    private enum TeamRoleOption
    {
        Lead,
        Member,
        Guest
    }

    public string Name => "add_team_member";

    public string Description =>
        "Add a user to a team. Returns success or error. Resolve the teamId with search_teams and "
        + "the userId with search_users first.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "teamId": { "type": "integer", "description": "Id of the team." },
            "userId": { "type": "integer", "description": "Id of the user to add." },
            "teamRole": { "type": "string", "enum": ["Lead","Member","Guest"], "description": "Role within the team. Defaults to Member." }
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

        if (!ToolArguments.TryGetEnum<TeamRoleOption>(arguments, "teamRole", out var teamRole, out var roleError))
            return AgentToolResult.Fail(roleError!);

        var team = await repository.GetByIdAsync(teamId.Value);
        if (team is null) return AgentToolResult.Fail($"Team {teamId} was not found.");

        var role = (teamRole ?? TeamRoleOption.Member).ToString();

        if (!await repository.AddMemberAsync(team.Id, userId.Value, role, context.UserId))
            return AgentToolResult.Fail($"User {userId} could not be added to team {teamId}. They may already be a member.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"User #{userId} was added to team #{team.Id} ({team.Key}) as {role}.",
            teamId = team.Id,
            userId = userId.Value,
            teamRole = role
        });
    }
}
