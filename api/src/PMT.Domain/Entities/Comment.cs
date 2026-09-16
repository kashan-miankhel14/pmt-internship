using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class Comment : AuditableEntity
{
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
    public long? IssueId { get; set; }
    public long? UserStoryId { get; set; }
    public long UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public string Body { get => Content; set => Content = value; }
}
