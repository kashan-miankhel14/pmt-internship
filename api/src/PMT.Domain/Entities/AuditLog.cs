using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class AuditLog : AuditableEntity
{
    public string EntityName { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public long UserId { get; set; }
    public string EntityType { get => EntityName; set => EntityName = value; }
    public string? OldValuesJson { get => OldValue; set => OldValue = value; }
    public string? NewValuesJson { get => NewValue; set => NewValue = value; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
