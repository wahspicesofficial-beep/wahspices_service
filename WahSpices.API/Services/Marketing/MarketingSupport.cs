using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Messaging;

namespace WahSpices.API.Services.Marketing;

/// <summary>Input rules shared by campaigns and broadcasts. Admin text is plain text only — never HTML.</summary>
public static class MarketingRules
{
    public const int TitleMax = 150, BodyMax = 4000, BroadcastMessageMax = 2000, ButtonLabelMax = 40, UrlMax = 512, MaxProducts = 4, MaxSelectedUsers = 20000;

    private static readonly Regex SitePath = new(@"^/(?![/\\])[A-Za-z0-9\-._~/?#\[\]@!$&'()*+,;=%]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UploadPath = new(@"^/uploads/[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*/[A-Za-z0-9_\-.]+\.(jpg|jpeg|png|webp|gif|avif)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CouponPattern = new(@"^[A-Z0-9_\-]{2,50}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ControlChars = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.Compiled);

    /// <summary>Trim, normalise newlines, drop control characters (keeps \n). Null when empty.</summary>
    public static string? CleanText(string? s)
    {
        if (s == null) return null;
        var t = ControlChars.Replace(s.Replace("\r\n", "\n").Replace('\r', '\n'), "").Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? CleanLine(string? s) => CleanText(s)?.Replace('\n', ' ');

    /// <summary>Site path ("/products?category=x") or absolute https URL. Returns null when acceptable, else an error.</summary>
    public static string? CheckLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (u.Length > UrlMax) return "Link is too long.";
        if (u.StartsWith('/')) return SitePath.IsMatch(u) ? null : "Use a site path like /products or a full https:// link.";
        if (Uri.TryCreate(u, UriKind.Absolute, out var abs) && abs.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(abs.Host)
            && string.IsNullOrEmpty(abs.UserInfo) && !u.Any(char.IsWhiteSpace))
            return null;
        return "Use a site path like /products or a full https:// link.";
    }

    /// <summary>Uploaded image path (/uploads/…) or https URL.</summary>
    public static string? CheckImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (u.Length > UrlMax) return "Image link is too long.";
        if (u.Contains("..")) return "Invalid image path.";
        if (UploadPath.IsMatch(u)) return null;
        if (Uri.TryCreate(u, UriKind.Absolute, out var abs) && abs.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(abs.UserInfo)) return null;
        return "Upload an image (JPG, PNG, WEBP or GIF) or use an https:// image link.";
    }

    /// <summary>"http://localhost:5056/uploads/x.png" (as returned by the upload API) → "/uploads/x.png".</summary>
    public static string? NormalizeImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (Uri.TryCreate(u, UriKind.Absolute, out var abs) && abs.AbsolutePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return abs.AbsolutePath;
        return u;
    }

    public static string? CleanCoupon(string? code, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpperInvariant();
        if (!CouponPattern.IsMatch(c)) { error = "Coupon codes use letters, digits, - or _ (2–50 characters)."; return null; }
        return c;
    }

    public static List<int> ParseIds(string? csvOrJson)
    {
        if (string.IsNullOrWhiteSpace(csvOrJson)) return new List<int>();
        var s = csvOrJson.Trim();
        if (s.StartsWith('['))
        {
            try { return (JsonSerializer.Deserialize<List<int>>(s) ?? new()).Distinct().ToList(); }
            catch (JsonException) { return new List<int>(); }
        }
        return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).Distinct().ToList();
    }

    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;
        var at = email.IndexOf('@');
        var local = email[..at];
        return (local.Length <= 2 ? local[..1] : local[..2]) + new string('*', Math.Max(1, Math.Min(local.Length - 2, 5))) + email[at..];
    }
}

/// <summary>
/// Signed, non-expiring unsubscribe tokens: "{userId}.{HMAC-SHA256(key, "unsub:v1:{userId}") base64url, 128 bits}".
/// Key: Marketing:UnsubscribeKey, else derived from Jwt:SecretKey (rotating the JWT key invalidates old links,
/// which then just show "link no longer valid — use your profile page").
/// </summary>
public sealed class UnsubscribeTokens
{
    private readonly byte[] _key;

    public UnsubscribeTokens(IConfiguration config)
    {
        var dedicated = config["Marketing:UnsubscribeKey"];
        var source = !string.IsNullOrWhiteSpace(dedicated) && !dedicated.StartsWith("SET-", StringComparison.OrdinalIgnoreCase)
            ? dedicated
            : config["Jwt:SecretKey"] ?? throw new InvalidOperationException("Jwt:SecretKey missing");
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(source), Encoding.UTF8.GetBytes("wah-marketing-unsubscribe-v1"));
    }

    public string Create(int userId) => $"{userId}.{Sign(userId)}";

    public int? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 80) return null;
        var dot = token.IndexOf('.');
        if (dot <= 0 || !int.TryParse(token[..dot], out var uid) || uid <= 0) return null;
        var expected = Encoding.ASCII.GetBytes(Sign(uid));
        var given = Encoding.ASCII.GetBytes(token[(dot + 1)..]);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected) ? uid : null;
    }

    private string Sign(int userId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"unsub:v1:{userId}"));
        return Convert.ToBase64String(mac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>Display labels for the CampaignType master-data list (cached 5 minutes).</summary>
public sealed class CampaignTypeLabels
{
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;
    public CampaignTypeLabels(IMemoryCache cache, IServiceScopeFactory scopes) { _cache = cache; _scopes = scopes; }

    public async Task<Dictionary<string, string>> GetAsync()
    {
        if (_cache.TryGetValue("campaign-type-labels", out Dictionary<string, string>? hit) && hit != null) return hit;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var map = await db.MasterDataValues.AsNoTracking().Where(m => m.Type == "CampaignType")
            .ToDictionaryAsync(m => m.Value, m => m.Label, StringComparer.OrdinalIgnoreCase);
        _cache.Set("campaign-type-labels", map, TimeSpan.FromMinutes(5));
        return map;
    }

    public async Task<string> LabelAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Update";
        var map = await GetAsync();
        return map.TryGetValue(value, out var l) ? l : value;
    }

    public void Invalidate() => _cache.Remove("campaign-type-labels");
}

/// <summary>A customer as the audience logic sees them.</summary>
public sealed record AudienceUser(int Id, string Name, string Email, string Phone, bool IsActive, bool IsEmailVerified, bool OptedOut);

/// <summary>
/// Who gets what. Email: active, verified, not deleted, not opted out, deliverable address (no phone-signup
/// placeholder). WhatsApp: active, not deleted, not opted out, valid Indian mobile. Ineligible people in an
/// explicit selection become "Skipped" rows with the reason, so the admin can see why.
/// </summary>
public static class Audience
{
    public sealed record Planned(int UserId, string Channel, string Address, string Name, string? SkipReason);

    public static IQueryable<User> Customers(AppDbContext db) =>
        db.Users.AsNoTracking().Where(u => !u.IsDeleted && u.Role == UserRole.Customer);

    public static async Task<List<AudienceUser>> LoadAsync(AppDbContext db, string audienceType, IReadOnlyCollection<int>? selectedIds, CancellationToken ct = default)
    {
        var q = Customers(db);
        if (audienceType == AudienceTypes.Selected)
        {
            var ids = (selectedIds ?? Array.Empty<int>()).ToList();
            if (ids.Count == 0) return new List<AudienceUser>();
            var result = new List<AudienceUser>(ids.Count);
            foreach (var chunk in ids.Chunk(1000))   // keep SQL parameter lists small
                result.AddRange(await q.Where(u => chunk.Contains(u.Id))
                    .Select(u => new AudienceUser(u.Id, u.Name, u.Email, u.Phone, u.IsActive, u.IsEmailVerified, u.MarketingOptOut)).ToListAsync(ct));
            return result;
        }
        // "All": active, verified customers (opted-out ones are listed as Skipped)
        return await q.Where(u => u.IsActive && u.IsEmailVerified)
            .Select(u => new AudienceUser(u.Id, u.Name, u.Email, u.Phone, u.IsActive, u.IsEmailVerified, u.MarketingOptOut)).ToListAsync(ct);
    }

    public static List<Planned> Plan(IEnumerable<AudienceUser> users, bool email, bool whatsApp)
    {
        var list = new List<Planned>();
        foreach (var u in users)
        {
            if (email)
            {
                string? skip = u.OptedOut ? "Opted out of marketing"
                    : !u.IsActive ? "Account disabled"
                    : !u.IsEmailVerified ? "Email not verified"
                    : !EmailAddressRules.IsDeliverable(u.Email) ? "No deliverable email address"
                    : null;
                list.Add(new Planned(u.Id, MessagingChannels.Email, Clip(u.Email ?? "", 256), Clip(u.Name ?? "", 128), skip));
            }
            if (whatsApp)
            {
                var phone = IWhatsAppSender.NormalizeIndianMobile(u.Phone);
                string? skip = u.OptedOut ? "Opted out of marketing"
                    : !u.IsActive ? "Account disabled"
                    : phone == null ? "No valid mobile number"
                    : null;
                list.Add(new Planned(u.Id, MessagingChannels.WhatsApp, phone ?? Clip(u.Phone ?? "", 256), Clip(u.Name ?? "", 128), skip));
            }
        }
        return list;
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];
}
