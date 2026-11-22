using System.Diagnostics;

namespace ProductShowcase.Api.Middleware;

public class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";
        var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var userEmail = context.User?.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "authenticated"
                : "anonymous";

            logger.LogInformation(
                "HTTP {Method} {Path}{Query} -> {StatusCode} ({ElapsedMs} ms) | IP: {RemoteIp} | User: {User}",
                method,
                path,
                query,
                statusCode,
                stopwatch.ElapsedMilliseconds,
                remoteIp,
                userEmail);
        }
    }
}
