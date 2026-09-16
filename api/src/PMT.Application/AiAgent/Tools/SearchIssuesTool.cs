using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Finds issues/defects by text, optionally narrowed to a project, task or severity.</summary>
public sealed class SearchIssuesTool(IIssueRepository repository) : EntitySearchToolBase
{
    public override string Name => "search_issues";

    public override string Description =>
        "Find issues or defects by title or description. Optionally filter by project, related "
        + "task or severity. Use this to locate the issueId needed by update_issue.";

    public override string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            {{CommonProperties}},
            "projectId": { "type": "integer", "description": "Only return issues in this project." },
            "taskId": { "type": "integer", "description": "Only return issues linked to this task." },
            "severity": {
              "type": "string",
              "enum": ["Low", "Medium", "High", "Critical"],
              "description": "Only return issues at this severity."
            }
          },
          "required": []
        }
        """;

    public override string? RequiredPermission => PermissionRequirement.IssuesView;

    protected override async Task<AgentToolResult> SearchAsync(
        AgentToolContext context, string? query, int limit, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetEnum<IssueSeverity>(arguments, "severity", out var severity, out var severityError))
            return AgentToolResult.Fail(severityError!);

        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is <= 0)
            return AgentToolResult.Fail("'projectId' must be a positive project id.");

        var taskId = ToolArguments.GetLong(arguments, "taskId");
        if (taskId is <= 0)
            return AgentToolResult.Fail("'taskId' must be a positive task id.");

        if (projectId is { } requested && !AgentToolScope.IsInScope(context, requested))
            return AgentToolResult.Fail($"This conversation is scoped to project {context.ProjectId}. Project {requested} is out of scope.");

        projectId ??= context.ProjectId;

        var pool = await repository.GetPagedAsync(1, AgentToolScope.CandidatePoolSize, query, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Where(x => projectId is null || x.ProjectId == projectId)
            .Where(x => taskId is null || x.TaskId == taskId)
            .Where(x => severity is null || x.Severity == severity)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                title = x.Title,
                status = x.Status.ToString(),
                severity = x.Severity.ToString(),
                projectId = x.ProjectId
            })
            .ToArray();

        return Results(results, pool.TotalCount);
    }
}
