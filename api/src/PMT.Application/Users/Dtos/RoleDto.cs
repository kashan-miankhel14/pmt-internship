namespace PMT.Application.Users.Dtos;
public sealed record RoleDto(long Id, string Name, string? Description, bool IsSystemRole);
