namespace PMT.Domain.Entities;

/// <summary>
/// Per-project source of sequential issue numbers used to build keys like "PMT-42".
/// Mirrors core.ProjectCounters.
/// </summary>
/// <remarks>
/// This entity intentionally does not derive from <see cref="Common.AuditableEntity"/>:
/// the table is a one-row-per-project counter keyed by ProjectId, with no surrogate Id
/// and no audit or soft-delete columns. <see cref="LastIssueNumber"/> must only ever be
/// advanced by the atomic increment in the issue-creation stored procedure, never by a
/// read-then-write from application code.
/// </remarks>
public sealed class ProjectCounter
{
    /// <summary>Primary key; one counter row per project.</summary>
    public long ProjectId { get; set; }

    /// <summary>Highest issue number handed out so far; 0 for a new project.</summary>
    public int LastIssueNumber { get; set; }
}
