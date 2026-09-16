using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the members of one team, with the role each holds inside the team.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/teams/{id}/members</c>. Teams are addressed by id everywhere in the
/// tool surface, so the team is looked up first with <c>GetByIdAsync</c> — that turns a stale or
/// invented id into "team N was not found" instead of an empty page the model would read as "the
/// team has no members", exactly as add_team_member and remove_team_member do.</para>
/// <para>Teams are not project-scoped, so there is no project pin to apply; this follows
/// search_teams in taking projects.view, which is the claim the REST route requires for the same
/// data.</para>
/// <para>Email addresses are deliberately not projected, as in list_project_members: the payload
/// goes to a third-party model provider, and the model needs a userId to act on and a name to
/// speak, not an address. search_users is the one tool that returns one, gated on users.view.</para>
/// </remarks>
public sealed class ListTeamMembersTool(ITeamRepository repository) : IAgentTool
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    public string Name => "list_team_members";

    public string Description =>
        "List the members of a team with the role each one holds in it (Lead, Member or Guest). "
        + "Resolve the teamId with search_teams first. Use this to answer 'who is on this team?' and "
        + "to find the userId of the person to remove with remove_team_member.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "teamId": { "type": "integer", "description": "Id of the team. Resolve it with search_teams." },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Members per page (1-{{MaxPageSize}}). Defaults to {{DefaultPageSize}}." }
          },
          "required": ["teamId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var teamId = ToolArguments.GetLong(arguments, "teamId");
        if (teamId is null or <= 0)
            return AgentToolResult.Fail("'teamId' is required and must be a positive team id.");

        var page = ToolArguments.GetInt(arguments, "page", 1, 1, 1000);
        var pageSize = ToolArguments.GetInt(arguments, "pageSize", DefaultPageSize, 1, MaxPageSize);

        var team = await repository.GetByIdAsync(teamId.Value);
        if (team is null) return AgentToolResult.Fail($"Team {teamId} was not found.");

        var pool = await repository.ListMembersAsync(team.Id, page, pageSize);

        var results = pool.Items
            .Select(x => new
            {
                userId = x.UserId,
                userName = x.UserName,
                teamRole = x.TeamRole,
                joinedAtUtc = x.JoinedAtUtc
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            page,
            pageSize,
            totalCount = pool.TotalCount,
            teamId = team.Id,
            teamKey = team.Key,
            teamName = team.Name,
            results
        });
    }
}
