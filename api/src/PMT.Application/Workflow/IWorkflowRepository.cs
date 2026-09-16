using PMT.Domain.Entities;
// This namespace is itself called "Workflow", which shadows the entity type of the same name,
// so the entity is reached through an alias here.
using WorkflowEntity = PMT.Domain.Entities.Workflow;

namespace PMT.Application.Workflow;

/// <summary>
/// Reads the workflow catalog, evaluates available transitions and persists the change-log.
/// Every call dispatches to a stored procedure in the SP_WORKFLOW* family.
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>Resolves the workflow bound to a project, or the default workflow.</summary>
    Task<WorkflowEntity?> GetWorkflowByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>All statuses of the project's workflow, in board order.</summary>
    Task<IReadOnlyCollection<WorkflowStatus>> GetStatusesByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Live transitions leaving <paramref name="fromStatusCode"/> for the project's workflow.</summary>
    Task<IReadOnlyCollection<WorkflowTransition>> GetAvailableTransitionsAsync(long projectId, string fromStatusCode, CancellationToken cancellationToken = default);

    /// <summary>A single transition by id (project-scoped).</summary>
    Task<WorkflowTransition?> GetTransitionByIdAsync(long projectId, long transitionId, CancellationToken cancellationToken = default);

    /// <summary>The single transition from one status code to another (project-scoped).</summary>
    Task<WorkflowTransition?> GetTransitionAsync(long projectId, string fromStatusCode, string toStatusCode, CancellationToken cancellationToken = default);

    /// <summary>Appends a row to the generic change-log.</summary>
    Task<long> SaveHistoryAsync(IssueHistory entry, CancellationToken cancellationToken = default);

    /// <summary>Most-recent-first change-log for one entity.</summary>
    Task<IReadOnlyCollection<IssueHistory>> GetHistoryAsync(string entityType, long entityId, CancellationToken cancellationToken = default);

    /// <summary>Denormalised breadcrumb of the resolved catalog status on a work item.</summary>
    Task StampStatusAsync(string entityType, long entityId, long? workflowStatusId, long? changedByUser, CancellationToken cancellationToken = default);
}
