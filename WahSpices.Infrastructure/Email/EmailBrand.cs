using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Email;

/// <summary>Business details + link bases used by every email (from SiteSettings, the "social" content block and App:* config).</summary>
public sealed record EmailBrand
{
    public string Name { get; init; } = "WAH! Spices";      // display name (header wordmark, sign-offs)
    public string Tagline { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public string Address { get; init; } = "";
    public string? Gstin { get; init; }
    public string? Fssai { get; init; }
    public string? Udyam { get; init; }
    public decimal FreeDeliveryAbove { get; init; }
    public string AvgDeliveryDays { get; init; } = "";
    public List<(string Platform, string Url, string? Handle)> Social { get; init; } = new();
    public string FrontendUrl { get; init; } = "https://wahspices.com";   // no trailing slash
    public string AssetBaseUrl { get; init; } = "https://wahspices.com";  // where /uploads/** is served (the API origin)

    public string LogoUrl => $"{FrontendUrl}/wah_logo.jpg";
    public string Link(string path) => FrontendUrl + (path.StartsWith('/') ? path : "/" + path);

    /// <summary>Absolute URL for a stored image path (/uploads/… → API origin, /images/… → frontend, http(s) kept).</summary>
    public string? Asset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return path;
        var p = path.StartsWith('/') ? path : "/" + path;
        return (p.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ? AssetBaseUrl : FrontendUrl) + p;
    }

    public string PhoneHref => "tel:" + new string(Phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
}

public interface IEmailBrandProvider
{
    Task<EmailBrand> GetAsync(CancellationToken ct = default);
}

/// <summary>Reads business details from the DB and caches them for 5 minutes (singleton; uses its own scope).</summary>
public sealed class EmailBrandProvider : IEmailBrandProvider
{
    private const string CacheKey = "email-brand";
    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailBrandProvider> _log;

    public EmailBrandProvider(IServiceScopeFactory scopes, IMemoryCache cache, IConfiguration config, ILogger<EmailBrandProvider> log)
    {
        _scopes = scopes; _cache = cache; _config = config; _log = log;
    }

    public async Task<EmailBrand> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out EmailBrand? cached) && cached != null) return cached;
        var brand = await LoadAsync(ct);
        _cache.Set(CacheKey, brand, TimeSpan.FromMinutes(5));
        return brand;
    }

    private async Task<EmailBrand> LoadAsync(CancellationToken ct)
    {
        var frontend = (_config["App:FrontendUrl"] ?? "https://wahspices.com").TrimEnd('/');
        var assets = (_config["App:AssetBaseUrl"] ?? frontend).TrimEnd('/');
        var fromName = _config["Email:FromName"] ?? "WAH! Spices";
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.SiteSettings.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            var socialJson = await db.SiteContentBlocks.AsNoTracking().Where(b => b.Key == "social" && !b.IsDeleted)
                .Select(b => b.Json).FirstOrDefaultAsync(ct);

            var siteName = string.IsNullOrWhiteSpace(s?.SiteName) ? fromName : s!.SiteName.Trim();
            return new EmailBrand
            {
                // "Wah Spices" in settings and "WAH! Spices" as sender name are the same brand → prefer the styled spelling
                Name = Normalize(siteName) == Normalize(fromName) ? fromName : siteName,
                Tagline = s?.Tagline ?? "",
                Phone = s?.Phone ?? "",
                Email = s?.Email ?? "",
                Address = s?.Address ?? "",
                Gstin = s?.Gstin,
                Fssai = s?.Fssai,
                Udyam = s?.UdyamNumber,
                FreeDeliveryAbove = s?.FreeDeliveryAbove ?? 0,
                AvgDeliveryDays = s?.AvgDeliveryDays ?? "",
                Social = ParseSocial(socialJson),
                FrontendUrl = frontend,
                AssetBaseUrl = assets,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not load business details for emails — using defaults");
            return new EmailBrand { Name = fromName, FrontendUrl = frontend, AssetBaseUrl = assets };
        }
    }

    private static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static List<(string, string, string?)> ParseSocial(string? json)
    {
        var list = new List<(string, string, string?)>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
            foreach (var i in items.EnumerateArray())
            {
                bool enabled = i.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
                var url = i.TryGetProperty("url", out var u) ? u.GetString() : null;
                var platform = i.TryGetProperty("platform", out var p) ? p.GetString() : null;
                var handle = i.TryGetProperty("handle", out var h) ? h.GetString() : null;
                if (!enabled || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(platform)) continue;
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add((platform!, url!, handle));
            }
        }
        catch (JsonException) { }
        return list;
    }
}
