using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Services;

// ── Request / item shapes (shared by the admin API and the seeder) ─────────────────────────────
public record RegionListItem(string? Name, string? Note, string? Icon);
public record RegionSectionRequest(string? Heading, string? Body, string? ImageUrl, string? ImageAlt, string? ImageCaption, string? ImageCredit);
public record RegionRequest(
    string? Name, string? Slug, string? MapStateId, string? Tagline, string? CardText, string? Landmark,
    string? FeaturedMasala, string? AccentColor, string? HeroImageUrl, string? HeroImageCredit, string? CardImageUrl,
    List<RegionListItem>? SignatureMasalas, List<RegionListItem>? Produces, List<RegionListItem>? Dishes,
    List<string>? ProductSlugs, bool? AutoLinkProducts, List<RegionSectionRequest>? Sections,
    string? SeoTitle, string? SeoDescription, bool? IsPublished, bool? ShowOnHome, int? DisplayOrder);

/// <summary>Validation, normalisation and JSON mapping for <see cref="Region"/> (admin API, public API, seeder).</summary>
public static class RegionLogic
{
    /// <summary>States / UTs on the website's India map (ids = lowercase ISO 3166-2:IN suffix). Keep in sync with
    /// wah_frontend/src/data/indiaMap.js (generated from geoBoundaries / DataMeet, 36 units, post-2020).</summary>
    public static readonly IReadOnlyDictionary<string, string> MapStates = new Dictionary<string, string>
    {
        ["an"] = "Andaman and Nicobar Islands", ["ap"] = "Andhra Pradesh", ["ar"] = "Arunachal Pradesh", ["as"] = "Assam",
        ["br"] = "Bihar", ["ch"] = "Chandigarh", ["ct"] = "Chhattisgarh", ["dh"] = "Dadra and Nagar Haveli and Daman and Diu",
        ["dl"] = "Delhi", ["ga"] = "Goa", ["gj"] = "Gujarat", ["hr"] = "Haryana", ["hp"] = "Himachal Pradesh",
        ["jk"] = "Jammu and Kashmir", ["jh"] = "Jharkhand", ["ka"] = "Karnataka", ["kl"] = "Kerala", ["la"] = "Ladakh",
        ["ld"] = "Lakshadweep", ["mp"] = "Madhya Pradesh", ["mh"] = "Maharashtra", ["mn"] = "Manipur", ["ml"] = "Meghalaya",
        ["mz"] = "Mizoram", ["nl"] = "Nagaland", ["or"] = "Odisha", ["py"] = "Puducherry", ["pb"] = "Punjab",
        ["rj"] = "Rajasthan", ["sk"] = "Sikkim", ["tn"] = "Tamil Nadu", ["tg"] = "Telangana", ["tr"] = "Tripura",
        ["up"] = "Uttar Pradesh", ["ut"] = "Uttarakhand", ["wb"] = "West Bengal",
    };

    public static readonly Regex SlugRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex ColorRegex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex ImagePathRegex = new(@"^/(images|uploads)/[A-Za-z0-9._\-/]+\.(jpe?g|png|webp|gif|avif)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const int MaxListItems = 20, MaxSections = 12, MaxProducts = 40;

    public static string ToSlug(string text) => Regex.Replace(text.ToLowerInvariant().Trim(), "[^a-z0-9]+", "-").Trim('-');

    /// <summary>Trim, drop control characters (newlines kept when <paramref name="multiline"/>), collapse to null when empty.</summary>
    public static string? Clean(string? s, bool multiline = false)
    {
        if (s == null) return null;
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        s = new string(s.Where(c => !char.IsControl(c) || (multiline && c == '\n')).ToArray()).Trim();
        if (multiline) s = Regex.Replace(s, "\n{3,}", "\n\n");
        return s.Length == 0 ? null : s;
    }

    /// <summary>
    /// Images may be a site path (/images/… shipped with the website, /uploads/… uploaded here) or an https URL.
    /// An absolute URL pointing at this API's own /uploads/ is stored as the bare path so it survives host changes.
    /// Returns (value, error).
    /// </summary>
    public static (string? Value, string? Error) NormalizeImage(string? url, string field, string? requestHost)
    {
        url = Clean(url);
        if (url == null) return (null, null);
        if (url.Length > 512) return (null, $"{field}: the image link is too long.");
        if (url.StartsWith('/'))
            return ImagePathRegex.IsMatch(url) && !url.Contains("..") && !url.Contains("//")
                ? (url, null) : (null, $"{field}: use an uploaded image or a full https:// image URL.");
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(u.Host))
        {
            if (u.AbsolutePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) && requestHost != null
                && string.Equals(u.Authority, requestHost, StringComparison.OrdinalIgnoreCase))
                return ImagePathRegex.IsMatch(u.AbsolutePath) ? (u.AbsolutePath, null) : (null, $"{field}: not an image file.");
            if (u.Scheme != Uri.UriSchemeHttps) return (null, $"{field}: external images must use https://.");
            return (u.ToString(), null);
        }
        return (null, $"{field}: use an uploaded image or a full https:// image URL.");
    }

    private static string? Check(string? value, string label, int max, int min = 0, bool required = false)
    {
        if (value == null) return required ? $"{label} is required." : null;
        if (value.Length < min) return $"{label} must be at least {min} characters.";
        if (value.Length > max) return $"{label} must be {max} characters or fewer (now {value.Length}).";
        return null;
    }

    private static (List<RegionListItem> Items, string? Error) CleanList(List<RegionListItem>? list, string label, bool withIcon)
    {
        var outList = new List<RegionListItem>();
        if (list == null) return (outList, null);
        foreach (var it in list)
        {
            var name = Clean(it?.Name);
            var note = Clean(it?.Note);
            var icon = withIcon ? Clean(it?.Icon) : null;
            if (name == null && note == null) continue;            // blank row in the editor
            if (name == null) return (outList, $"{label}: every entry needs a name.");
            if (name.Length > 60) return (outList, $"{label}: \"{name[..20]}…\" — names must be 60 characters or fewer.");
            if (note is { Length: > 300 }) return (outList, $"{label}: notes must be 300 characters or fewer.");
            if (icon is { Length: > 8 }) return (outList, $"{label}: the icon must be a single emoji.");
            outList.Add(new RegionListItem(name, note, icon));
        }
        if (outList.Count > MaxListItems) return (outList, $"{label}: at most {MaxListItems} entries.");
        return (outList, null);
    }

    public static string? ToJson<T>(List<T> list) => list.Count == 0 ? null : JsonSerializer.Serialize(list, Json);

    public static List<T> FromJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        try { return JsonSerializer.Deserialize<List<T>>(json, Json) ?? new List<T>(); }
        catch (JsonException) { return new List<T>(); }
    }

    /// <summary>Validates a request and copies it onto <paramref name="r"/> (sections replaced). Returns (error, field) or null.</summary>
    public static (string Message, string Field)? Apply(Region r, RegionRequest req, string? requestHost)
    {
        var name = Clean(req.Name);
        var err = Check(name, "Name", 80, 2, required: true);
        if (err != null) return (err, "name");

        var slug = Clean(req.Slug)?.ToLowerInvariant() ?? ToSlug(name!);
        if (slug.Length < 2 || slug.Length > 80 || !SlugRegex.IsMatch(slug))
            return ("Slug must be 2–80 lowercase letters, digits and single dashes (e.g. tamil-nadu).", "slug");

        var map = Clean(req.MapStateId)?.ToLowerInvariant();
        if (map == null || !MapStates.ContainsKey(map)) return ("Choose the state on the India map this region belongs to.", "mapStateId");

        var tagline = Clean(req.Tagline);
        var cardText = Clean(req.CardText);
        var landmark = Clean(req.Landmark);
        var featured = Clean(req.FeaturedMasala);
        var seoTitle = Clean(req.SeoTitle);
        var seoDesc = Clean(req.SeoDescription);
        var heroCredit = Clean(req.HeroImageCredit);
        foreach (var (v, label, max, field) in new[]
        {
            (tagline, "Tagline", 120, "tagline"), (cardText, "Card text", 400, "cardText"), (landmark, "Landmark", 80, "landmark"),
            (featured, "Featured masala", 60, "featuredMasala"), (seoTitle, "SEO title", 70, "seoTitle"),
            (seoDesc, "SEO description", 160, "seoDescription"), (heroCredit, "Photo credit", 300, "heroImageCredit"),
        })
        {
            var e = Check(v, label, max);
            if (e != null) return (e, field);
        }

        var color = Clean(req.AccentColor) ?? "#1a5c2e";
        if (!ColorRegex.IsMatch(color)) return ("Accent colour must be a hex colour like #2e7d32.", "accentColor");

        var (hero, heroErr) = NormalizeImage(req.HeroImageUrl, "Hero image", requestHost);
        if (heroErr != null) return (heroErr, "heroImageUrl");
        var (card, cardErr) = NormalizeImage(req.CardImageUrl, "Card image", requestHost);
        if (cardErr != null) return (cardErr, "cardImageUrl");

        var (masalas, mErr) = CleanList(req.SignatureMasalas, "Signature masalas", false);
        if (mErr != null) return (mErr, "signatureMasalas");
        var (produces, pErr) = CleanList(req.Produces, "What grows here", true);
        if (pErr != null) return (pErr, "produces");
        var (dishes, dErr) = CleanList(req.Dishes, "Famous dishes", false);
        if (dErr != null) return (dErr, "dishes");

        var slugs = new List<string>();
        foreach (var s in req.ProductSlugs ?? new List<string>())
        {
            var ps = Clean(s)?.ToLowerInvariant();
            if (ps == null) continue;
            if (ps.Length > 200 || !SlugRegex.IsMatch(ps)) return ($"\"{ps}\" is not a valid product.", "productSlugs");
            if (!slugs.Contains(ps)) slugs.Add(ps);
        }
        if (slugs.Count > MaxProducts) return ($"Pick at most {MaxProducts} products.", "productSlugs");

        var sections = new List<RegionStorySection>();
        var i = 0;
        foreach (var s in req.Sections ?? new List<RegionSectionRequest>())
        {
            i++;
            var heading = Clean(s?.Heading);
            var body = Clean(s?.Body, multiline: true);
            if (heading == null && body == null && string.IsNullOrWhiteSpace(s?.ImageUrl)) continue;   // empty section
            var where = $"Story section {i}";
            var e = Check(heading, $"{where}: heading", 150, 2, required: true) ?? Check(body, $"{where}: text", 8000, 20, required: true);
            if (e != null) return (e, "sections");
            var (img, iErr) = NormalizeImage(s!.ImageUrl, $"{where}: image", requestHost);
            if (iErr != null) return (iErr, "sections");
            var alt = Clean(s.ImageAlt); var cap = Clean(s.ImageCaption); var cred = Clean(s.ImageCredit);
            e = Check(alt, $"{where}: image description", 200) ?? Check(cap, $"{where}: caption", 300) ?? Check(cred, $"{where}: photo credit", 300);
            if (e != null) return (e, "sections");
            sections.Add(new RegionStorySection
            {
                SortOrder = sections.Count, Heading = heading!, Body = body!, ImageUrl = img,
                ImageAlt = img == null ? null : alt, ImageCaption = img == null ? null : cap, ImageCredit = img == null ? null : cred,
            });
        }
        if (sections.Count > MaxSections) return ($"A story can have at most {MaxSections} sections.", "sections");

        var publish = req.IsPublished ?? r.IsPublished;
        if (publish && (hero == null || sections.Count == 0))
            return ("To publish, add a hero image and at least one story section (or save it as a draft).", "isPublished");

        r.Name = name!; r.Slug = slug; r.MapStateId = map; r.Tagline = tagline; r.CardText = cardText;
        r.Landmark = landmark; r.FeaturedMasala = featured; r.AccentColor = color.ToLowerInvariant();
        r.HeroImageUrl = hero; r.HeroImageCredit = hero == null ? null : heroCredit; r.CardImageUrl = card;
        r.SignatureMasalasJson = ToJson(masalas.Select(x => new { x.Name, x.Note }).ToList());
        r.ProducesJson = ToJson(produces);
        r.DishesJson = ToJson(dishes.Select(x => new { x.Name, x.Note }).ToList());
        r.ProductSlugsJson = ToJson(slugs);
        r.AutoLinkProducts = req.AutoLinkProducts ?? r.AutoLinkProducts;
        r.SeoTitle = seoTitle; r.SeoDescription = seoDesc;
        if (req.ShowOnHome.HasValue) r.ShowOnHome = req.ShowOnHome.Value;
        if (req.DisplayOrder.HasValue) r.DisplayOrder = Math.Clamp(req.DisplayOrder.Value, 0, 100_000);
        if (publish && r.PublishedAt == null) r.PublishedAt = DateTime.UtcNow;
        r.IsPublished = publish;

        // Replace the sections (the caller removes the old rows)
        r.Sections = sections;
        return null;
    }

    public static IEnumerable<string> ImageUrls(Region r) =>
        new[] { r.HeroImageUrl, r.CardImageUrl }.Concat(r.Sections.Select(s => s.ImageUrl)).Where(u => !string.IsNullOrEmpty(u))!;

    /// <summary>Keys that auto-link products by Product.State (name, slug, map-state name / id).</summary>
    public static List<string> StateKeys(Region r)
    {
        if (!r.AutoLinkProducts) return new List<string>();
        var keys = new List<string> { r.Name, r.Slug, r.MapStateId, r.Slug.Replace('-', ' ') };
        if (MapStates.TryGetValue(r.MapStateId, out var mapName)) keys.Add(mapName);
        return keys;
    }

    public static object Summary(Region r) => new
    {
        id = r.Id, name = r.Name, slug = r.Slug, mapStateId = r.MapStateId, tagline = r.Tagline, cardText = r.CardText,
        landmark = r.Landmark, featuredMasala = r.FeaturedMasala, accentColor = r.AccentColor,
        heroImageUrl = r.HeroImageUrl, heroImageCredit = r.HeroImageCredit, cardImageUrl = r.CardImageUrl ?? r.HeroImageUrl,
        showOnHome = r.ShowOnHome, displayOrder = r.DisplayOrder,
        produces = FromJson<RegionListItem>(r.ProducesJson).Select(p => new { name = p.Name, icon = p.Icon }),
        signatureMasalas = FromJson<RegionListItem>(r.SignatureMasalasJson).Select(m => m.Name),
    };

    public static object Detail(Region r, object products, object others) => new
    {
        id = r.Id, name = r.Name, slug = r.Slug, mapStateId = r.MapStateId, tagline = r.Tagline, cardText = r.CardText,
        landmark = r.Landmark, featuredMasala = r.FeaturedMasala, accentColor = r.AccentColor,
        heroImageUrl = r.HeroImageUrl, heroImageCredit = r.HeroImageCredit, cardImageUrl = r.CardImageUrl ?? r.HeroImageUrl,
        signatureMasalas = FromJson<RegionListItem>(r.SignatureMasalasJson).Select(m => new { name = m.Name, note = m.Note }),
        produces = FromJson<RegionListItem>(r.ProducesJson).Select(p => new { name = p.Name, note = p.Note, icon = p.Icon }),
        dishes = FromJson<RegionListItem>(r.DishesJson).Select(d => new { name = d.Name, note = d.Note }),
        sections = r.Sections.OrderBy(s => s.SortOrder).Select(s => new
        {
            heading = s.Heading, body = s.Body, imageUrl = s.ImageUrl, imageAlt = s.ImageAlt,
            imageCaption = s.ImageCaption, imageCredit = s.ImageCredit,
        }),
        seoTitle = r.SeoTitle, seoDescription = r.SeoDescription, isPublished = r.IsPublished,
        products, others,
    };

    /// <summary>Full editable record for the admin editor.</summary>
    public static object AdminDetail(Region r) => new
    {
        id = r.Id, name = r.Name, slug = r.Slug, mapStateId = r.MapStateId, tagline = r.Tagline, cardText = r.CardText,
        landmark = r.Landmark, featuredMasala = r.FeaturedMasala, accentColor = r.AccentColor,
        heroImageUrl = r.HeroImageUrl, heroImageCredit = r.HeroImageCredit, cardImageUrl = r.CardImageUrl,
        signatureMasalas = FromJson<RegionListItem>(r.SignatureMasalasJson).Select(m => new { name = m.Name, note = m.Note }),
        produces = FromJson<RegionListItem>(r.ProducesJson).Select(p => new { name = p.Name, note = p.Note, icon = p.Icon }),
        dishes = FromJson<RegionListItem>(r.DishesJson).Select(d => new { name = d.Name, note = d.Note }),
        productSlugs = FromJson<string>(r.ProductSlugsJson), autoLinkProducts = r.AutoLinkProducts,
        sections = r.Sections.OrderBy(s => s.SortOrder).Select(s => new
        {
            heading = s.Heading, body = s.Body, imageUrl = s.ImageUrl, imageAlt = s.ImageAlt,
            imageCaption = s.ImageCaption, imageCredit = s.ImageCredit,
        }),
        seoTitle = r.SeoTitle, seoDescription = r.SeoDescription, isPublished = r.IsPublished, showOnHome = r.ShowOnHome,
        displayOrder = r.DisplayOrder, publishedAt = r.PublishedAt, createdAt = r.CreatedAt, updatedAt = r.UpdatedAt,
        updatedBy = r.UpdatedBy ?? r.CreatedBy,
    };
}

/// <summary>
/// Startup seeder for the Regional Flavours stories: inserts every region from the embedded
/// regions.seed.json whose slug has never existed (a region the admin deleted stays deleted) and whose
/// map state isn't already used by another region. Never updates existing rows, so admin edits are kept.
/// </summary>
public class RegionSeeder : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<RegionSeeder> _logger;
    public RegionSeeder(IServiceProvider sp, ILogger<RegionSeeder> logger) { _sp = sp; _logger = logger; }

    private sealed record SeedFile(List<RegionRequest> Regions);

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("regions.seed.json", StringComparison.OrdinalIgnoreCase));
            if (res == null) { _logger.LogWarning("RegionSeeder: regions.seed.json resource not found"); return; }
            await using var stream = asm.GetManifestResourceStream(res)!;
            var seed = await JsonSerializer.DeserializeAsync<SeedFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            if (seed?.Regions == null || seed.Regions.Count == 0) return;

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var all = await db.Regions.IgnoreQueryFilters().Select(r => new { r.Slug, r.MapStateId, r.IsDeleted }).ToListAsync(ct);
            var slugs = all.Select(r => r.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var maps = all.Where(r => !r.IsDeleted).Select(r => r.MapStateId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var (req, idx) in seed.Regions.Select((r, i) => (r, i)))
            {
                var slug = req.Slug ?? RegionLogic.ToSlug(req.Name ?? "");
                if (slugs.Contains(slug) || (req.MapStateId != null && maps.Contains(req.MapStateId))) continue;
                var region = new Region { CreatedBy = "seed", AutoLinkProducts = true, ShowOnHome = true };
                var err = RegionLogic.Apply(region, req with { DisplayOrder = req.DisplayOrder ?? (idx + 1) * 10 }, null);
                if (err != null) { _logger.LogWarning("RegionSeeder: skipped {Slug}: {Error}", slug, err.Value.Message); continue; }
                db.Regions.Add(region);
                slugs.Add(region.Slug); maps.Add(region.MapStateId);
                added++;
            }
            if (added > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("RegionSeeder: seeded {Count} regional stories", added);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RegionSeeder failed");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
