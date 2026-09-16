using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class Permission : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Key { get => Name; set => Name = value; }
    public string Module { get; set; } = string.Empty;
}
