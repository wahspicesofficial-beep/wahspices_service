using System.Diagnostics;
using System.Text.Json;
using WahSpices.Application.Common;

namespace WahSpices.API.Middleware;

/// <summary>
/// Last-chance error handler. Clients get a generic message plus a correlation id (also sent as the
/// X-Correlation-Id header); the exception, stack trace and inner details go ONLY to the server log under that id.
/// Development additionally shows the exception message to speed up debugging — never a stack trace.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            return Task.CompletedTask;
        });

        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            // Deliberate, user-facing business errors — their message is written for the customer.
            _logger.LogWarning("AppException {Code} on {Method} {Path} [{CorrelationId}]: {Message}",
                ex.ErrorCode, context.Request.Method, context.Request.Path, correlationId, ex.Message);
            await WriteErrorAsync(context, ex.StatusCode, ex.ErrorCode, ex.Message, ex.Field);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // client went away — nothing to send
        }
        catch (BadHttpRequestException ex)   // body over the size limit (413), malformed request (400)
        {
            _logger.LogWarning("Bad request on {Method} {Path} [{CorrelationId}]: {Status}",
                context.Request.Method, context.Request.Path, correlationId, ex.StatusCode);
            var tooLarge = ex.StatusCode == StatusCodes.Status413PayloadTooLarge;
            await WriteErrorAsync(context, ex.StatusCode, tooLarge ? "PAYLOAD_TOO_LARGE" : "BAD_REQUEST",
                tooLarge ? "The request is too large." : "The request could not be read.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path} [{CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            var message = _env.IsDevelopment()
                ? $"{ex.GetType().Name}: {ex.Message} (ref {correlationId})"
                : $"Something went wrong on our side. Please try again later. If it keeps happening, contact support with reference {correlationId}.";

            await WriteErrorAsync(context, 500, "INTERNAL_SERVER_ERROR", message);
        }
    }

    private static async Task WriteErrorAsync(HttpContext ctx, int statusCode, string code, string message, string? field = null)
    {
        if (ctx.Response.HasStarted) return;   // can't change a response that's already streaming
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = statusCode;

        var response = ApiResponse<object>.Fail(code, message, field);
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(response, Json));
    }
}
