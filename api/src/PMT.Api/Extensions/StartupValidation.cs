using Microsoft.Extensions.Configuration;
using System.Text;
using PMT.Infrastructure.Identity;

namespace PMT.Api.Extensions;

/// <summary>
/// Fail-fast validation of security-sensitive configuration at application startup.
/// Rejects placeholder JWT signing keys, wildcard issuer/audience values, wildcard CORS
/// origins used with credentials, and unsafe production HTTP/HTTPS settings.
/// </summary>
public static class StartupValidation
{
    public const string PlaceholderSigningKey = "CHANGE-THIS-TO-A-STRONG-SECRET-AT-LEAST-32-BYTES";

    /// <summary>
    /// Validates the resolved configuration and throws <see cref="InvalidOperationException"/>
    /// when security-sensitive values are missing, placeholder, or unsafe for production.
    /// </summary>
    /// <param name="configuration">The resolved application configuration.</param>
    /// <param name="isDevelopment">Whether the active environment is Development.</param>
    /// <param name="isBootstrap">
    /// When true (admin bootstrap / password-reset CLI flows) JWT validation is skipped because
    /// those flows do not issue access tokens.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown when configuration is unsafe.</exception>
    public static void Validate(IConfiguration configuration, bool isDevelopment, bool isBootstrap = false)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        ValidateCors(configuration);
        if (!isBootstrap)
            ValidateJwt(configuration, isDevelopment);
        if (!isDevelopment)
            ValidateProductionConfiguration(configuration);
    }

    private static void ValidateCors(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (origins is null || origins.Length == 0)
        {
            throw new InvalidOperationException(
                "Startup validation failed: 'Cors:AllowedOrigins' is not configured. " +
                "Provide an explicit array of trusted origin URLs (e.g. https://app.example.com). " +
                "Wildcard '*' is not permitted when AllowCredentials() is enabled.");
        }

        if (origins.Any(o => string.Equals(o, "*", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Startup validation failed: 'Cors:AllowedOrigins' must not contain the wildcard '*'. " +
                "Wildcard origins cannot be combined with AllowCredentials(). " +
                "Configure exact origins for each trusted client.");
        }
    }

    private static void ValidateJwt(IConfiguration configuration, bool isDevelopment)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.SigningKey) ||
            jwt.SigningKey == PlaceholderSigningKey ||
            Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Startup validation failed: 'Jwt:SigningKey' is a placeholder or shorter than 32 bytes. " +
                "Configure a strong, random signing key (>= 32 bytes) via 'Jwt:SigningKey' in user-secrets " +
                "(Development) or the Jwt__SigningKey environment variable (Production).");
        }

        // In Production a wildcard issuer/audience is unsafe: it weakens token validation and
        // can allow tokens minted for other audiences to be accepted.
        if (!isDevelopment && (string.Equals(jwt.Issuer, "*", StringComparison.Ordinal) ||
                               string.Equals(jwt.Audience, "*", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Startup validation failed: 'Jwt:Issuer' and 'Jwt:Audience' must not be the wildcard '*' in production. " +
                "Configure explicit issuer/audience values.");
        }

        // JWT bearer metadata (token exchange over HTTP) must only be permitted in Development.
        var allowHttpMetadata = configuration.GetValue<bool>("Authentication:AllowHttpMetadata", false);
        if (!isDevelopment && allowHttpMetadata)
        {
            throw new InvalidOperationException(
                "Startup validation failed: 'Authentication:AllowHttpMetadata' is enabled outside Development. " +
                "JWT metadata must only flow over HTTPS in production.");
        }
    }

    private static void ValidateProductionConfiguration(IConfiguration configuration)
    {
        if (!configuration.GetValue("HttpsRedirection:Enabled", true))
        {
            throw new InvalidOperationException(
                "Startup validation failed: 'HttpsRedirection:Enabled' must be true in production. " +
                "HTTPS is required for all production traffic.");
        }
    }
}
