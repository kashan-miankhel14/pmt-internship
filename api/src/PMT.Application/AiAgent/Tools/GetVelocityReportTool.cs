using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Reports;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the velocity report for a date range, for one project or across all of them.</summary>
/// <remarks>
/// <para>Goes through <see cref="ReportService"/> rather than <see cref="IReportRepository"/>
/// directly, which is the same path <c>ReportsController</c> takes: the report is an aggregate
/// scan over stories and tasks and the service holds the result for
/// <c>CacheRegions.ReportsTtl</c>, so a question Anna asks twice in a turn costs one scan. It
/// also means the numbers she quotes and the numbers on the dashboard come from the same cache
/// entry and cannot disagree.</para>
/// <para><c>projectId</c> is optional because SP_REPORT treats a null ProjectId as "every
/// project". In a project-pinned conversation the pin is used as the default and a different
/// project is refused, so an unqualified "what's our velocity?" answers about the pinned project
/// instead of silently widening to the whole organisation.</para>
/// </remarks>
public sealed class GetVelocityReportTool(ReportService reports) : IAgentTool
{
    public string Name => "get_velocity_report";

    public string Description =>
        "Retrieve the team's velocity report for a date range: completed stories, completed story "
        + "points and completed tasks per period. Pass a projectId to report on one project, or "
        + "leave it out to cover every project the caller can see. Resolve the projectId with "
        + "search_projects first when the user names a project.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Id of the project to report on. Omit for all projects. Resolve it with search_projects." },
            "from": { "type": "string", "description": "Start of the reporting window as YYYY-MM-DD. Required." },
            "to": { "type": "string", "description": "End of the reporting window as YYYY-MM-DD. Required and must be on or after 'from'." }
          },
          "required": ["from", "to"]
        }
        """;

    // Reads inherit the same permission the REST surface requires for the same data.
    public string? RequiredPermission => PermissionRequirement.ReportsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // 'from' and 'to' are required, so a missing argument object is refused below anyway; a
        // scalar or array is rejected here for the same reason the searches reject one.
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        long? projectId = null;

        if (ToolArguments.Has(arguments, "projectId"))
        {
            var requested = ToolArguments.GetLong(arguments, "projectId");
            if (requested is null or <= 0)
                return AgentToolResult.Fail("'projectId' must be a positive project id, or omitted to report on every project.");

            if (!AgentToolScope.IsInScope(context, requested.Value))
                return AgentToolResult.Fail($"This conversation is scoped to project {context.ProjectId}. Project {requested} is out of scope.");

            projectId = requested;
        }

        // A pinned conversation reports on its own project unless the model named one explicitly.
        projectId ??= context.ProjectId;

        var from = ToolArguments.GetDateTime(arguments, "from");
        if (from is null)
            return AgentToolResult.Fail("'from' is required and must be a date, for example 2026-01-01.");

        var to = ToolArguments.GetDateTime(arguments, "to");
        if (to is null)
            return AgentToolResult.Fail("'to' is required and must be a date, for example 2026-03-31.");

        if (to < from)
            return AgentToolResult.Fail("'to' must be on or after 'from'.");

        var series = await reports.GetVelocityAsync(projectId, from.Value, to.Value, cancellationToken);

        var results = series
            .Select(x => new
            {
                period = x.Period,
                completedStories = x.CompletedStories,
                completedStoryPoints = x.CompletedStoryPoints,
                completedTasks = x.CompletedTasks
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            projectId,
            allProjects = projectId is null,
            from = from.Value.ToString("yyyy-MM-dd"),
            to = to.Value.ToString("yyyy-MM-dd"),
            totalCompletedStories = series.Sum(x => x.CompletedStories),
            totalCompletedStoryPoints = series.Sum(x => x.CompletedStoryPoints),
            totalCompletedTasks = series.Sum(x => x.CompletedTasks),
            results
        });
    }
}
