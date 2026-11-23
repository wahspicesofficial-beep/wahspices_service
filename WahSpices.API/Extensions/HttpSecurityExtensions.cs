using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WahSpices.API.Middleware;

namespace WahSpices.API.Extensions;

/// <summary>
/// HTTP-layer hardening: Kestrel/IIS limits, CORS, forwarded headers, HSTS, security headers,
/// method filter, body-size limits, abuse guard, uniform validation errors, safe /uploads + /health.
///   builder.AddHttpSecurity();          // services
///   app.UseHttpSecurity();              // first in the pipeline
///   app.UseStaticFiles(app.UploadsStaticFileOptions());
///   app.MapHealthChecks("/health", app.HealthCheckOptions());
/// </summary>
public static class HttpSecurityExtensions
{
    public const string CorsPolicyName = "AllowFrontend";

    public static WebApplicationBuilder AddHttpSecurity(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;
        services.Configure<HttpSecurityOptions>(config.GetSection(HttpSecurityOptions.Section));
        var opt = config.GetSection(HttpSecurityOptions.Section).Get<HttpSecurityOptions>() ?? new HttpSecurityOptions();
        var lim = opt.Limits;

        // ── Kestrel limits (also applied to IIS in-process hosting) ──
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = lim.MaxRequestBodyBytes;
            k.Limits.MaxRequestHeaderCount = lim.MaxRequestHeaderCount;
            k.Limits.MaxRequestHeadersTotalSize = lim.MaxRequestHeadersTotalBytes;
            k.Limits.MaxRequestLineSize = lim.MaxRequestLineBytes;
            k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(lim.RequestHeadersTimeoutSeconds);
            k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(lim.KeepAliveTimeoutSeconds);
        });
        services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(o => o.MaxRequestBodySize = lim.MaxRequestBodyBytes);

        // ── JSON: shallow nesting only (deeply nested payloads = CPU/stack DoS) ──
        services.PostConfigure<Microsoft.AspNetCore.Mvc.JsonOptions>(o => o.JsonSerializerOptions.MaxDepth = lim.JsonMaxDepth);

        // ── Model-validation errors in the API's standard shape (no type names / stack internals) ──
        services.PostConfigure<ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = BuildValidationResponse);   // Post: AddControllers sets its own factory

        // ── CORS: exact origins from config, explicit methods/headers, cached preflight ──
        var origins = (opt.Cors.AllowedOrigins.Length > 0
                ? opt.Cors.AllowedOrigins
                : config.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
            .Select(o => o.Trim().TrimEnd('/'))
            .Where(o => o.Length > 0 && o != "*" && Uri.TryCreate(o, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (origins.Length == 0 && builder.Environment.IsDevelopment())
            origins = new[] { "http://localhost:3000", "http://localhost:3003" };
        services.AddCors(c => c.AddPolicy(CorsPolicyName, p =>
        {
            p.WithOrigins(origins)
             .WithMethods(opt.Cors.AllowedMethods)
             .WithHeaders(opt.Cors.AllowedHeaders)
             .WithExposedHeaders(opt.Cors.ExposedHeaders)
             .SetPreflightMaxAge(TimeSpan.FromSeconds(opt.Cors.PreflightMaxAgeSeconds));
            if (opt.Cors.AllowCredentials) p.AllowCredentials(); else p.DisallowCredentials();
        }));

        // ── Reverse proxy: trust X-Forwarded-For/Proto only from known proxies (loopback by default) ──
        services.Configure<ForwardedHeadersOptions>(f =>
        {
            f.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            f.ForwardLimit = 1;
            foreach (var p in opt.KnownProxies)
                if (IPAddress.TryParse(p, out var ip)) f.KnownProxies.Add(ip);
            foreach (var n in opt.KnownNetworks)
            {
                var parts = n.Split('/');
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var net) && int.TryParse(parts[1], out var bits))
                    f.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(net, bits));
            }
        });

        // ── HSTS (Production): 2 years, subdomains, preload-ready ──
        services.AddHsts(h =>
        {
            h.MaxAge = TimeSpan.FromDays(opt.HstsMaxAgeDays);
            h.IncludeSubDomains = true;
            h.Preload = opt.HstsPreload;
        });
        services.AddHttpsRedirection(r =>
        {
            r.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
            r.HttpsPort = opt.HttpsPort;
        });

        // Rate-limit (AspNetCoreRateLimit) rejections in the API's JSON error shape instead of plain text.
        services.PostConfigure<AspNetCoreRateLimit.IpRateLimitOptions>(o =>
        {
            o.QuotaExceededResponse ??= new AspNetCoreRateLimit.QuotaExceededResponse
            {
                StatusCode = 429,
                ContentType = "application/json",
                Content = "{{\"success\":false,\"error\":{{\"code\":\"RATE_LIMITED\",\"message\":\"Too many requests. Please wait {2} second(s) and try again.\"}}}}",
            };
        });

        services.AddSingleton<AbuseGuardStore>();
        return builder;
    }

    /// <summary>Call FIRST in the pipeline (before ExceptionMiddleware) so headers/limits/abuse counting cover every response.</summary>
    public static WebApplication UseHttpSecurity(this WebApplication app)
    {
        var opt = app.Services.GetRequiredService<IOptions<HttpSecurityOptions>>().Value;

        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            if (opt.HttpsRedirection)
                // /health stays reachable over plain http for load-balancer / uptime probes
                app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/health"), b => b.UseHttpsRedirection());
        }
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RequestHardeningMiddleware>();
        app.UseMiddleware<AbuseGuardMiddleware>();
        return app;
    }

    /// <summary>Swagger UI/JSON exposure: Development only unless Security:SwaggerEnabled is set explicitly.</summary>
    public static bool SwaggerEnabled(this WebApplication app)
    {
        var opt = app.Services.GetRequiredService<IOptions<HttpSecurityOptions>>().Value;
        return opt.SwaggerEnabled ?? app.Environment.IsDevelopment();
    }

    /// <summary>/uploads/**: correct types only, long browser cache for media, nothing executable.</summary>
    public static StaticFileOptions UploadsStaticFileOptions(this WebApplication app)
    {
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".avif"] = "image/avif";
        types.Mappings[".webp"] = "image/webp";
        return new StaticFileOptions
        {
            ContentTypeProvider = types,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = ctx =>
            {
                var h = ctx.Context.Response.Headers;
                var ct = ctx.Context.Response.ContentType ?? "";
                if (ct.StartsWith("image/") || ct.StartsWith("video/"))
                    h.CacheControl = "public, max-age=2592000";   // 30 days (uploaded names are GUIDs)
                else
                {
                    h.CacheControl = "no-cache";
                    h.ContentDisposition = "attachment";          // anything else downloads, never renders inline
                }
            },
        };
    }

    /// <summary>/health: status only in Production; per-check status (no exception text / server names) in Development.</summary>
    public static HealthCheckOptions HealthCheckOptions(this WebApplication app)
    {
        var dev = app.Environment.IsDevelopment();
        return new HealthCheckOptions
        {
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                ctx.Response.Headers.CacheControl = "no-store";
                object body = dev
                    ? new { status = report.Status.ToString(), checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()) }
                    : new { status = report.Status.ToString() };
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
            },
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = 200,
                [HealthStatus.Degraded] = 200,
                [HealthStatus.Unhealthy] = 503,
            },
        };
    }

    private static IActionResult BuildValidationResponse(ActionContext ctx)
    {
        var entries = ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0).ToList();
        // When the JSON body itself is bad, MVC also adds "The req field is required." for the action parameter —
        // drop that noise so the message names the real problem.
        var hasJsonErrors = entries.Any(kv => kv.Key.StartsWith('$'));
        var errors = entries
            .Where(kv => !(hasJsonErrors && !kv.Key.StartsWith('$') && !kv.Key.Contains('.')
                           && kv.Value!.Errors.All(e => e.ErrorMessage.EndsWith("field is required."))))
            .OrderBy(kv => kv.Key.StartsWith('$') ? 0 : 1)
            .ToDictionary(
                kv => CleanField(kv.Key),
                kv => kv.Value!.Errors.Select(e => CleanMessage(e, CleanField(kv.Key))).Distinct().ToArray());
        var first = errors.FirstOrDefault();
        var message = first.Value?.FirstOrDefault() ?? "Invalid request.";
        var body = new
        {
            success = false,
            error = new { code = "VALIDATION_ERROR", message, field = string.IsNullOrEmpty(first.Key) ? null : first.Key, details = errors },
        };
        return new BadRequestObjectResult(body) { ContentTypes = { "application/json" } };
    }

    // "$.items[0].qty" → "items[0].qty"; "request" / "dto" root keys → ""
    private static string CleanField(string key)
    {
        var k = key.StartsWith("$.") ? key[2..] : key == "$" ? "" : key;
        // camelCase each segment so it matches the JSON the client sent ("Items[0].Qty" → "items[0].qty")
        return string.Join('.', k.Split('.').Select(seg => seg.Length > 0 ? char.ToLowerInvariant(seg[0]) + seg[1..] : seg));
    }

    private static string CleanMessage(Microsoft.AspNetCore.Mvc.ModelBinding.ModelError e, string field)
    {
        // JSON parse/convert errors carry type names, line numbers and paths — replace with a plain message.
        var msg = e.ErrorMessage;
        if (e.Exception != null || string.IsNullOrEmpty(msg) || msg.Contains("Path:") || msg.Contains("LineNumber")
            || msg.Contains("System.") || msg.Contains("could not be converted"))
            return string.IsNullOrEmpty(field) ? "The request body is not valid JSON." : $"Invalid value for '{field}'.";
        return msg;
    }
}
