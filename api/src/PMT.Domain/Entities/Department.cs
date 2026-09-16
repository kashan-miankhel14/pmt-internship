using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class Department : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
}
