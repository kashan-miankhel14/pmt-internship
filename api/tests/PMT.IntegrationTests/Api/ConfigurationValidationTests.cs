using Microsoft.Extensions.Configuration;
using PMT.Api.Extensions;

namespace PMT.IntegrationTests.Api;

public sealed class ConfigurationValidationTests
{
    private static IConfiguration Build(params (string Key, string Value)[] values)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in values)
            dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private const string StrongKey = "test-only-signing-key-must-be-at-least-32-bytes-long";
    private const string PlaceholderKey = "CHANGE-THIS-TO-A-STRONG-SECRET-AT-LEAST-32-BYTES";

    [Fact]
    public void Development_rejects_placeholder_signing_key()
    {
        var cfg = Build(
            ("Jwt:SigningKey", PlaceholderKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "http://localhost:3000"));

        Assert.Throws<InvalidOperationException>(() =>
            StartupValidation.Validate(cfg, isDevelopment: true, isBootstrap: false));
    }

    [Fact]
    public void Development_allows_real_signing_key_with_explicit_cors()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "http://localhost:3000"),
            ("Authentication:AllowHttpMetadata", "true"));

        StartupValidation.Validate(cfg, isDevelopment: true, isBootstrap: false);
    }

    [Fact]
    public void Bootstrap_skips_jwt_placeholder_check()
    {
        var cfg = Build(
            ("Jwt:SigningKey", PlaceholderKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "http://localhost:3000"));

        StartupValidation.Validate(cfg, isDevelopment: false, isBootstrap: true);
    }

    [Fact]
    public void Rejects_wildcard_cors_origins()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "*"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, false, false));
    }

    [Fact]
    public void Rejects_wildcard_cors_origins_with_credentials()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "http://localhost:3000"),
            ("Cors:AllowedOrigins:1", "*"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, false, false));
    }

    [Fact]
    public void Rejects_missing_cors_origins()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, false, false));
    }

    [Fact]
    public void Production_rejects_wildcard_issuer()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "*"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "https://app.example.com"),
            ("HttpsRedirection:Enabled", "true"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, isDevelopment: false, isBootstrap: false));
    }

    [Fact]
    public void Production_rejects_https_redirection_disabled()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "https://app.example.com"),
            ("HttpsRedirection:Enabled", "false"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, isDevelopment: false, isBootstrap: false));
    }

    [Fact]
    public void Production_rejects_http_metadata()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "https://app.example.com"),
            ("Authentication:AllowHttpMetadata", "true"),
            ("HttpsRedirection:Enabled", "true"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, isDevelopment: false, isBootstrap: false));
    }

    [Fact]
    public void Production_accepts_secure_configuration()
    {
        var cfg = Build(
            ("Jwt:SigningKey", StrongKey),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "https://app.example.com"),
            ("Authentication:AllowHttpMetadata", "false"),
            ("HttpsRedirection:Enabled", "true"));

        StartupValidation.Validate(cfg, isDevelopment: false, isBootstrap: false);
    }

    [Fact]
    public void Rejects_short_signing_key()
    {
        var cfg = Build(
            ("Jwt:SigningKey", "short"),
            ("Jwt:Issuer", "PMT.Api"),
            ("Jwt:Audience", "PMT.Web"),
            ("Cors:AllowedOrigins:0", "http://localhost:3000"));

        Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(cfg, isDevelopment: true, isBootstrap: false));
    }
}
