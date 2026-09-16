using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Reports;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the per-person workload report, for one project or across all of them.</summary>
/// <remarks>
/// <para>Goes through <see cref="ReportService"/> rather than <see cref="IReportRepository"/>
/// directly, which is the same path <c>ReportsController</c> takes, so the tool inherits the
/// service's short-lived cache and reports the same figures as the dashboard. See
/// <see cref="GetVelocityReportTool"/> for the full reasoning.</para>
/// <para><c>projectId</c> is optional because SP_REPORT treats a null ProjectId as "every
/// project". A project-pinned conversation defaults to its own project and refuses a different
/// one, so "who is overloaded?" cannot quietly widen to the whole organisation.</para>
/// <para>A row with a null userId is unassigned work, which SP_REPORT groups on its own; it is
/// passed through as-is rather than dropped, because "nobody owns these eleven tasks" is usually
/// the answer the user actually needs.</para>
/// </remarks>
public sealed class GetWorkloadReportTool(ReportService reports) : IAgentTool
{
    public string Name => "get_workload_report";

    public string Description =>
        "Retrieve the current workload per person: how many open tasks and open issues each user "
        + "holds and how many hours those are estimated at. Pass a projectId to report on one "
        + "project, or leave it out to cover every project the caller can see. Resolve the "
        + "projectId with search_projects first when the user names a project.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Id of the project to report on. Omit for all projects. Resolve it with search_projects." }
          },
          "required": []
        }
        """;

    // Reads inherit the same permission the REST surface requires for the same data.
    public string? RequiredPermission => PermissionRequirement.ReportsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // An absent argument object is fine (report on everything); a scalar or array is not.
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

        var workload = await reports.GetWorkloadAsync(projectId, cancellationToken);

        var results = workload
            .Select(x => new
            {
                userId = x.UserId,
                userName = x.UserName,
                openTasks = x.OpenTasks,
                openIssues = x.OpenIssues,
                estimatedHours = x.EstimatedHours
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            projectId,
            allProjects = projectId is null,
            totalOpenTasks = workload.Sum(x => x.OpenTasks),
            totalOpenIssues = workload.Sum(x => x.OpenIssues),
            totalEstimatedHours = workload.Sum(x => x.EstimatedHours),
            results
        });
    }
}
