using System.Text.Json;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Tools;

namespace PMT.Infrastructure.Ai.Tools;

/// <summary>
/// Retrieval over the indexed knowledge store. This is the agent's default way to answer
/// open questions about project content without guessing.
/// </summary>
public sealed class SearchKnowledgeTool(IAiIndexingService indexingService) : IAgentTool
{
    public string Name => "search_knowledge";

    public string Description =>
        "Search indexed PMT project knowledge (projects, stories, tasks, issues) by meaning. "
        + "Use this first for open-ended questions about what exists or what is happening.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Natural-language search query." },
            "projectId": { "type": "integer", "description": "Optional project id to restrict the search to." },
            "topN": { "type": "integer", "description": "Maximum results to return (1-20). Defaults to 5." }
          },
          "required": ["query"]
        }
        """;

    public string? RequiredPermission => null; // Retrieval is scoped by project, not by a distinct permission.

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var query = ToolArguments.GetString(arguments, "query");
        if (query is null)
            return AgentToolResult.Fail("The 'query' argument is required.");

        // An explicit request argument may narrow the scope but never widen it beyond the
        // project the conversation is pinned to.
        var projectId = context.ProjectId ?? ToolArguments.GetLong(arguments, "projectId");
        var topN = ToolArguments.GetInt(arguments, "topN", fallback: 5, min: 1, max: 20);

        var results = await indexingService.SearchAsync(query, projectId, topN, cancellationToken);

        return AgentToolResult.Ok(new
        {
            count = results.Count,
            results = results.Select(x => new
            {
                x.EntityType,
                x.EntityId,
                x.ProjectId,
                x.Title,
                x.Score,
                excerpt = Excerpt(x.Content)
            })
        });
    }

    /// <summary>Trims chunk bodies so a handful of results cannot exhaust the prompt budget.</summary>
    private static string Excerpt(string content) =>
        string.IsNullOrEmpty(content) || content.Length <= 600 ? content : content[..600] + "...";
}
