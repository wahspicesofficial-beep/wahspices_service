namespace WahSpices.API.Extensions;

/// <summary>
/// HTTP-layer security settings — bound from the "Security" config section.
/// Every value has a safe default, so the section is optional.
/// Env-var form: Security__Cors__AllowedOrigins__0=https://wahspices.in
/// </summary>
public class HttpSecurityOptions
{
    public const string Section = "Security";

    public CorsSettings Cors { get; set; } = new();
    public LimitSettings Limits { get; set; } = new();
    public AbuseGuardSettings AbuseGuard { get; set; } = new();

    /// <summary>Redirect http → https outside Development (needs TLS on the server or a proxy sending X-Forwarded-Proto).</summary>
    public bool HttpsRedirection { get; set; } = true;
    /// <summary>Public HTTPS port used for the redirect.</summary>
    public int HttpsPort { get; set; } = 443;
    /// <summary>HSTS max-age (days). 730 days + includeSubDomains + preload = hstspreload.org requirements.</summary>
    public int HstsMaxAgeDays { get; set; } = 730;
    public bool HstsPreload { get; set; } = true;

    /// <summary>Reverse proxies (nginx / load balancer) whose X-Forwarded-For / X-Real-IP headers are trusted.
    /// Loopback is always trusted. Requests from anyone else have those headers stripped so the client IP
    /// (used by rate limiting + abuse guard) cannot be spoofed.</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();
    /// <summary>CIDR networks of trusted proxies, e.g. "10.0.0.0/8".</summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();

    /// <summary>Expose Swagger UI + /swagger JSON. Default: Development only.</summary>
    public bool? SwaggerEnabled { get; set; }
}

public class CorsSettings
{
    /// <summary>Exact origins allowed (scheme://host[:port]). Falls back to the legacy top-level "AllowedOrigins" array.</summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public string[] AllowedMethods { get; set; } = { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };
    /// <summary>Request headers browsers may send. Add any new custom header (e.g. proof-of-work / captcha) here.</summary>
    public string[] AllowedHeaders { get; set; } =
    {
        "Authorization", "Content-Type", "Accept", "Accept-Language", "X-Requested-With",
        "Idempotency-Key", "X-Idempotency-Key", "X-Client-Version",
        "X-PoW-Proof",   // proof-of-work header sent by src/services/humanProof.js (login, register, OTP, contact, orders …)
        "X-PoW-Challenge", "X-PoW-Nonce", "X-PoW-Solution", "X-PoW-Token", "X-Captcha-Token", "X-Device-Id",
        "X-SignalR-User-Agent",   // @microsoft/signalr negotiate request (real-time notifications hub /hubs/notifications)
    };
    /// <summary>Response headers JS may read (file names of exports, rate-limit hints).</summary>
    public string[] ExposedHeaders { get; set; } =
    {
        "Content-Disposition", "Retry-After", "X-Rate-Limit-Limit", "X-Rate-Limit-Remaining", "X-Rate-Limit-Reset",
        "X-Correlation-Id",
    };
    /// <summary>Only needed if the API ever uses cookies. Never combined with a wildcard origin.</summary>
    public bool AllowCredentials { get; set; } = true;
    public int PreflightMaxAgeSeconds { get; set; } = 7200;
}

public class LimitSettings
{
    /// <summary>Default max request body for every endpoint (bytes). Endpoints with [RequestSizeLimit] override it.</summary>
    public long MaxRequestBodyBytes { get; set; } = 2 * 1024 * 1024;
    /// <summary>Max body for multipart/form-data uploads on endpoints without their own [RequestSizeLimit] (e.g. return photos).</summary>
    public long MaxMultipartBodyBytes { get; set; } = 25 * 1024 * 1024;
    public int MaxRequestHeaderCount { get; set; } = 50;
    public int MaxRequestHeadersTotalBytes { get; set; } = 32 * 1024;
    public int MaxRequestLineBytes { get; set; } = 8 * 1024;
    public int RequestHeadersTimeoutSeconds { get; set; } = 15;
    public int KeepAliveTimeoutSeconds { get; set; } = 60;
    public int JsonMaxDepth { get; set; } = 32;
}

public class AbuseGuardSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Status codes that count as "suspicious" for an IP.</summary>
    public int[] CountedStatusCodes { get; set; } = { 400, 401, 403, 404, 405, 429 };
    /// <summary>How many counted responses within WindowSeconds trigger a block.</summary>
    public int Threshold { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
    public int BlockMinutes { get; set; } = 15;
    /// <summary>Only paths under this prefix are counted (static images 404s never block a shopper).</summary>
    public string PathPrefix { get; set; } = "/api";
    /// <summary>IPs never counted or blocked (office IP, uptime monitor).</summary>
    public string[] ExemptIps { get; set; } = Array.Empty<string>();
    /// <summary>Don't count/block 127.0.0.1 / ::1 in Development (local test scripts).</summary>
    public bool ExemptLoopbackInDevelopment { get; set; } = true;
}
