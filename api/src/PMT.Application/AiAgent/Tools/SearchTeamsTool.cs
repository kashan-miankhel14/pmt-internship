using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Resolves a team name or key to a concrete team id. Every other team tool is addressed by id,
/// so this is normally the agent's first call when the user names a team in prose.
/// </summary>
/// <remarks>
/// Teams are not project-scoped, so unlike the entity searches there is nothing to filter against
/// the conversation's pinned project. Paging is expressed as page/pageSize here because SP_TEAM's
/// PAGED branch is paged rather than limited.
/// </remarks>
public sealed class SearchTeamsTool(ITeamRepository repository) : IAgentTool
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public string Name => "search_teams";

    public string Description =>
        "Search teams by name or key. Returns matching teams with their ids, keys, names, and "
        + "member counts. Use this to resolve a team name to its id before calling any tool that "
        + "takes a teamId.";

    // Reads inherit the same permission the REST surface requires for the same data.
    public string? RequiredPermission => PermissionRequirement.ProjectsView;

    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Free-text filter applied to the team name and key." },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Records per page (1-200). Defaults to 50." }
          },
          "required": []
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        // An absent argument object is fine (list everything); a scalar or array is not.
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var query = ToolArguments.GetString(arguments, "query");
        var page = ToolArguments.GetInt(arguments, "page", 1, 1, int.MaxValue);
        var pageSize = ToolArguments.GetInt(arguments, "pageSize", DefaultPageSize, 1, MaxPageSize);

        var pool = await repository.ListAsync(page, pageSize, query);

        var results = pool.Items
            .Select(x => new
            {
                id = x.Id,
                key = x.Key,
                name = x.Name,
                leadUserId = x.LeadUserId,
                leadName = x.LeadName,
                memberCount = x.MemberCount,
                isActive = x.IsActive
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            page,
            pageSize,
            totalCount = pool.TotalCount,
            results
        });
    }
}
