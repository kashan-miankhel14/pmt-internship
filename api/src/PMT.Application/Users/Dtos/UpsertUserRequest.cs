namespace PMT.Application.Users.Dtos;

public sealed record UpsertUserRequest(long? DepartmentId, string UserName, string Email, string DisplayName, string? Password, bool Active = true, long? RoleId = null);
