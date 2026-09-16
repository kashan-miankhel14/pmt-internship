using PMT.Domain.Enums;
namespace PMT.Application.Projects.Dtos;

public sealed record UpsertProjectRequest(string Key, string Name, string? Description, long? OwnerUserId, ProjectStatus Status, DateOnly? StartDate, DateOnly? TargetDate, bool Active = true, long? DepartmentId = null);
