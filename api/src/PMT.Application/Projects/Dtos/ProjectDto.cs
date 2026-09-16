using PMT.Domain.Enums;
namespace PMT.Application.Projects.Dtos;

public sealed record ProjectDto(long Id, string Key, string Name, string? Description, long? OwnerUserId, ProjectStatus Status, DateOnly? StartDate, DateOnly? TargetDate, bool Active, long DepartmentId = 0);
