using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Security;

public static class WahSecurityRegistration
{
    /// <summary>Everything security-related that isn't JWT itself: rate limiting, proxies, human proof, admin policy.</summary>
    public static IServiceCollection AddWahSecurity(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddWahRateLimiting(config);
        services.AddSingleton<HumanProofService>();
        services.AddSingleton<AdminSecuritySettingsService>();
        services.AddHttpClient("turnstile", c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<TurnstileVerifier>();
        return services;
    }

    /// <summary>
    /// Refuse to start in Production with a weak signing key. The key comes from configuration / secrets
    /// (env var Jwt__SecretKey, user-secrets, key vault …) — never from a committed appsettings file.
    /// </summary>
    public static byte[] RequireSigningKey(IConfiguration config)
    {
        var key = config["Jwt:SecretKey"] ?? "";
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase) && key.Length < 64)
            throw new InvalidOperationException(
                "Jwt:SecretKey must be at least 64 characters in Production. Set it via the Jwt__SecretKey environment variable or a secret store.");
        if (key.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters.");
        return System.Text.Encoding.UTF8.GetBytes(key);
    }
}

/// <summary>Cookie that carries the refresh token: httpOnly, SameSite=Strict, Secure (HTTPS), path-scoped to the refresh endpoint.</summary>
public static class RefreshCookies
{
    public const string Customer = "wah_rt";
    public const string Admin = "wah_art";

    private static string PathFor(IConfiguration c, string name)
    {
        var prefix = (c["Security:RefreshCookie:PathPrefix"] ?? "/api").TrimEnd('/');
        return name == Admin ? $"{prefix}/admin/auth/refresh" : $"{prefix}/auth/refresh";
    }

    private static CookieOptions Options(HttpContext http, string name, DateTimeOffset? expires)
    {
        var c = http.RequestServices.GetRequiredService<IConfiguration>();
        var env = http.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var secure = c.GetValue<bool?>("Security:RefreshCookie:Secure") ?? (http.Request.IsHttps || !env.IsDevelopment());
        var sameSite = Enum.TryParse<SameSiteMode>(c["Security:RefreshCookie:SameSite"], true, out var m) ? m : SameSiteMode.Strict;
        if (sameSite == SameSiteMode.None) secure = true;   // browsers require Secure with SameSite=None
        return new CookieOptions
        {
            HttpOnly = true, Secure = secure, SameSite = sameSite, Path = PathFor(c, name),
            Expires = expires, IsEssential = true,
        };
    }

    public static bool ReturnInBody(HttpContext http) =>
        http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Security:RefreshCookie:ReturnInBody", false);

    public static void Set(HttpContext http, string name, string token, DateTimeOffset expires) =>
        http.Response.Cookies.Append(name, token, Options(http, name, expires));

    public static void Clear(HttpContext http, string name) =>
        http.Response.Cookies.Delete(name, Options(http, name, null));

    public static string? Read(HttpContext http, string name, string? bodyToken) =>
        !string.IsNullOrWhiteSpace(bodyToken) ? bodyToken : http.Request.Cookies[name];

    /// <summary>Cookie-borne refresh calls must come from one of our own origins (CSRF defence in depth).</summary>
    public static bool OriginAllowed(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin)) return true;   // non-browser client (no ambient cookies to abuse)
        var allowed = http.RequestServices.GetRequiredService<IConfiguration>().GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        return allowed.Any(a => string.Equals(a.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Per-request token checks (wired into JwtBearerEvents.OnTokenValidated).</summary>
public static class WahTokenEvents
{
    public const string FailureReasonKey = "__wah_auth_failure";

    public static async Task OnTokenValidated(TokenValidatedContext ctx)
    {
        var p = ctx.Principal;
        if (p == null) { ctx.Fail("No principal"); return; }

        // Login-step tokens (2FA / forced 2FA setup) are only accepted by their own endpoints, never as bearer tokens.
        if (p.FindFirst("temp_2fa") != null || p.FindFirst("temp_2fa_setup") != null)
        {
            Fail(ctx, "Token missing or expired.");
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        if (p.FindFirst("admin_id") != null)
        {
            await ValidateAdmin(ctx, p, db);
            return;
        }

        // ── customer ──
        if (!int.TryParse(p.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) || !int.TryParse(p.FindFirst("tv")?.Value, out var tv))
        {
            Fail(ctx, "Your session has expired. Please sign in again.");   // legacy (pre-TokenVersion) tokens
            return;
        }
        var u = await db.Users.AsNoTracking().Where(x => x.Id == uid)
            .Select(x => new { x.TokenVersion, x.IsActive, x.IsDeleted, x.RefreshToken }).FirstOrDefaultAsync();
        if (u == null || u.IsDeleted || !u.IsActive) { Fail(ctx, "Your account is not active."); return; }
        if (u.TokenVersion != tv) { Fail(ctx, "Your session has expired. Please sign in again."); return; }
        var sid = p.FindFirst("wsid")?.Value;
        if (sid != null && RefreshSessions.Parse(u.RefreshToken).All(s => s.Key != sid))
            Fail(ctx, "You have been signed out. Please sign in again.");
    }

    private static async Task ValidateAdmin(TokenValidatedContext ctx, ClaimsPrincipal p, AppDbContext db)
    {
        var http = ctx.HttpContext;
        int.TryParse(p.FindFirst("admin_id")?.Value, out var adminId);
        var sidClaim = p.FindFirst("wsid")?.Value;
        var jti = p.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? p.FindFirst("jti")?.Value;

        WahSpices.Domain.Entities.AdminSession? session = null;
        if (int.TryParse(sidClaim, out var sid))
            session = await db.AdminSessions.FirstOrDefaultAsync(s => s.Id == sid);
        else if (!string.IsNullOrEmpty(jti))
            session = await db.AdminSessions.FirstOrDefaultAsync(s => s.JwtJti == jti);

        // Every admin token must belong to a live session (Access Control → Sessions → Revoke works instantly).
        if (session == null || session.AdminUserId != adminId) { Fail(ctx, "Session not found. Please sign in again."); return; }
        if (session.RevokedAt != null) { Fail(ctx, "Session revoked."); return; }
        if (session.ExpiresAt < DateTime.UtcNow) { Fail(ctx, "Session expired. Please sign in again."); return; }

        var settings = await http.RequestServices.GetRequiredService<AdminSecuritySettingsService>().GetAsync(db);
        if (settings.IdleTimeoutMinutes > 0 && session.LastActiveAt < DateTime.UtcNow.AddMinutes(-settings.IdleTimeoutMinutes))
        {
            session.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Fail(ctx, $"Signed out after {settings.IdleTimeoutMinutes} minutes of inactivity.");
            return;
        }
        if (!AdminSecuritySettingsService.IpAllowed(settings, http))
        {
            Fail(ctx, "Admin access isn't allowed from this network.");
            return;
        }
        if (session.LastActiveAt < DateTime.UtcNow.AddMinutes(-1))
        {
            session.LastActiveAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private static void Fail(TokenValidatedContext ctx, string reason)
    {
        ctx.HttpContext.Items[FailureReasonKey] = reason;
        ctx.Fail(reason);
    }
}
