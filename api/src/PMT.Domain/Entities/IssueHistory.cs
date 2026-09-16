using PMT.Domain.Common;

namespace PMT.Domain.Entities;

/// <summary>
/// Generic change-log for the work items. Despite the name it records changes to Issues, Tasks
/// and UserStories through (EntityType, EntityId). This mirrors the soft/external-key style used
/// by <c>Comment</c> so the three item types can share one log without a union table.
/// </summary>
public sealed class IssueHistory : AuditableEntity
{
    public string EntityType { get; set; } = "Issue";

    /// <summary>Id of the Issue / Task / UserStory this row describes.</summary>
    public long EntityId { get; set; }

    /// <summary>When this change was a status transition, the transition that produced it.</summary>
    public long? WorkflowTransitionId { get; set; }

    /// <summary>The field that changed, e.g. "Status", "AssigneeUserId", "Title".</summary>
    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>Optional free-text note (e.g. the blocker explanation on a Block transition).</summary>
    public string? Comment { get; set; }

    public long? ChangedByUser { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
