using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

public sealed class Project : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long DepartmentId { get; set; }
    public long OwnerUserId { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    public string Key { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetDate { get; set; }
}
