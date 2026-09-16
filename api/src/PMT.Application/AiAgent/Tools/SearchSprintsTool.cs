using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Sprints;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the sprints of one project, optionally filtered by text or status.</summary>
/// <remarks>
/// <para>This is the lookup the three sprint write tools depend on: every one of them is
/// addressed by sprint id, and this is the only tool that hands those ids out.</para>
/// <para>It does not derive from <see cref="EntitySearchToolBase"/> on purpose. That base models
/// "search records of type X across the instance" with a free-text query and a limit, whereas
/// SP_SPRINT has no such surface — its paged action takes a mandatory ProjectId, so a sprint
/// search is always a page of one project's board and is exposed here with the page/pageSize
/// pair the procedure actually accepts.</para>
/// <para>The status filter is applied in memory for the same reason the other search tools
/// filter in memory: the procedure accepts free text and paging only.</para>
/// </remarks>
public sealed class SearchSprintsTool(
    ISprintRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    public string Name => "search_sprints";

    public string Description =>
        "List the sprints of a project, newest page first, with their ids, goals, statuses and dates. "
        + "Give the project's key (for example 'PMT'); optionally filter by free text or by status. "
        + "Call this before update_sprint or complete_sprint to get the sprint id, and to answer "
        + "questions like 'which sprint is active?'.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." },
            "search": { "type": "string", "description": "Free-text filter applied to the sprint name and goal." },
            "status": {
              "type": "string",
              "enum": ["Planned", "Active", "Completed"],
              "description": "Only return sprints in this status."
            },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Sprints per page (1-{{MaxPageSize}}). Defaults to {{DefaultPageSize}}." }
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

        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(projectAccess, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        if (!ToolArguments.TryGetEnum<SprintStatus>(arguments, "status", out var status, out var statusError))
            return AgentToolResult.Fail(statusError!);

        var page = ToolArguments.GetInt(arguments, "page", 1, 1, 1000);
        var pageSize = ToolArguments.GetInt(arguments, "pageSize", DefaultPageSize, 1, MaxPageSize);
        var search = ToolArguments.GetString(arguments, "search");

        var pool = await repository.GetPagedAsync(projectId, page, pageSize, search, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Where(x => status is null || x.Status == status)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                goal = x.Goal,
                status = x.Status.ToString(),
                startDate = x.StartDate?.ToString("yyyy-MM-dd"),
                endDate = x.EndDate?.ToString("yyyy-MM-dd"),
                projectId = x.ProjectId
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
