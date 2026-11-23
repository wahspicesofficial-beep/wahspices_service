using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using System.Security.Claims;

namespace WahSpices.API.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;

    public AuditLogMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // Only log mutating admin actions
        if (context.Request.Path.StartsWithSegments("/api/admin") &&
            context.Request.Method is "POST" or "PUT" or "DELETE" or "PATCH" &&
            context.Response.StatusCode < 400)
        {
            using var scope = context.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            var userName = context.User.FindFirstValue(ClaimTypes.Name) ?? "unknown";

            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                UserName = userName,
                Module = ExtractModule(context.Request.Path),
                Action = context.Request.Method.ToLower(),
                Details = $"{context.Request.Method} {context.Request.Path}",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString()
            });

            await db.SaveChangesAsync();
        }
    }

    private static string ExtractModule(string path)
    {
        var parts = path.ToString().Split('/');
        return parts.Length >= 4 ? parts[3] : "general";
    }
}
