using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

public sealed class Issue : AuditableEntity
{
    public long ProjectId { get; set; }
    public long? TaskId { get; set; }

    /// <summary>
    /// Per-project sequential issue number, allocated by SP_ISSUE from
    /// dbo.ProjectCounters. Combined with the project key it forms "PMT-1".
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Read-only projection of dbo.Project.[Key], hydrated by the SP_ISSUE
    /// FETCH/PAGED join. Not a column on dbo.Issue and never written back.
    /// </summary>
    public string? ProjectKey { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IssueSeverity Severity { get; set; } = IssueSeverity.Medium;
    public IssueStatus Status { get; set; } = IssueStatus.Open;
    public long ReportedByUserId { get; set; }
    public long? AssignedToUserId { get; set; }
    public long? TeamId { get; set; }
    public DateTime? ResolvedDate { get; set; }

    /// <summary>
    /// Denormalised link to <c>dbo.WorkflowStatus</c> for the workflow-resolved status. Not
    /// enforced; the authoritative status is still <see cref="Status"/>. Stamped by the workflow
    /// engine so the richer catalog status (e.g. In QA) is observable even though the legacy enum
    /// column cannot express it.
    /// </summary>
    public long? WorkflowStatusId { get; set; }
}
