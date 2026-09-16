using PMT.Domain.Entities;

namespace PMT.Application.Workflow.Engine;

/// <summary>
/// The abstraction the work-item services, the agent tools and <c>WorkflowService</c> depend on.
/// Implemented by <see cref="WorkflowEngine"/>; it exists so those consumers can be constructed
/// with a substitute in tests and so the engine can be swapped without touching call sites.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>All transitions reachable from <paramref name="fromStatusCode"/> in the project's workflow.</summary>
    Task<IReadOnlyCollection<WorkflowTransition>> EvaluateAsync(
        long projectId, string fromStatusCode, CancellationToken cancellationToken = default);

    /// <summary>Executes a transition identified by target status code.</summary>
    Task<ExecuteResult> ExecuteAsync(
        WorkflowEntityKind kind, long projectId, long entityId, string fromStatusCode, string toStatusCode,
        IDictionary<string, object?>? values = null, CancellationToken cancellationToken = default);

    /// <summary>Executes a transition identified by its id.</summary>
    Task<ExecuteResult> ExecuteAsync(
        WorkflowEntityKind kind, long projectId, long entityId, string fromStatusCode, long transitionId,
        IDictionary<string, object?>? values = null, CancellationToken cancellationToken = default);
}
