using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class Notification : AuditableEntity
{
    public long UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string Type { get => EventType; set => EventType = value; }
    public string Title { get; set; } = string.Empty;
    public string? Link { get; set; }
    public DateTime? ReadDate { get; set; }
}
