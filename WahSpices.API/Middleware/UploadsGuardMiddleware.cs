namespace WahSpices.API.Middleware;

/// <summary>
/// Hardens the public /uploads/** folder served by UseStaticFiles:
///  - only image/video extensions are ever served (an .html/.svg/.js that somehow lands there is a 404);
///  - nosniff + a sandboxing CSP so a file can never be interpreted as a page or run script on our origin.
/// Static files are never executed by ASP.NET Core; staff documents and return photos live outside wwwroot.
/// </summary>
public class UploadsGuardMiddleware
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".mp4", ".webm", ".mov", ".avi", ".mkv" };

    private readonly RequestDelegate _next;
    public UploadsGuardMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(ctx.Request.Path.Value ?? "");
            if (!Allowed.Contains(ext))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            ctx.Response.OnStarting(() =>
            {
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
                return Task.CompletedTask;
            });
        }
        return _next(ctx);
    }
}
