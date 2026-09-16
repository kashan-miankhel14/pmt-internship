using PMT.Domain.Common;
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Domain.Entities;

public sealed class TaskItem : AuditableEntity
{
    public long StoryId { get; set; }
    public long? AssigneeUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.ToDo;
    public decimal? EstimateHours { get; set; }
    public decimal? ActualHours { get; set; }
    public DateTime? DueDate { get; set; }
    public long ProjectId { get; set; }
    public long? UserStoryId { get => StoryId; set => StoryId = value ?? 0; }
    public int Priority { get; set; } = 3;
    public long? AssignedToUserId { get => AssigneeUserId; set => AssigneeUserId = value; }
    public decimal? EstimatedHours { get => EstimateHours; set => EstimateHours = value; }
    public DateTime? CompletedDate { get; set; }

    /// <summary>
    /// Optional assigned team. An item can be assigned to a team, an individual user, or both.
    /// </summary>
    public long? TeamId { get; set; }

    /// <summary>
    /// Denormalised link to <c>dbo.WorkflowStatus</c> for the workflow-resolved status. See
    /// <see cref="Issue.WorkflowStatusId"/> for the rationale.
    /// </summary>
    public long? WorkflowStatusId { get; set; }
}
