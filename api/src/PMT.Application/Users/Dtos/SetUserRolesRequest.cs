namespace PMT.Application.Users.Dtos;
public sealed record SetUserRolesRequest(IReadOnlyCollection<long> RoleIds);
