using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

public sealed class Sprint : AuditableEntity
{
    public long ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public SprintStatus Status { get; set; } = SprintStatus.Planned;
}
