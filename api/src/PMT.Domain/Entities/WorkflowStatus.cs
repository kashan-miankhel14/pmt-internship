using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

/// <summary>
/// A status within a workflow. The Code is the stable key used by the engine and the API;
/// Name is the human label; Category drives done-date stamping.
/// </summary>
public sealed class WorkflowStatus : AuditableEntity
{
    public long? WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public WorkflowStatusCategory Category { get; set; } = WorkflowStatusCategory.ToDo;
    public bool IsInitial { get; set; }
    public int Order { get; set; }
}
