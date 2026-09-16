using System.Text.Json;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Shared shape for the "look up records of type X" tools: argument parsing, paging limits and
/// the result envelope live here so the concrete tools contain only their filter logic.
/// </summary>
/// <remarks>
/// <para><b>Filters are applied in memory.</b> The stored procedures behind every repository
/// accept only a free-text Search plus paging — there is no server-side projectId, status or
/// severity predicate. So a search pulls a bounded candidate pool
/// (<see cref="AgentToolScope.CandidatePoolSize"/> rows) and filters it here.</para>
/// <para>That is correct for the common case but it is not exhaustive: when the text search
/// alone matches more rows than the pool holds, a matching record can fall outside it. The
/// envelope therefore reports <c>incomplete</c> so the model can say "there may be more"
/// instead of asserting a total. The real fix is filter parameters on the procedures.</para>
/// </remarks>
public abstract class EntitySearchToolBase : IAgentTool
{
    protected const int DefaultLimit = 10;
    protected const int MaxLimit = 25;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string ParametersJsonSchema { get; }
    public abstract string? RequiredPermission { get; }

    public bool IsDestructive => false;

    public Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // An absent argument object is fine (search everything); a scalar or array is not.
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return Task.FromResult(AgentToolResult.Fail("Arguments must be a JSON object."));

        var query = ToolArguments.GetString(arguments, "query");
        var limit = ToolArguments.GetInt(arguments, "limit", DefaultLimit, 1, MaxLimit);

        return SearchAsync(context, query, limit, arguments, cancellationToken);
    }

    protected abstract Task<AgentToolResult> SearchAsync(
        AgentToolContext context,
        string? query,
        int limit,
        JsonElement arguments,
        CancellationToken cancellationToken);

    /// <summary>Builds the standard envelope, flagging when the candidate pool may have hidden matches.</summary>
    protected static AgentToolResult Results<T>(IReadOnlyCollection<T> results, long poolTotal) =>
        AgentToolResult.Ok(new
        {
            count = results.Count,
            incomplete = poolTotal > AgentToolScope.CandidatePoolSize,
            results
        });

    /// <summary>Shared JSON Schema fragment for the two arguments every search tool accepts.</summary>
    protected const string CommonProperties = """
        "query": { "type": "string", "description": "Free-text filter applied to the title and description." },
        "limit": { "type": "integer", "description": "Maximum records to return (1-25). Defaults to 10." }
        """;
}
