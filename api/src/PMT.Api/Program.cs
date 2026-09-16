using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PMT.Api.Extensions;
using PMT.Api.Middleware;
using PMT.Application;
using PMT.Infrastructure;
using PMT.Infrastructure.RealTime;
using PMT.Infrastructure.Persistence;

using Serilog;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
var isBootstrap = args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--reset-admin-password", StringComparer.OrdinalIgnoreCase);

if ((builder.Environment.IsDevelopment() || isBootstrap) &&
    string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
{
    builder.Configuration["Jwt:SigningKey"] =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

StartupValidation.Validate(builder.Configuration, builder.Environment.IsDevelopment(), isBootstrap);

// Configure Serilog
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Response compression. The API's payloads are JSON list/report responses, which compress by
// roughly an order of magnitude. Brotli is preferred and gzip is the fallback for clients
// that do not advertise "br".
builder.Services.AddResponseCompression(options =>
{
    // Required for this to do anything in practice: the API is served over HTTPS. Safe here
    // because PMT authenticates with bearer tokens in headers rather than cookies, and the
    // endpoints that return tokens in the body are excluded from the branch below.
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    // ResponseCompressionDefaults already covers application/json; problem+json is what
    // ExceptionHandlingMiddleware emits and is not in the default list.
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
});

// Fastest, not Optimal: these are dynamic, uncacheable responses, so CPU per response matters
// more than the last few percent of ratio. On JSON the size difference is marginal.
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// CORS policy is configured centrally in AddApiServices (from configuration) so it is
// validated and consistent across environments. Development overrides origins via user-secrets.


// Data Protection keys persistence
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys")))
    .SetApplicationName("PMT");

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPmtRateLimiting(builder.Configuration);

builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

if (args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var userId = await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().BootstrapAsync();
    app.Logger.LogInformation("Bootstrap administrator created with ID {UserId}.", userId);
    return;
}

if (args.Contains("--reset-admin-password", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var result = await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().ResetAdminPasswordAsync();
    if (result > 0)
        app.Logger.LogInformation("Administrator password has been reset.");
    else
        app.Logger.LogWarning("No active administrator found to reset. Ensure bootstrapping has already run.");
    return;
}

// Forwarded headers let the app recover the real client IP/scheme when it runs behind a
// reverse proxy (TLS termination, load balancer). Honoring X-Forwarded-* from *any* source
// would let a remote client forge X-Forwarded-For and defeat IpWhitelistMiddleware, which
// trusts context.Connection.RemoteIpAddress. So we only accept these headers from proxies we
// explicitly trust. KnownProxies/KnownNetworks come from configuration
// (ForwardedHeaders:KnownProxies / ForwardedHeaders:KnownNetworks); when nothing is
// configured we fall back to loopback only, which is safe for a direct or same-host
// deployment and cannot be spoofed from off-box.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // Only the first hop is trusted by default; raise this only for a known proxy chain.
    ForwardLimit = app.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1)
};

// Replace the framework's default trust list with an explicit, config-driven one so nothing
// is trusted implicitly. (KnownIPNetworks is the non-obsolete .NET 10 replacement for
// KnownNetworks and shares its backing list.)
forwardedOptions.KnownProxies.Clear();
forwardedOptions.KnownIPNetworks.Clear();

foreach (var proxy in app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    if (System.Net.IPAddress.TryParse(proxy, out var ip))
        forwardedOptions.KnownProxies.Add(ip);

foreach (var network in app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    if (System.Net.IPNetwork.TryParse(network, out var cidr))
        forwardedOptions.KnownIPNetworks.Add(cidr);

// No trusted proxies configured: trust loopback only. A direct/same-host deployment still
// resolves scheme correctly, and remote clients cannot spoof X-Forwarded-For because their
// connection does not originate from loopback.
if (forwardedOptions.KnownProxies.Count == 0 && forwardedOptions.KnownIPNetworks.Count == 0)
{
    forwardedOptions.KnownProxies.Add(System.Net.IPAddress.Loopback);      // 127.0.0.1
    forwardedOptions.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);  // ::1
}

app.UseForwardedHeaders(forwardedOptions);

// Enforce a global request-body size limit early (before the body is read by any endpoint).
// The upload endpoint raises the limit via [RequestSizeLimit] and FormOptions.MultipartBodyLengthLimit
// is raised in AddApiServices to match.
app.Use(async (context, next) =>
{
    context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize =
        PmtLimits.MaxRequestBodySizeBytes;
    await next();
});

// Compression sits outside the exception handler so error bodies are compressed too, and
// outside CORS/auth so it applies to every response that carries a compressible body.
// Two branches are excluded:
//   /api/v1/auth - responses carry refresh tokens, and compressing a secret alongside any
//                  attacker-influenced content is the BREACH precondition. These payloads are
//                  a few hundred bytes, so nothing is lost by skipping them.
//   /hubs        - SignalR negotiates its own transports; its content types are not in the
//                  compressible list anyway and buffering them here only adds latency.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api/v1/auth")
               && !context.Request.Path.StartsWithSegments("/hubs"),
    branch => branch.UseResponseCompression());

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Configuration.GetValue("HttpsRedirection:Enabled", true)) app.UseHttpsRedirection();
app.UseCors("PmtWeb");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
// Rate limiting runs after authentication so user-partitioned policies (for example
// "ai-agent", which budgets per user id) can read HttpContext.User. Placing it earlier
// would silently degrade those policies to per-IP partitioning.
app.UseRateLimiter();
app.UseMiddleware<IpWhitelistMiddleware>();
app.UseMiddleware<AuditContextMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new { status = report.Status.ToString(), checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description }) });
        await context.Response.WriteAsync(result);
    }
}).AllowAnonymous();

app.Run();

public partial class Program;
