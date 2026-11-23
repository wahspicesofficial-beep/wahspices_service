using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using WahSpices.Application.Common;

namespace WahSpices.API.Security;

/// <summary>Client IP after the forwarded-headers middleware (X-Forwarded-For is honoured ONLY from Security:TrustedProxies).</summary>
public static class ClientIp
{
    public static string Get(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip == null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.ToString();
    }

    public static IPAddress? Address(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        return ip != null && ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    /// <summary>Parses "1.2.3.4", "10.0.0.0/8", "2001:db8::/32". Returns null when invalid.</summary>
    public static System.Net.IPNetwork? ParseNetwork(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (!s.Contains('/'))
        {
            if (!IPAddress.TryParse(s, out var single)) return null;
            if (single.IsIPv4MappedToIPv6) single = single.MapToIPv4();
            return new System.Net.IPNetwork(single, single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
        }
        return System.Net.IPNetwork.TryParse(s, out var net) ? net : null;
    }
}

/// <summary>
/// ASP.NET Core built-in rate limiter (replaces the AspNetCoreRateLimit setup, whose rules used the wrong
/// endpoint syntax and keyed on a spoofable X-Real-IP header).
///   • general: every /api request — per admin (900/min), per customer (300/min), per anonymous IP (300/min),
///     plus an overall per-IP ceiling (1200/min) so rotating tokens doesn't escape it;
///   • strict rules for auth / OTP / contact / feedback / reviews / tracking / orders / coupons (per IP, or per
///     customer when signed in). Each can be tuned with Security:RateLimits:&lt;rule&gt;:Permit / WindowSeconds.
/// Rejections: HTTP 429 + Retry-After + the standard error JSON.
/// </summary>
public static class WahRateLimiting
{
    private sealed record Rule(string Name, string Method, Regex Path, int Permit, int WindowSeconds);

    private static Rule R(string name, string method, string pattern, int permit, int window) =>
        new(name, method, new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant), permit, window);

    private static readonly Rule[] Rules =
    {
        R("auth-login",      "POST", @"^/api/auth/(login|google)/?$",                                   10, 300),
        R("auth-register",   "POST", @"^/api/auth/register/?$",                                          5, 900),
        R("auth-otp-send",   "POST", @"^/api/auth/(send-otp|resend-email-otp|forgot-password)/?$",        5, 900),
        R("auth-otp-verify", "POST", @"^/api/auth/(verify-otp|verify-email|reset-password)/?$",          10, 900),
        R("auth-refresh",    "POST", @"^/api/(admin/)?auth/refresh/?$",                                  30, 300),
        R("admin-login",     "POST", @"^/api/admin/auth/(login|2fa/verify-login|2fa/setup-login|2fa/enable-login)/?$", 10, 300),
        R("contact",         "POST", @"^/api/contact/?$",                                                 3, 600),
        R("feedback",        "POST", @"^/api/feedback/?$",                                                3, 600),
        R("review",          "POST", @"^/api/products/\d+/reviews/?$",                                    5, 600),
        R("track",           "GET",  @"^/api/orders/track/",                                             20, 300),
        R("order",           "POST", @"^/api/orders/?$",                                                 10, 600),
        R("payment",         "POST", @"^/api/payments/create-order/?$",                                  20, 600),
        R("coupon",          "POST", @"^/api/cart/apply-coupon/?$",                                      20, 600),
        R("quote",           "POST", @"^/api/orders/quote/?$",                                           60, 300),
        R("return",          "POST", @"^/api/(returns|orders/\d+/return)/?$",                             10, 3600),
        R("challenge",       "GET",  @"^/api/security/challenge/?$",                                     60, 60),
        R("unsubscribe",     "POST", @"^/api/marketing/(unsubscribe|resubscribe)/?$",                    10, 600),
    };

    public static IServiceCollection AddWahRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        // ── Trusted proxies: X-Forwarded-For / -Proto are applied only when the TCP peer is listed here ──
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownNetworks.Clear();          // default trusts loopback — we trust nothing unless configured
            o.ForwardLimit = config.GetValue("Security:ForwardLimit", 1);
            // One trusted-proxy list for the whole app: Security:TrustedProxies (also accepts the older
            // Security:KnownProxies / Security:KnownNetworks keys used by the HTTP hardening options).
            var trusted = (config.GetSection("Security:TrustedProxies").Get<string[]>() ?? Array.Empty<string>())
                .Concat(config.GetSection("Security:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
                .Concat(config.GetSection("Security:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
                .Distinct();
            foreach (var entry in trusted)
            {
                var net = ClientIp.ParseNetwork(entry);
                if (net == null) continue;
                var n = net.Value;
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(n.BaseAddress, n.PrefixLength));
            }
        });

        var enabled = config.GetValue("Security:RateLimits:Enabled", true);
        int Permit(string name, int d) => Math.Max(1, config.GetValue($"Security:RateLimits:{name}:Permit", d));
        int Window(string name, int d) => Math.Max(1, config.GetValue($"Security:RateLimits:{name}:WindowSeconds", d));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            var general = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!enabled || !http.Request.Path.StartsWithSegments("/api")) return RateLimitPartition.GetNoLimiter("none");
                var adminId = http.User.FindFirst("admin_id")?.Value;
                var userId = http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
                if (adminId != null && http.User.Identity?.IsAuthenticated == true)
                    return Sliding("a:" + adminId, Permit("admin", 900), Window("admin", 60));
                if (userId != null)
                    return Sliding("u:" + userId, Permit("customer", 300), Window("customer", 60));
                return Sliding("ip:" + ClientIp.Get(http), Permit("anonymous", 300), Window("anonymous", 60));
            });

            var perIp = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                !enabled || !http.Request.Path.StartsWithSegments("/api")
                    ? RateLimitPartition.GetNoLimiter("none")
                    : Sliding("ipall:" + ClientIp.Get(http), Permit("ip", 1200), Window("ip", 60)));

            var strict = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!enabled) return RateLimitPartition.GetNoLimiter("none");
                var path = http.Request.Path.Value ?? "";
                foreach (var r in Rules)
                {
                    if (!HttpMethods.Equals(http.Request.Method, r.Method) || !r.Path.IsMatch(path)) continue;
                    // signed-in customers are limited per account (shared NAT / office IPs don't punish each other)
                    var who = http.User.Identity?.IsAuthenticated == true && http.User.FindFirst("admin_id") == null
                        ? "u:" + http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        : "ip:" + ClientIp.Get(http);
                    var permit = Permit(r.Name, r.Permit);
                    var window = Window(r.Name, r.WindowSeconds);
                    return RateLimitPartition.GetFixedWindowLimiter($"{r.Name}:{who}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permit, Window = TimeSpan.FromSeconds(window), QueueLimit = 0, AutoReplenishment = true
                    });
                }
                return RateLimitPartition.GetNoLimiter("none");
            });

            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(strict, general, perIp);

            o.OnRejected = async (ctx, ct) =>
            {
                var retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? (int)Math.Ceiling(ra.TotalSeconds) : 60;
                retry = Math.Max(retry, 1);
                var http = ctx.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = retry.ToString();
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimit")
                    .LogWarning("429 {Method} {Path} from {Ip}", http.Request.Method, http.Request.Path, ClientIp.Get(http));
                await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("RATE_LIMITED",
                    $"Too many requests. Please wait {retry} second(s) and try again."), ct);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Sliding(string key, int permit, int windowSeconds) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permit, Window = TimeSpan.FromSeconds(windowSeconds), SegmentsPerWindow = 6, QueueLimit = 0, AutoReplenishment = true
        });
}
