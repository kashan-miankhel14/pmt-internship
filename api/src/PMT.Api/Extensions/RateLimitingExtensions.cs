using System.Linq;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PMT.Api.Extensions;

internal sealed class RateLimitingConfiguredMarker { }

public static class RateLimitingExtensions
{
    public static IServiceCollection AddPmtRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Prevent configuring rate limiting more than once
        if (services.Any(sd => sd.ServiceType == typeof(RateLimitingConfiguredMarker)))
            return services;

        var permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 100);
        var windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 60);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.PermitLimit = permitLimit;
                limiter.Window = TimeSpan.FromSeconds(windowSeconds);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
            options.AddFixedWindowLimiter("login", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(5);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });

            // Agent turns are expensive (embedding + multi-step LLM calls), so they get a
            // dedicated per-user budget instead of sharing the general "api" window.
            // Partitioning by user id rather than IP prevents one user on a shared NAT
            // from exhausting everyone else's quota. This requires UseRateLimiter() to run
            // after UseAuthentication() so HttpContext.User is populated; see Program.cs.
            var aiPermitLimit = configuration.GetValue("AiAgent:RequestsPerHourPerUser", 30);
            options.AddPolicy<string>("ai-agent", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = aiPermitLimit,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });

        // register marker so subsequent calls are no-ops
        services.AddSingleton<RateLimitingConfiguredMarker>();

        return services;
    }
}
