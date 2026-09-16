using System.Net;
using System.Text.Json;
using PMT.Domain.Exceptions;
namespace PMT.Api.Middleware;
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IWebHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            var (status, title, errors) = ex switch
            {
                NotFoundException => (HttpStatusCode.NotFound, "Resource not found", Array.Empty<string>()),
                PMT.Domain.Exceptions.ValidationException validation => (HttpStatusCode.BadRequest, "Validation failed", validation.Errors.ToArray()),
                ForbiddenAccessException forbidden => (HttpStatusCode.Forbidden, forbidden.Message, Array.Empty<string>()),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized", Array.Empty<string>()),
                // The AI backend being down is an expected, retryable condition rather than
                // a server fault, so it is reported as 503 with an actionable message.
                AiServiceUnavailableException unavailable => (HttpStatusCode.ServiceUnavailable, unavailable.Message, Array.Empty<string>()),
                AiAgentException agent => (HttpStatusCode.InternalServerError, env.IsDevelopment() ? agent.Message : "Anna could not complete the request.", Array.Empty<string>()),
                _ => (HttpStatusCode.InternalServerError, env.IsDevelopment() ? "An unexpected error occurred: " + ex.Message : "An unexpected error occurred.",
                      env.IsDevelopment() ? new[] { ex.Message } : Array.Empty<string>())
            };
            if ((int)status >= 500) logger.LogError(ex, "Unhandled request exception. TraceId: {TraceId}", context.TraceIdentifier);
            else logger.LogWarning(ex, "Request failed with status {StatusCode}. Errors: {Errors}. TraceId: {TraceId}",
                (int)status,
                ex is PMT.Domain.Exceptions.ValidationException validationFailure ? string.Join(" | ", validationFailure.Errors) : "none",
                context.TraceIdentifier);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                type = $"https://httpstatuses.com/{(int)status}", title, status=(int)status,
                traceId=context.TraceIdentifier, errors
            }, cancellationToken: context.RequestAborted);
        }
    }
}
