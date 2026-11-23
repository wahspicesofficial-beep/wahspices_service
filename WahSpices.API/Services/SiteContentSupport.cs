using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Services;

/// <summary>
/// Startup seeder for website content: inserts every default <see cref="SiteContentBlock"/> whose key
/// is missing (never overwrites admin edits) and creates the SiteSettings row with the contact details
/// the website shipped with when none exists yet. Registered as a hosted service in Program.cs.
/// </summary>
public class SiteContentSeeder : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<SiteContentSeeder> _logger;
    public SiteContentSeeder(IServiceProvider sp, ILogger<SiteContentSeeder> logger) { _sp = sp; _logger = logger; }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existing = await db.SiteContentBlocks.IgnoreQueryFilters().Select(b => b.Key).ToListAsync(ct);
            var have = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var (key, json) in SiteContentDefaults.Blocks)
            {
                if (have.Contains(key)) continue;
                db.SiteContentBlocks.Add(new SiteContentBlock
                {
                    Key = key, Json = json.Trim(), IsPublic = true, CreatedBy = "seed",
                });
                added++;
            }

            var s = await db.SiteSettings.FirstOrDefaultAsync(ct);
            if (s == null)
            {
                db.SiteSettings.Add(new SiteSettings
                {
                    Phone = SiteContentLogic.DefaultPhone, Email = SiteContentLogic.DefaultEmail,
                    Address = SiteContentLogic.DefaultAddress, CreatedBy = "seed",
                });
                added++;
            }
            else if (string.IsNullOrWhiteSpace(s.Phone) && string.IsNullOrWhiteSpace(s.Email) && string.IsNullOrWhiteSpace(s.Address))
            {
                // Row exists but contact details were never configured — fill in what the website showed.
                s.Phone = SiteContentLogic.DefaultPhone; s.Email = SiteContentLogic.DefaultEmail; s.Address = SiteContentLogic.DefaultAddress;
                added++;
            }

            if (added > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("SiteContentSeeder: seeded {Count} missing website content defaults", added);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SiteContentSeeder failed");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Shared helpers for the website content / contact / feedback controllers.</summary>
public static class SiteContentLogic
{
    public const string DefaultPhone = "+91 97231 85650";
    public const string DefaultEmail = "yash@wahspices.in";
    public const string DefaultAddress = "Shop No. 17-18, Krishna Township, Mota Varachha, Surat, Gujarat - 394101";

    public static readonly Regex KeyRegex = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    public static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    /// <summary>Public subset of SiteSettings — the single source of truth for contact details.</summary>
    public static object PublicSettings(SiteSettings s) => new
    {
        siteName = s.SiteName,
        tagline = s.Tagline,
        phone = s.Phone,
        email = s.Email,
        address = s.Address,
        gstin = s.Gstin,
        fssai = s.Fssai,
        udyamNumber = s.UdyamNumber,
        freeDeliveryAbove = s.FreeDeliveryAbove,
        deliveryCharge = s.DeliveryCharge,
        codAvailable = s.CodAvailable,
        avgDeliveryDays = s.AvgDeliveryDays,
        maintenanceMode = s.MaintenanceMode,
    };

    public static async Task<SiteSettings> GetSettingsAsync(AppDbContext db)
        => await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync()
           ?? new SiteSettings { Phone = DefaultPhone, Email = DefaultEmail, Address = DefaultAddress };

    public static JsonNode? Parse(string json)
    {
        try { return JsonNode.Parse(json); } catch { return null; }
    }

    // Properties that hold links. Internal paths ("/about"), http(s) URLs, mailto: and tel: are accepted.
    private static readonly HashSet<string> LinkProps = new(StringComparer.OrdinalIgnoreCase)
        { "path", "url", "link", "href", "mapLink", "ctaLink", "photo", "image", "imageSmall" };
    private static readonly HashSet<string> EmbedProps = new(StringComparer.OrdinalIgnoreCase) { "mapEmbedUrl" };

    /// <summary>Walks the JSON and validates every link-like property. Returns an error message or null.</summary>
    public static string? ValidateLinks(JsonNode? node, string where = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    var here = string.IsNullOrEmpty(where) ? name : $"{where}.{name}";
                    if (child is JsonValue v && v.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str))
                    {
                        if (LinkProps.Contains(name) && !IsValidLink(str))
                            return $"\"{str}\" is not a valid link ({here}). Use a site path like /about or a full https:// URL.";
                        if (EmbedProps.Contains(name) && !IsHttpUrl(str))
                            return $"Map embed URL must be a full https:// URL ({here}).";
                    }
                    var err = ValidateLinks(child, here);
                    if (err != null) return err;
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var err = ValidateLinks(arr[i], $"{where}[{i}]");
                    if (err != null) return err;
                }
                break;
        }
        return null;
    }

    public static bool IsHttpUrl(string s)
        => Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(u.Host);

    public static bool IsValidLink(string s)
    {
        s = s.Trim();
        if (s == "#") return true;
        if (s.StartsWith('/') && !s.StartsWith("//") && !s.Contains(' ')) return true;
        if (s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return EmailRegex.IsMatch(s[7..]);
        if (s.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return Regex.IsMatch(s[4..], @"^\+?[0-9 \-]{6,20}$");
        return IsHttpUrl(s);
    }

    public static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty).Replace("\n", "<br/>");
}

/// <summary>Tiny in-memory per-IP throttle for public POST endpoints (contact, feedback).</summary>
public static class PublicThrottle
{
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Hits = new();

    /// <returns>true when the caller is within the limit (and records the hit).</returns>
    public static bool Allow(string bucket, string? ip, int max, TimeSpan window)
    {
        var key = $"{bucket}:{ip ?? "?"}";
        var now = DateTime.UtcNow;
        var q = Hits.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > window) q.Dequeue();
            if (q.Count >= max) return false;
            q.Enqueue(now);
            return true;
        }
    }
}
