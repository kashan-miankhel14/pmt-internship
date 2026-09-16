using PMT.Infrastructure.Security;
namespace PMT.Api.Middleware;
public sealed class IpWhitelistMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, IpWhitelistProvider provider)
    {
        if (!configuration.GetValue("Security:EnableIpWhitelist", false) || context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context); return;
        }
        var address=context.Connection.RemoteIpAddress;
        if (address is null || !await provider.IsAllowedAsync(address, context.RequestAborted))
        {
            context.Response.StatusCode=StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message="This IP address is not allowed." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
