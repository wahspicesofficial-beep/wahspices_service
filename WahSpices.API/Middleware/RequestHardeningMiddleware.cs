using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Options;
using WahSpices.API.Extensions;

namespace WahSpices.API.Middleware;

/// <summary>
/// Cheap request-level guards that run before anything else touches the request:
///  • only normal HTTP methods (TRACE / TRACK / CONNECT / WebDAV verbs → 405)
///  • X-Real-IP is overwritten with the proxy-validated client IP, so a script cannot fake its IP
///    to dodge rate limiting or the abuse guard
///  • per-request body-size limit: small JSON default, larger only for multipart uploads
///  • /uploads serves only media/PDF file types (no .html/.svg/.js/.json executed from the upload folder)
/// </summary>
public class RequestHardeningMiddleware
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
        { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };

    private static readonly HashSet<string> UploadExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".ico", ".mp4", ".webm", ".mov", ".avi", ".mkv", ".pdf" };

    private readonly RequestDelegate _next;
    private readonly HttpSecurityOptions _opt;

    public RequestHardeningMiddleware(RequestDelegate next, IOptions<HttpSecurityOptions> opt)
    {
        _next = next;
        _opt = opt.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var req = context.Request;

        if (!AllowedMethods.Contains(req.Method))
        {
            context.Response.Headers.Allow = "GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS";
            await WriteError(context, 405, "METHOD_NOT_ALLOWED", "HTTP method not allowed.");
            return;
        }

        // Client IP: UseForwardedHeaders has already resolved RemoteIpAddress from X-Forwarded-For — but only
        // for trusted proxies (Security:KnownProxies/KnownNetworks + loopback). Whatever the client sent in
        // X-Real-IP is replaced by that resolved address, because AspNetCoreRateLimit trusts X-Real-IP blindly:
        // without this a script could send a random X-Real-IP per request and never be rate-limited.
        var remote = context.Connection.RemoteIpAddress;
        req.Headers.Remove("X-Forwarded-For");
        req.Headers.Remove("X-Forwarded-Host");
        if (remote != null)
            req.Headers["X-Real-IP"] = (remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString();
        else
            req.Headers.Remove("X-Real-IP");

        if (req.Path.StartsWithSegments("/uploads"))
        {
            var ext = Path.GetExtension(req.Path.Value ?? "");
            if (!UploadExts.Contains(ext) || (req.Path.Value ?? "").Contains("..", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 404;
                return;
            }
        }

        // Body-size: endpoints with [RequestSizeLimit]/[DisableRequestSizeLimit] keep their own limit
        // (MVC applies it later); everything else gets the small default, multipart uploads a larger one.
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            var endpointLimit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
            if (endpointLimit == null)
            {
                sizeFeature.MaxRequestBodySize = req.HasFormContentType && (req.ContentType ?? "").Contains("multipart/", StringComparison.OrdinalIgnoreCase)
                    ? _opt.Limits.MaxMultipartBodyBytes
                    : _opt.Limits.MaxRequestBodyBytes;
            }
        }
        // Fast reject when the client announces an oversized body up-front.
        var limit = sizeFeature?.MaxRequestBodySize;
        if (limit != null && req.ContentLength > limit)
        {
            await WriteError(context, 413, "PAYLOAD_TOO_LARGE", $"Request body too large (max {limit / 1024} KB).");
            return;
        }

        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            if (!context.Response.HasStarted)
                await WriteError(context, 413, "PAYLOAD_TOO_LARGE", "Request body too large.");
        }
    }

    internal static Task WriteError(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            success = false,
            error = new { code, message },
        });
        return ctx.Response.WriteAsync(json);
    }
}
