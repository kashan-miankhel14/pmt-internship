namespace PMT.Application.Users.Dtos;

public sealed record UserDto(long Id, long? DepartmentId, string UserName, string Email, string DisplayName, bool IsLocked, bool Active, long RoleId = 0);
