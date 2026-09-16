using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

public sealed class JobQueueItem : AuditableEntity
{
    public string JobType { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public DateTime? ProcessedDate { get; set; }
    public string PayloadJson { get => Payload ?? "{}"; set => Payload = value; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get => ProcessedDate; set => ProcessedDate = value; }
    public string? LastError { get; set; }
}
