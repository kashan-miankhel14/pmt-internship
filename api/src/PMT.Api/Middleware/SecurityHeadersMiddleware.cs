namespace PMT.Api.Middleware;
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers=context.Response.Headers;
        headers["X-Content-Type-Options"]="nosniff";
        headers["X-Frame-Options"]="DENY";
        headers["Referrer-Policy"]="no-referrer";
        headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] = context.Request.Path.StartsWithSegments("/swagger")
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'"
            : "default-src 'none'; frame-ancestors 'none'";
        await next(context);
    }
}
