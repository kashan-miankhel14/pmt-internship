using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reports the caller's own effective role on one project.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/projects/{projectKey}/my-role</c>. usp_Project_EffectiveRole collapses
/// direct membership and every team grant the caller reaches the project through, ordering by
/// SortOrder so the most privileged role wins; holding no role at all is a legitimate answer rather
/// than an error, which is why an empty result is reported as <c>hasRole: false</c> and not as a
/// failure the model has to reinterpret.</para>
/// <para>The user asked about is always <see cref="AgentToolContext.UserId"/>, never an argument:
/// this tool answers "what role do I have?", and accepting a userId would turn it into a way to
/// enumerate other people's access from a projects.view claim.</para>
/// </remarks>
public sealed class GetMyProjectRoleTool(IProjectAccessRepository repository) : IAgentTool
{
    public string Name => "get_my_project_role";

    public string Description =>
        "Report the role the current user holds on a project, combining their direct membership and "
        + "any team that is granted the project; the most privileged role wins. Give the project's "
        + "key (for example 'PMT'). Use this to answer 'what role do I have on PMT?' — it always "
        + "reports the caller's own role, never anyone else's.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." }
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

        if (context.UserId <= 0)
            return AgentToolResult.Fail("No authenticated user, so there is no role to report.");

        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(repository, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var role = await repository.GetEffectiveRoleAsync(projectId, context.UserId);

        return AgentToolResult.Ok(new
        {
            projectId,
            userId = context.UserId,
            hasRole = role is not null,
            projectRoleId = role?.Id,
            projectRoleName = role?.Name,
            sortOrder = role?.SortOrder,
            message = role is null
                ? $"The current user holds no role on project #{projectId}, either directly or through a team."
                : $"The current user's effective role on project #{projectId} is {role.Name}."
        });
    }
}
