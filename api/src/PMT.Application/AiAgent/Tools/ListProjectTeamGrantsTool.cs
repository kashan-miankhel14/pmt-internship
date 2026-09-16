using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the teams granted access to one project, and the role each grant carries.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/projects/{projectKey}/teams</c>. A team grant gives every member of
/// the team the role named here, so this is the other half of "who can see this project?" —
/// list_project_members covers direct membership, and neither list is complete on its own.</para>
/// <para>SP_PROJECT_TEAM's PAGED branch takes no search argument, so unlike the member list this
/// tool exposes paging only; there is nothing to filter on server-side.</para>
/// </remarks>
public sealed class ListProjectTeamGrantsTool(IProjectAccessRepository repository) : IAgentTool
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    public string Name => "list_project_team_grants";

    public string Description =>
        "List the teams that have been granted access to a project, with the project role each grant "
        + "carries and how many members the team has. Give the project's key (for example 'PMT'). "
        + "Every member of a granted team inherits that role, so read this alongside "
        + "list_project_members to see everyone who can reach the project.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Team grants per page (1-{{MaxPageSize}}). Defaults to {{DefaultPageSize}}." }
          },
          "required": ["projectKey"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(repository, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var page = ToolArguments.GetInt(arguments, "page", 1, 1, 1000);
        var pageSize = ToolArguments.GetInt(arguments, "pageSize", DefaultPageSize, 1, MaxPageSize);

        var pool = await repository.ListTeamGrantsAsync(projectId, page, pageSize);

        var results = pool.Items
            .Select(x => new
            {
                teamId = x.TeamId,
                teamKey = x.TeamKey,
                teamName = x.TeamName,
                projectRoleId = x.ProjectRoleId,
                projectRoleName = x.ProjectRoleName,
                memberCount = x.MemberCount,
                grantedOn = x.InsertDate
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            page,
            pageSize,
            totalCount = pool.TotalCount,
            projectId,
            results
        });
    }
}
