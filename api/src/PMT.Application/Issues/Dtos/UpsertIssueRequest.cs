using PMT.Domain.Enums;
namespace PMT.Application.Issues.Dtos;

public sealed record UpsertIssueRequest(long ProjectId, long? TaskId, string Title, string? Description, IssueSeverity Severity, IssueStatus Status, long? ReportedByUserId, long? AssignedToUserId, bool Active = true, string? Comment = null, long? TeamId = null);
