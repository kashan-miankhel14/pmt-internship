using PMT.Domain.Common;

namespace PMT.Domain.Entities;

/// <summary>
/// Binds a project to a workflow. Each project may have at most one live scheme; when none
/// exists the engine falls back to the default workflow (and the application services fall back
/// to the original hardcoded status rules).
/// </summary>
public sealed class WorkflowScheme : AuditableEntity
{
    public long ProjectId { get; set; }
    public long WorkflowId { get; set; }
}
