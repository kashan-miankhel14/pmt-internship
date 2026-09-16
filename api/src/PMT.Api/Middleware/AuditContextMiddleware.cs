using PMT.Application.Common.Interfaces;

namespace PMT.Api.Middleware;

public sealed class AuditContextMiddleware(RequestDelegate next, ILogger<AuditContextMiddleware> logger)
{
    private static readonly HashSet<string> AuditedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete
    };

    public async Task InvokeAsync(HttpContext context, IAuditService auditService)
    {
        context.Response.Headers.Append("X-Correlation-ID", context.TraceIdentifier);
        await next(context);

        if (!AuditedMethods.Contains(context.Request.Method) ||
            context.Response.StatusCode >= StatusCodes.Status400BadRequest ||
            context.User.Identity?.IsAuthenticated != true ||
            context.Request.Path.StartsWithSegments("/api/v1/auth"))
            return;

        try
        {
            var controller = context.Request.RouteValues.TryGetValue("controller", out var value)
                ? value?.ToString() ?? "Unknown"
                : "Unknown";
            long? entityId = context.Request.RouteValues.TryGetValue("id", out var idValue) &&
                             long.TryParse(idValue?.ToString(), out var parsed)
                ? parsed
                : null;

            // The request envelope is the only payload available at this point. Record it as the new
            // value for creates/updates and as the old value for deletes, so AuditLog.NewValue /
            // OldValue describe the change instead of leaving both columns empty.
            var payload = new
            {
                Method = context.Request.Method,
                Path = context.Request.Path.Value,
                Query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                StatusCode = context.Response.StatusCode,
                context.TraceIdentifier
            };
            var isDelete = HttpMethods.IsDelete(context.Request.Method);

            await auditService.WriteAsync(context.Request.Method, controller, entityId,
                oldValues: isDelete ? payload : null,
                newValues: isDelete ? null : payload,
                cancellationToken: context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to write audit record for trace {TraceId}", context.TraceIdentifier);
        }
    }
}
