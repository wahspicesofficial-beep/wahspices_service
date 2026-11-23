namespace WahSpices.API.Middleware;

/// <summary>
/// Adds browser security headers to EVERY response (API JSON, errors, static /uploads files)
/// and removes server fingerprint headers. Registered by app.UseHttpSecurity().
/// </summary>
public class SecurityHeadersMiddleware
{
    // The API returns JSON (and a printable payslip HTML) — nothing may load, run or frame it.
    private const string ApiCsp =
        "default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    // User-uploaded files: even if an SVG/HTML slipped in, opening it directly runs nothing (sandbox = unique origin, no scripts).
    private const string UploadsCsp = "default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'; sandbox";

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isUploads = path.StartsWithSegments("/uploads");
        var isSwagger = path.StartsWithSegments("/swagger");

        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h.Remove("Server");
            h.Remove("X-Powered-By");
            h.Remove("X-AspNet-Version");
            h.Remove("X-AspNetMvc-Version");

            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
            h["X-Frame-Options"] = "DENY";
            h["X-Permitted-Cross-Domain-Policies"] = "none";

            if (isUploads)
            {
                h["Content-Security-Policy"] = UploadsCsp;
                // Product/blog images are embedded by the website (and emails) from another origin.
                h["Cross-Origin-Resource-Policy"] = "cross-origin";
            }
            else if (!(isSwagger && _env.IsDevelopment()))   // Swagger UI (dev only) needs its scripts
            {
                h["Content-Security-Policy"] = ApiCsp;
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                // CORP only affects no-cors loads (<img>, <script>); CORS fetches from the website are unaffected.
                h["Cross-Origin-Resource-Policy"] = "same-origin";
                // API data is per-user — never let a shared proxy/browser cache it unless the endpoint opted in.
                if (!h.ContainsKey("Cache-Control")) h["Cache-Control"] = "no-store";
            }
            return Task.CompletedTask;
        });

        return _next(context);
    }
}
