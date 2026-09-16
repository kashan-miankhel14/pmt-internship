using PMT.Domain.Enums;
namespace PMT.Application.UserStories.Dtos;

/// <summary>
/// Read model for a user story. <paramref name="SprintId"/> is null when the story sits in the
/// backlog; it is projected so a client can read back the sprint it just committed the story to
/// instead of having to guess that the write took.
/// </summary>
public sealed record UserStoryDto(long Id, long ProjectId, string Title, string? Description, string? AcceptanceCriteria, StoryStatus Status, int Priority, decimal? StoryPoints, long? AssignedToUserId, string? AssigneeName, bool Active, long? SprintId, long? TeamId = null);
