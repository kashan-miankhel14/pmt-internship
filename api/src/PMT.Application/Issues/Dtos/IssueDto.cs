using PMT.Domain.Enums;
namespace PMT.Application.Issues.Dtos;

public sealed record IssueDto(long Id, int Number, long ProjectId, long? TaskId, string Title, string? Description, IssueSeverity Severity, IssueStatus Status, long? ReportedByUserId, long? AssignedToUserId, DateTime? ResolvedDate, bool Active, string? ProjectKey = null, long? TeamId = null)
{
    /// <summary>Jira-style human key, e.g. "PMT-1". Falls back to the raw number when the project key is unknown.</summary>
    public string Key => ProjectKey is null ? Number.ToString() : $"{ProjectKey}-{Number}";
}
