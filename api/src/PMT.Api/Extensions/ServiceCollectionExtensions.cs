using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using PMT.Infrastructure.Identity;
using PMT.Application.Common.Security;

namespace PMT.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options => options.Filters.Add<Filters.ValidateModelAttribute>())
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Allow multipart uploads up to the per-endpoint [RequestSizeLimit]; the global request
        // body cap (PmtLimits.MaxRequestBodySizeBytes) still bounds all other endpoints.
        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = PmtLimits.MaxUploadSizeBytes);

        // Real database health check backed by a lightweight SQL query on the configured
        // connection string. Registered once (AddHealthChecks is idempotent) and consumed by
        // the /health endpoint mapped in Program.cs.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Startup validation failed: 'ConnectionStrings:DefaultConnection' is not configured.");
        services.AddHealthChecks()
            .AddSqlServer(connectionString, name: "sql-server", tags: ["db", "database"]);

        services.AddEndpointsApiExplorer();
        services.AddPmtSwagger();
        services.AddPmtRateLimiting(configuration);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
        if (origins.Length == 0 || origins.Any(o => o == "*"))
        {
            throw new InvalidOperationException("CORS configuration error: 'Cors:AllowedOrigins' must be an explicit array of origins. Wildcard '*' is not allowed with AllowCredentials(). Set exact origins in appsettings.Development.json or user secrets.");
        }
        services.AddCors(options => options.AddPolicy("PmtWeb", policy => policy
            .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !configuration.GetValue("Authentication:AllowHttpMetadata", false);
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrWhiteSpace(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            context.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build();
            foreach (var permission in PermissionRequirement.All)
                options.AddPolicy(permission, policy => policy.RequireClaim(PermissionRequirement.ClaimType, permission));
        });
        return services;
    }
}
