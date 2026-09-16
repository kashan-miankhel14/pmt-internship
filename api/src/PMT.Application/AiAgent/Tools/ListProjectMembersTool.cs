using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the people who hold direct membership of one project, with the role each one holds.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/projects/{projectKey}/members</c>, so it is addressed by the project's
/// public key like the other key-addressed tools and resolves it through
/// <see cref="ProjectKeyResolver"/>, which also applies the conversation's project pin.</para>
/// <para>The role is returned as a name as well as an id: an id on its own is meaningless to the
/// user Anna is answering, and ProjectRoles is an extensible table whose ids cannot be assumed.
/// SP_PROJECT_MEMBER already joins ProjectRoles for the PAGED projection, so the name costs nothing.</para>
/// <para>This covers direct membership only, which is exactly what the underlying procedure reads.
/// Access granted through a team is a separate table and is listed by list_project_team_grants.</para>
/// <para>Email addresses are deliberately not projected. The payload is handed to a third-party
/// model provider, and a member list is a bulk read, so this would ship the project's address book
/// off-platform for no gain: the model needs a userId to act and a name to speak. The <c>search</c>
/// filter still matches on email inside the procedure — the term travels in, the addresses do not
/// travel back out. search_users remains the one tool that returns an address, gated on users.view
/// and refusing a blank query for that reason.</para>
/// </remarks>
public sealed class ListProjectMembersTool(IProjectAccessRepository repository) : IAgentTool
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    public string Name => "list_project_members";

    public string Description =>
        "List the people who are direct members of a project, with the project role each one holds "
        + "(for example Project Admin, Member, Viewer). Give the project's key (for example 'PMT'); "
        + "optionally filter by a name or email fragment. Teams granted the project are not included "
        + "here — use list_project_team_grants for those.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." },
            "search": { "type": "string", "description": "Free-text filter applied to the member's full name and email." },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Members per page (1-{{MaxPageSize}}). Defaults to {{DefaultPageSize}}." }
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
        var search = ToolArguments.GetString(arguments, "search");

        var pool = await repository.ListMembersAsync(projectId, page, pageSize, search);

        var results = pool.Items
            .Select(x => new
            {
                userId = x.UserId,
                userName = x.UserName,
                projectRoleId = x.ProjectRoleId,
                projectRoleName = x.ProjectRoleName,
                addedByUserId = x.AddedBy,
                addedOn = x.InsertDate
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
