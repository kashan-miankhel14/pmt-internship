using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PMT.Application.Common.Interfaces;

namespace PMT.Infrastructure.Identity;

public sealed class JwtTokenGenerator(IOptions<JwtOptions> options) : IJwtTokenGenerator
{
    public GeneratedToken Generate(long userId, string userName, string email, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var settings = options.Value;
        if (Encoding.UTF8.GetByteCount(settings.SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var jwtId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, jwtId),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userName)
        };
        claims.AddRange(roles.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => new Claim(ClaimTypes.Role, x)));
        claims.AddRange(permissions.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => new Claim("permission", x)));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now, expires, credentials);
        return new GeneratedToken(new JwtSecurityTokenHandler().WriteToken(token), jwtId, expires);
    }
}
