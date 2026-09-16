using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class NotificationPreference : AuditableEntity
{
    public long UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public string NotificationType { get => EventType; set => EventType = value; }
}
