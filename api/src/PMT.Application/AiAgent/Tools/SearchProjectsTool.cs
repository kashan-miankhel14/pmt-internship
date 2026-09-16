using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Resolves a project name, key or description to a concrete project id. This is normally the
/// agent's first call, because every other tool is addressed by id.
/// </summary>
public sealed class SearchProjectsTool(IProjectRepository repository) : EntitySearchToolBase
{
    public override string Name => "search_projects";

    public override string Description =>
        "Find projects by name, key or description. Use this to resolve a project name to its id "
        + "before calling any tool that takes a projectId.";

    public override string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            {{CommonProperties}}
          },
          "required": []
        }
        """;

    // Reads inherit the same permission the REST surface requires for the same data.
    public override string? RequiredPermission => PermissionRequirement.ProjectsView;

    protected override async Task<AgentToolResult> SearchAsync(
        AgentToolContext context, string? query, int limit, JsonElement arguments, CancellationToken cancellationToken)
    {
        var pool = await repository.GetPagedAsync(1, AgentToolScope.CandidatePoolSize, query, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Where(x => AgentToolScope.IsInScope(context, x.Id))
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                key = x.Key,
                status = x.Status.ToString()
            })
            .ToArray();

        return Results(results, pool.TotalCount);
    }
}
