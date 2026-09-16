using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Workflow;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the status catalog of the workflow a project is bound to, in board order.</summary>
/// <remarks>
/// <para>The three work-item tables keep their own fixed status enums, but the workflow catalog is
/// what a team actually names its columns after, and it is the vocabulary the transition graph is
/// expressed in. Without this tool Anna can only talk in the hardcoded enum spellings
/// (<c>Backlog</c>, <c>InProgress</c>, ...) and has no way to see that a project's real statuses
/// include <c>READY_FOR_QA</c> or <c>BLOCKED</c>; see <see cref="WorkflowStatusMap"/> for the
/// translation between the two worlds.</para>
/// <para>Mirrors <c>GET /projects/{projectKey}/workflow/statuses</c>: the project is addressed by
/// its public key through <see cref="ProjectKeyResolver"/>, exactly like the sprint and board
/// tools, and the rows come back through the same
/// <see cref="IWorkflowRepository.GetStatusesByProjectAsync"/> call the controller reaches via
/// <c>WorkflowService</c>. The ordering is re-applied here so the payload is board order whatever
/// SP_WORKFLOW_STATUS does later.</para>
/// </remarks>
public sealed class ListWorkflowStatusesTool(
    IWorkflowRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    public string Name => "list_workflow_statuses";

    public string Description =>
        "List the statuses in a project's workflow, in board order, with each status's code, "
        + "display name, category (ToDo, InProgress or Done) and whether it is the starting status. "
        + "Give the project's key (for example 'PMT'). Use this to learn the project's real status "
        + "vocabulary before calling list_workflow_transitions, instead of guessing status names.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." }
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

        // Reported separately from the statuses so "this project has no workflow scheme" and
        // "this project's workflow has no statuses" cannot be confused for one another.
        var workflow = await repository.GetWorkflowByProjectAsync(projectId, cancellationToken);
        if (workflow is null)
            return AgentToolResult.Fail(
                $"Project {projectId} is not bound to a workflow, so it has no status catalog. "
                + "Its work items use their built-in statuses instead.");

        var statuses = await repository.GetStatusesByProjectAsync(projectId, cancellationToken);

        var results = statuses
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name,
                category = x.Category.ToString(),
                isInitial = x.IsInitial,
                order = x.Order
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            projectId,
            workflowId = workflow.Id,
            workflowName = workflow.Name,
            statuses = results
        });
    }
}
