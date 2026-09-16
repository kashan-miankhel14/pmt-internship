using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the workflow transitions that are reachable from one status of a project.</summary>
/// <remarks>
/// <para>This is the "what can happen next?" lookup. The transition graph is data, not code, so the
/// only honest way to answer "can this go straight to Done?" is to ask the project's own workflow;
/// <see cref="ListWorkflowStatusesTool"/> hands out the status codes this tool is addressed by.</para>
/// <para>Mirrors <c>GET /projects/{projectKey}/workflow/transitions?fromStatusCode=</c>: evaluation
/// goes through <see cref="IWorkflowEngine.EvaluateAsync"/>, the same call
/// <c>WorkflowService.GetTransitionsAsync</c> makes, so the agent and the REST surface can never
/// disagree about what is reachable. A blank <c>fromStatusCode</c> means the initial status, which
/// is how the API and SP_WORKFLOW_TRANSITION's FETCH_AVAILABLE action already read an absent
/// value.</para>
/// <para>A supplied status is matched against the project's catalog before the engine is called —
/// by code or by display name, ignoring case, spaces and underscores — because a model that writes
/// "In Progress" for <c>IN_PROGRESS</c> should be told the legal codes rather than handed an empty
/// list it will read as "nothing is possible".</para>
/// </remarks>
public sealed class ListWorkflowTransitionsTool(
    IWorkflowEngine engine,
    IWorkflowRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    public string Name => "list_workflow_transitions";

    public string Description =>
        "List the workflow transitions a work item can take from a given status in a project, with "
        + "each transition's id, name, from-status, to-status and target category. Give the "
        + "project's key (for example 'PMT') and optionally fromStatusCode (a status code from "
        + "list_workflow_statuses, such as 'IN_PROGRESS'); leave it out to get the transitions out "
        + "of the workflow's initial status. Use this to answer 'what can I do with this next?'.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." },
            "fromStatusCode": {
              "type": "string",
              "description": "Status code the item is in now, for example 'IN_PROGRESS'. Get it from list_workflow_statuses. Leave blank for the workflow's initial status."
            }
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

        var workflow = await repository.GetWorkflowByProjectAsync(projectId, cancellationToken);
        if (workflow is null)
            return AgentToolResult.Fail(
                $"Project {projectId} is not bound to a workflow, so it has no transitions. "
                + "Its work items use their built-in statuses instead.");

        // Null here means "the initial status", which is what the engine and the stored procedure
        // read a blank code as.
        string? fromStatusCode = null;

        var requested = ToolArguments.GetString(arguments, "fromStatusCode");
        if (requested is not null)
        {
            var statuses = await repository.GetStatusesByProjectAsync(projectId, cancellationToken);
            var live = statuses.Where(x => !x.IsDeleted).ToArray();

            var match = live.FirstOrDefault(x => Matches(x.Code, requested) || Matches(x.Name, requested));
            if (match is null)
                return AgentToolResult.Fail(
                    $"'{requested}' is not a status of this project's workflow. Expected one of: "
                    + $"{string.Join(", ", live.OrderBy(x => x.Order).Select(x => x.Code))}. "
                    + "Use list_workflow_statuses to see them with their names.");

            fromStatusCode = match.Code;
        }

        var transitions = await engine.EvaluateAsync(projectId, fromStatusCode ?? string.Empty, cancellationToken);

        var results = transitions
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                fromStatusCode = x.FromStatusCode,
                fromStatusName = x.FromStatusName,
                // A transition with no source status is a "from any status" edge; the flag is
                // carried explicitly because null fields are dropped from the payload.
                fromAnyStatus = x.FromStatusCode is null,
                toStatusCode = x.ToStatusCode,
                toStatusName = x.ToStatusName,
                toStatusCategory = x.ToStatusCategory.ToString(),
                order = x.Order
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            projectId,
            workflowId = workflow.Id,
            workflowName = workflow.Name,
            fromStatusCode,
            fromInitialStatus = fromStatusCode is null,
            transitions = results
        });
    }

    /// <summary>
    /// Compares a catalog code or name with what the model wrote, ignoring case and the spacing
    /// characters models move around ("in progress", "in-progress", "IN_PROGRESS").
    /// </summary>
    private static bool Matches(string candidate, string requested) =>
        string.Equals(Normalize(candidate), Normalize(requested), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
}
