using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.Workflow.Engine;

/// <summary>Outcome of executing a transition through the engine.</summary>
public sealed class ExecuteResult
{
    public required WorkflowTransition Transition { get; init; }
    public required string ToStatusCode { get; init; }
    public required string ToStatusName { get; init; }
    public required WorkflowStatusCategory ToCategory { get; init; }
    public long? ToStatusId { get; init; }

    /// <summary>
    /// True when the item enters a Done-category status (DONE / CLOSED / CANCELLED). The calling
    /// service stamps the relevant completion date (ResolvedDate / CompletedDate) when this is set
    /// and clears it when it is not, which is exactly the bookkeeping the legacy services already do.
    /// </summary>
    public bool StampDoneDate => ToCategory == WorkflowStatusCategory.Done;
}
