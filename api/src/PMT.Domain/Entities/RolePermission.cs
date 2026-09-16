using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class RolePermission : AuditableEntity
{
    public long RoleId { get; set; }
    public long PermissionId { get; set; }
}
