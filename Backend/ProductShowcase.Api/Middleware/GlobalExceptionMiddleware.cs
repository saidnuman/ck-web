using System.Net;
using System.Text.Json;

namespace ProductShowcase.Api.Middleware;

public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, "Geçersiz İstek"),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Kayıt Bulunamadı"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Yetkisiz Erişim"),
            InvalidOperationException => (HttpStatusCode.Conflict, "İşlem Hatası"),
            _ => (HttpStatusCode.InternalServerError, "Sunucu Hatası")
        };

        logger.LogError(
            exception,
            "Yakalanmamış hata oluştu | Method: {Method} | Path: {Path} | Status: {StatusCode} | TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
            (int)statusCode,
            context.TraceIdentifier);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = (int)statusCode;

        var detailedMessage = exception.InnerException is not null
            ? $"{exception.Message} ({exception.InnerException.Message})"
            : exception.Message;

        var errorPayload = new
        {
            status = (int)statusCode,
            title,
            message = statusCode == HttpStatusCode.InternalServerError && !environment.IsDevelopment()
                ? "Beklenmeyen bir sunucu hatası oluştu. Lütfen daha sonra tekrar deneyin."
                : detailedMessage,
            traceId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
