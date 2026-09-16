using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the project role catalogue: the roles a project membership or team grant can carry.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/project-roles</c>. ProjectRoles ships seeded with Project Admin,
/// Member and Viewer, but it is an ordinary table an administrator can extend, so the ids named in
/// the access tools' descriptions are a convention rather than a contract. This tool is how Anna
/// gets the real ids before calling add_project_member or add_project_team, and how she turns a
/// role the user names in prose ("make her a viewer") into the id those tools take.</para>
/// <para>Roles are not project-scoped — the catalogue is instance-wide — so there is no project
/// pin to apply and no arguments to take. The repository fetches the whole table as a single
/// oversized page, so there is nothing to page through here either.</para>
/// </remarks>
public sealed class ListProjectRolesTool(IProjectRoleRepository repository) : IAgentTool
{
    public string Name => "list_project_roles";

    public string Description =>
        "List the project roles that can be assigned to a project member or a team grant, with their "
        + "ids, names and ordering (lower sort order means more privileged). Takes no arguments. Call "
        + "this to get the real projectRoleId before add_project_member or add_project_team instead of "
        + "assuming the seeded ids.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // An absent argument object is the normal case here; a scalar or array is still a mistake.
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var roles = await repository.ListAsync();

        var results = roles
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                sortOrder = x.SortOrder,
                isSystem = x.IsSystem
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            roles = results
        });
    }
}
