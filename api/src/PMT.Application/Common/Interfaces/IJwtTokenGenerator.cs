namespace PMT.Application.Common.Interfaces;

public sealed record GeneratedToken(string AccessToken, string JwtId, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    GeneratedToken Generate(long userId, string userName, string email, IEnumerable<string> roles, IEnumerable<string> permissions);
}
