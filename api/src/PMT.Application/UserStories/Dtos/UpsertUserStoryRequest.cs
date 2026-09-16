using PMT.Domain.Enums;
namespace PMT.Application.UserStories.Dtos;

public sealed record UpsertUserStoryRequest(long ProjectId, string Title, string? Description, string? AcceptanceCriteria, StoryStatus Status, int Priority, decimal? StoryPoints, long? AssignedToUserId, long? SprintId = null, bool Active = true, string? Comment = null, long? TeamId = null);
