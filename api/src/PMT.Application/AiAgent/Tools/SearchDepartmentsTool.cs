using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Departments;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Resolves a department name or code to a concrete department id. This is what create_project
/// needs before it can put a new project in the right department.
/// </summary>
/// <remarks>
/// <para>Without this tool the model has no way to turn "the Finance department" into an id, so
/// <see cref="CreateProjectTool"/> falls back to department 1 (the seeded IT row) and every
/// agent-created project lands in the wrong place. Anna is expected to call this first and pass
/// the resolved <c>departmentId</c> through.</para>
/// <para>Paging is expressed as page/pageSize rather than the limit the entity searches use,
/// because <see cref="IDepartmentRepository.GetPagedAsync"/> is paged in the same way SP_TEAM's
/// PAGED branch is; <see cref="SearchTeamsTool"/> is the model for the envelope. Departments are
/// not project-scoped, so there is no pinned-project filter to apply.</para>
/// </remarks>
public sealed class SearchDepartmentsTool(IDepartmentRepository repository) : IAgentTool
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    public string Name => "search_departments";

    public string Description =>
        "Find departments by name or code and return their ids, codes, names, descriptions and "
        + "whether they are active. Use this to resolve a department the user names in prose to "
        + "its id before calling create_project or update_project, or to list the departments "
        + "when the user is not sure which one they mean.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "search": { "type": "string", "description": "Free-text filter applied to the department name and code. Omit to list all departments." },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Records per page (1-200). Defaults to 25." }
          },
          "required": []
        }
        """;

    // Reads inherit the same permission the REST surface requires for the same data.
    public string? RequiredPermission => PermissionRequirement.DepartmentsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // An absent argument object is fine (list everything); a scalar or array is not.
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        // 'search' is the name the REST surface uses and the one the schema advertises; 'query'
        // is accepted too because that is what every other search tool calls the same argument
        // and a model will reach for it out of habit.
        var search = ToolArguments.GetString(arguments, "search") ?? ToolArguments.GetString(arguments, "query");
        // Same clamp as the other paged tools: an invented page number is pulled back into range
        // rather than turned into an enormous OFFSET the database has to scan past.
        var page = ToolArguments.GetInt(arguments, "page", 1, 1, 1000);
        var pageSize = ToolArguments.GetInt(arguments, "pageSize", DefaultPageSize, 1, MaxPageSize);

        var pool = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                code = x.Code,
                description = x.Description,
                active = x.Active
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
