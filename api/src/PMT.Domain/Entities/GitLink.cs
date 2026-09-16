using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

public sealed class GitLink : AuditableEntity
{
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
    public long? IssueId { get; set; }
    public GitProvider Provider { get; set; }
    public string? CommitSha { get; set; }
    public string? PullRequestUrl { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public string RepositoryUrl { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get => CommitSha ?? string.Empty; set => CommitSha = value; }
    public string? ReferenceUrl { get => PullRequestUrl; set => PullRequestUrl = value; }
}
