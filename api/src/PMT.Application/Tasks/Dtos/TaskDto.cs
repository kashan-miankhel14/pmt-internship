using TaskStatus = PMT.Domain.Enums.TaskStatus;
namespace PMT.Application.Tasks.Dtos;

public sealed record TaskDto(long Id, long ProjectId, long? UserStoryId, string Title, string? Description, TaskStatus Status, int Priority, long? AssignedToUserId, decimal? EstimatedHours, decimal? ActualHours,
    DateTime? DueDate, DateTime? CompletedDate, bool Active, long? TeamId = null);
