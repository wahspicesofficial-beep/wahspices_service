using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WahSpices.API.Authorization;
using WahSpices.API.Controllers.Admin;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Security;

/// <summary>Admin security policy, stored in SiteContentBlock "securitySettings" (IsPublic = false, never served publicly).</summary>
public sealed class AdminSecuritySettings
{
    /// <summary>Every admin must use 2FA; admins without it are walked through setup on their next login.</summary>
    public bool Enforce2FA { get; set; }
    /// <summary>Sign an admin session out after this many idle minutes (0 = off).</summary>
    public int IdleTimeoutMinutes { get; set; } = 120;
    /// <summary>Maximum simultaneous sessions per admin; the oldest is signed out (0 = unlimited).</summary>
    public int MaxConcurrentSessions { get; set; } = 10;
    /// <summary>When on, admin login and every admin request must come from one of IpAllowlist (IPs or CIDR ranges).</summary>
    public bool IpAllowlistEnabled { get; set; }
    public List<string> IpAllowlist { get; set; } = new();
}

public sealed class AdminSecuritySettingsService(IMemoryCache cache)
{
    public const string BlockKey = "securitySettings";
    private const string CacheKey = "admin-security-settings";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Reserved site-content keys that the Website-content editor must never read or overwrite.</summary>
    public static bool IsReservedKey(string? key) => string.Equals(key?.Trim(), BlockKey, StringComparison.OrdinalIgnoreCase);

    public async Task<AdminSecuritySettings> GetAsync(AppDbContext db)
    {
        if (cache.TryGetValue(CacheKey, out AdminSecuritySettings? s) && s != null) return s;
        var row = await db.SiteContentBlocks.AsNoTracking().FirstOrDefaultAsync(b => b.Key == BlockKey && !b.IsDeleted);
        s = Parse(row?.Json);
        cache.Set(CacheKey, s, TimeSpan.FromSeconds(30));
        return s;
    }

    public async Task<AdminSecuritySettings> SaveAsync(AppDbContext db, AdminSecuritySettings s, string by)
    {
        var row = await db.SiteContentBlocks.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Key == BlockKey);
        if (row == null) { row = new SiteContentBlock { Key = BlockKey, CreatedBy = by }; db.SiteContentBlocks.Add(row); }
        row.Json = JsonSerializer.Serialize(s, Json);
        row.IsPublic = false;
        row.IsDeleted = false;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = by;
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
        return s;
    }

    public static AdminSecuritySettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AdminSecuritySettings();
        try { return JsonSerializer.Deserialize<AdminSecuritySettings>(json, Json) ?? new AdminSecuritySettings(); }
        catch (JsonException) { return new AdminSecuritySettings(); }
    }

    public static bool IpAllowed(AdminSecuritySettings s, HttpContext http)
    {
        if (!s.IpAllowlistEnabled || s.IpAllowlist.Count == 0) return true;
        var ip = ClientIp.Address(http);
        if (ip == null) return false;
        foreach (var entry in s.IpAllowlist)
        {
            var net = ClientIp.ParseNetwork(entry);
            if (net != null && net.Value.Contains(ip)) return true;
        }
        return false;
    }
}

public record SecuritySettingsRequest(bool Enforce2FA, int IdleTimeoutMinutes, int MaxConcurrentSessions, bool IpAllowlistEnabled, List<string>? IpAllowlist);

[Route("api/admin/security-settings")]
[RequireModule("Settings")]
public class AdminSecuritySettingsController(AppDbContext db, AdminSecuritySettingsService svc) : AdminBaseController
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var s = await svc.GetAsync(db);
        var adminsWithout2FA = await db.AdminUsers.CountAsync(a => !a.IsDeleted && a.IsActive && !a.IsTwoFactorEnabled);
        return Ok(ApiResponse<object>.Ok(new
        {
            s.Enforce2FA, s.IdleTimeoutMinutes, s.MaxConcurrentSessions, s.IpAllowlistEnabled, s.IpAllowlist,
            yourIp = ClientIp.Get(HttpContext),
            adminsWithout2FA,
        }));
    }

    [HttpPut]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> Save([FromBody] SecuritySettingsRequest req)
    {
        if (req.IdleTimeoutMinutes is < 0 or > 1440)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Idle timeout must be between 0 (off) and 1440 minutes."));
        if (req.MaxConcurrentSessions is < 0 or > 100)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Max sessions must be between 0 (unlimited) and 100."));

        var list = (req.IpAllowlist ?? new()).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        if (list.Count > 100) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "At most 100 allow-list entries."));
        var bad = list.FirstOrDefault(x => ClientIp.ParseNetwork(x) == null);
        if (bad != null)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"'{bad}' is not a valid IP address or CIDR range (e.g. 203.0.113.7 or 203.0.113.0/24)."));

        var s = new AdminSecuritySettings
        {
            Enforce2FA = req.Enforce2FA,
            IdleTimeoutMinutes = req.IdleTimeoutMinutes,
            MaxConcurrentSessions = req.MaxConcurrentSessions,
            IpAllowlistEnabled = req.IpAllowlistEnabled && list.Count > 0,
            IpAllowlist = list,
        };
        if (req.IpAllowlistEnabled && list.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Add at least one IP / range before turning the allow-list on."));
        // Never let an admin lock themselves out: their current IP must be inside the new list.
        if (s.IpAllowlistEnabled && !AdminSecuritySettingsService.IpAllowed(s, HttpContext))
            return BadRequest(ApiResponse<object>.Fail("LOCKOUT_PREVENTED",
                $"Your current IP ({ClientIp.Get(HttpContext)}) isn't in the list — add it first, or you'd be locked out."));

        await svc.SaveAsync(db, s, AdminName);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = AdminId, UserName = AdminName, Module = "Security", Action = "Security settings updated",
            Details = $"2FA enforced={s.Enforce2FA}, idle={s.IdleTimeoutMinutes}m, maxSessions={s.MaxConcurrentSessions}, allowlist={(s.IpAllowlistEnabled ? string.Join(", ", s.IpAllowlist) : "off")}",
            IpAddress = ClientIp.Get(HttpContext), CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            s.Enforce2FA, s.IdleTimeoutMinutes, s.MaxConcurrentSessions, s.IpAllowlistEnabled, s.IpAllowlist,
            yourIp = ClientIp.Get(HttpContext),
        }, "Security settings saved"));
    }
}

/// <summary>Revokes an admin's sessions (and their refresh tokens), optionally keeping one. Caller saves.</summary>
public static class AdminSessionRevoker
{
    public static async Task RevokeAllAsync(AppDbContext db, AdminUser admin, int? exceptSessionId)
    {
        var sessions = await db.AdminSessions
            .Where(s => s.AdminUserId == admin.Id && s.RevokedAt == null && (exceptSessionId == null || s.Id != exceptSessionId))
            .ToListAsync();
        foreach (var s in sessions) s.RevokedAt = DateTime.UtcNow;
        var refresh = WahSpices.Infrastructure.Security.RefreshSessions.Parse(admin.RefreshToken);
        refresh.RemoveAll(r => exceptSessionId == null || r.Key != exceptSessionId.ToString());
        admin.RefreshToken = WahSpices.Infrastructure.Security.RefreshSessions.Serialize(refresh);
    }
}
