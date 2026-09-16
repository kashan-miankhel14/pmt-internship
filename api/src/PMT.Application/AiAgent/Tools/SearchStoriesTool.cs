using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.UserStories;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Finds user stories by text, optionally narrowed to one project or status.</summary>
public sealed class SearchStoriesTool(IUserStoryRepository repository) : EntitySearchToolBase
{
    public override string Name => "search_stories";

    public override string Description =>
        "Find user stories by title, description or acceptance criteria. Optionally filter by "
        + "project or status. Returns story ids needed by update_story and create_task.";

    public override string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            {{CommonProperties}},
            "projectId": { "type": "integer", "description": "Only return stories in this project." },
            "status": {
              "type": "string",
              "enum": ["Backlog", "Ready", "InProgress", "Review", "Done", "Cancelled"],
              "description": "Only return stories in this status."
            }
          },
          "required": []
        }
        """;

    public override string? RequiredPermission => PermissionRequirement.StoriesView;

    protected override async Task<AgentToolResult> SearchAsync(
        AgentToolContext context, string? query, int limit, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetEnum<StoryStatus>(arguments, "status", out var status, out var statusError))
            return AgentToolResult.Fail(statusError!);

        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is <= 0)
            return AgentToolResult.Fail("'projectId' must be a positive project id.");

        // A pinned conversation wins over whatever the model asked for.
        if (projectId is { } requested && !AgentToolScope.IsInScope(context, requested))
            return AgentToolResult.Fail($"This conversation is scoped to project {context.ProjectId}. Project {requested} is out of scope.");

        projectId ??= context.ProjectId;

        var pool = await repository.GetPagedAsync(1, AgentToolScope.CandidatePoolSize, query, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Where(x => projectId is null || x.ProjectId == projectId)
            .Where(x => status is null || x.Status == status)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                title = x.Title,
                status = x.Status.ToString(),
                priority = x.Priority,
                storyPoints = x.StoryPoints,
                projectId = x.ProjectId
            })
            .ToArray();

        return Results(results, pool.TotalCount);
    }
}
