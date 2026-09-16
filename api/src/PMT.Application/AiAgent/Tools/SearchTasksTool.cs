using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Tasks;
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Finds tasks by text, optionally narrowed to a project, parent story or status.</summary>
public sealed class SearchTasksTool(ITaskRepository repository) : EntitySearchToolBase
{
    public override string Name => "search_tasks";

    public override string Description =>
        "Find tasks by title or description. Optionally filter by project, parent story or status. "
        + "Use this to locate the taskId needed by update_task.";

    public override string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            {{CommonProperties}},
            "projectId": { "type": "integer", "description": "Only return tasks in this project." },
            "storyId": { "type": "integer", "description": "Only return tasks belonging to this user story." },
            "status": {
              "type": "string",
              "enum": ["ToDo", "InProgress", "Blocked", "Review", "Done", "Cancelled"],
              "description": "Only return tasks in this Kanban column."
            }
          },
          "required": []
        }
        """;

    public override string? RequiredPermission => PermissionRequirement.TasksView;

    protected override async Task<AgentToolResult> SearchAsync(
        AgentToolContext context, string? query, int limit, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetEnum<TaskStatus>(arguments, "status", out var status, out var statusError))
            return AgentToolResult.Fail(statusError!);

        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is <= 0)
            return AgentToolResult.Fail("'projectId' must be a positive project id.");

        var storyId = ToolArguments.GetLong(arguments, "storyId");
        if (storyId is <= 0)
            return AgentToolResult.Fail("'storyId' must be a positive story id.");

        if (projectId is { } requested && !AgentToolScope.IsInScope(context, requested))
            return AgentToolResult.Fail($"This conversation is scoped to project {context.ProjectId}. Project {requested} is out of scope.");

        projectId ??= context.ProjectId;

        var pool = await repository.GetPagedAsync(1, AgentToolScope.CandidatePoolSize, query, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Where(x => projectId is null || x.ProjectId == projectId)
            .Where(x => storyId is null || x.StoryId == storyId)
            .Where(x => status is null || x.Status == status)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                title = x.Title,
                status = x.Status.ToString(),
                priority = x.Priority,
                projectId = x.ProjectId,
                storyId = x.StoryId
            })
            .ToArray();

        return Results(results, pool.TotalCount);
    }
}
