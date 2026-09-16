using TaskStatus = PMT.Domain.Enums.TaskStatus;
namespace PMT.Application.Tasks.Dtos;

public sealed record UpsertTaskRequest(long ProjectId, long? UserStoryId, string Title, string? Description, TaskStatus Status, int Priority, long? AssignedToUserId, decimal? EstimatedHours, decimal? ActualHours, DateTime? DueDate, bool Active = true, string? Comment = null, long? TeamId = null);
