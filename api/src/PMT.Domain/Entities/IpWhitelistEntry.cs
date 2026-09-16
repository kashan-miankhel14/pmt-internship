using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class IpWhitelistEntry : AuditableEntity
{
    public string CidrRange { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Cidr { get => CidrRange; set => CidrRange = value; }
}
