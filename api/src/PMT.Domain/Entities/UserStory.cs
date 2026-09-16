using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

public sealed class UserStory : AuditableEntity
{
    public long ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public StoryStatus Status { get; set; } = StoryStatus.Backlog;
    public int Priority { get; set; } = 3;
    public decimal? StoryPoints { get; set; }
    public long? AssigneeUserId { get; set; }

    /// <summary>
    /// Sprint the story is committed to, or <c>null</c> when it sits in the backlog.
    /// Not an enforced foreign key: see 0017_SprintsAndBoards.sql.
    /// </summary>
    public long? SprintId { get; set; }

    /// <summary>
    /// Optional assigned team.
    /// </summary>
    public long? TeamId { get; set; }

    /// <summary>
    /// Denormalised link to <c>dbo.WorkflowStatus</c> for the workflow-resolved status. See
    /// <see cref="Issue.WorkflowStatusId"/> for the rationale.
    /// </summary>
    public long? WorkflowStatusId { get; set; }
}
