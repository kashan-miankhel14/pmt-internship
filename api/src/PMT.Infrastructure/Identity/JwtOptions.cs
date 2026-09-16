namespace PMT.Infrastructure.Identity;
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "PMT.Api";
    public string Audience { get; set; } = "PMT.Web";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
}
