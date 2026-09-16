namespace PMT.Application.Auth.Dtos;

public sealed record LoginResult(
    long UserId,
    string UserName,
    string DisplayName,
    string Email,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
