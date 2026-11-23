using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

public record RegionPublishRequest(bool Published);
public record RegionHomeRequest(bool ShowOnHome);
public record RegionReorderRequest(List<int>? Ids);

/// <summary>Admin → Website → Regions: the "Regional Flavours" state stories (home carousel + /regions pages).</summary>
[Route("api/admin/regions")]
[RequireModule("Website")]
public class AdminRegionsController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly IFileUploadService _files;
    private readonly IProductService _products;

    public AdminRegionsController(AppDbContext db, IFileUploadService files, IProductService products)
    { _db = db; _files = files; _products = products; }

    private string? Host => HttpContext?.Request.Host.Value;
    private static ApiResponse<object> Fail(string code, string msg, string? field = null) => ApiResponse<object>.Fail(code, msg, field);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.Regions.AsNoTracking()
            .OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.Slug, r.MapStateId, r.Tagline, r.AccentColor, r.HeroImageUrl, r.CardImageUrl,
                r.IsPublished, r.ShowOnHome, r.DisplayOrder, r.PublishedAt, r.UpdatedAt, r.CreatedAt,
                UpdatedBy = r.UpdatedBy ?? r.CreatedBy,
                SectionCount = r.Sections.Count,
                r.ProductSlugsJson,
            })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            items = rows.Select(r => new
            {
                r.Id, r.Name, r.Slug, r.MapStateId, r.Tagline, r.AccentColor, r.HeroImageUrl,
                CardImageUrl = r.CardImageUrl ?? r.HeroImageUrl,
                r.IsPublished, r.ShowOnHome, r.DisplayOrder, r.PublishedAt, UpdatedAt = r.UpdatedAt ?? r.CreatedAt, r.UpdatedBy,
                r.SectionCount, ProductCount = RegionLogic.FromJson<string>(r.ProductSlugsJson).Count,
            }),
            mapStates = RegionLogic.MapStates.Select(kv => new { id = kv.Key, name = kv.Value }).OrderBy(x => x.name),
        }));
    }

    [HttpGet("map-states")]
    public IActionResult MapStates() =>
        Ok(ApiResponse<object>.Ok(RegionLogic.MapStates.Select(kv => new { id = kv.Key, name = kv.Value }).OrderBy(x => x.name)));

    /// <summary>Lightweight product list for the "linked products" picker (no Products permission needed).</summary>
    [HttpGet("product-options")]
    public async Task<IActionResult> ProductOptions()
    {
        var items = await _db.Products.AsNoTracking().Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Slug, p.ImageUrl, p.CategorySlug, p.State, p.IsDraft })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var r = await _db.Regions.AsNoTracking().Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        return r == null ? NotFound(Fail("NOT_FOUND", "Region not found.")) : Ok(ApiResponse<object>.Ok(RegionLogic.AdminDetail(r)));
    }

    /// <summary>The public page payload for any region (drafts included) — used by the "Preview" link.</summary>
    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id)
    {
        var r = await _db.Regions.AsNoTracking().Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(Fail("NOT_FOUND", "Region not found."));
        var products = await _products.GetForRegionAsync(RegionLogic.FromJson<string>(r.ProductSlugsJson), RegionLogic.StateKeys(r));
        var others = await _db.Regions.AsNoTracking().Where(x => x.IsPublished && x.Id != r.Id)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();
        return Ok(ApiResponse<object>.Ok(RegionLogic.Detail(r, products, others.Select(RegionLogic.Summary))));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RegionRequest req)
    {
        var r = new Region { CreatedBy = AdminName };
        var maxOrder = await _db.Regions.Select(x => (int?)x.DisplayOrder).MaxAsync() ?? 0;
        var err = RegionLogic.Apply(r, req with { DisplayOrder = req.DisplayOrder ?? maxOrder + 10 }, Host);
        if (err != null) return BadRequest(Fail("VALIDATION", err.Value.Message, err.Value.Field));
        var conflict = await Conflicts(r);
        if (conflict != null) return conflict;

        _db.Regions.Add(r);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(RegionLogic.AdminDetail(r), r.IsPublished ? "Region published" : "Draft saved"));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RegionRequest req)
    {
        var r = await _db.Regions.Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(Fail("NOT_FOUND", "Region not found."));
        var oldImages = RegionLogic.ImageUrls(r).ToList();
        var oldSections = r.Sections.ToList();

        var err = RegionLogic.Apply(r, req, Host);
        if (err != null) return BadRequest(Fail("VALIDATION", err.Value.Message, err.Value.Field));
        var conflict = await Conflicts(r);
        if (conflict != null) return conflict;

        _db.RegionStorySections.RemoveRange(oldSections);
        r.UpdatedAt = DateTime.UtcNow;
        r.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();

        // Uploaded images that are no longer used by this region are removed from disk
        var now = RegionLogic.ImageUrls(r).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var url in oldImages.Where(u => !now.Contains(u))) await DeleteIfUnused(url);
        return Ok(ApiResponse<object>.Ok(RegionLogic.AdminDetail(r), "Region saved"));
    }

    [HttpPatch("{id:int}/publish")]
    public async Task<IActionResult> Publish(int id, [FromBody] RegionPublishRequest req)
    {
        var r = await _db.Regions.Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(Fail("NOT_FOUND", "Region not found."));
        if (req.Published && (string.IsNullOrEmpty(r.HeroImageUrl) || r.Sections.Count == 0))
            return BadRequest(Fail("VALIDATION", "Add a hero image and at least one story section before publishing.", "isPublished"));
        r.IsPublished = req.Published;
        if (req.Published && r.PublishedAt == null) r.PublishedAt = DateTime.UtcNow;
        r.UpdatedAt = DateTime.UtcNow; r.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { r.Id, r.IsPublished }, req.Published ? "Published — it's live on the website" : "Unpublished (hidden from the website)"));
    }

    [HttpPatch("{id:int}/home")]
    public async Task<IActionResult> Home(int id, [FromBody] RegionHomeRequest req)
    {
        var r = await _db.Regions.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(Fail("NOT_FOUND", "Region not found."));
        r.ShowOnHome = req.ShowOnHome;
        r.UpdatedAt = DateTime.UtcNow; r.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { r.Id, r.ShowOnHome }, req.ShowOnHome ? "Shown in the home-page carousel" : "Hidden from the home page"));
    }

    /// <summary>New display order: the ids in order (regions not listed keep their relative order after them).</summary>
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] RegionReorderRequest req)
    {
        var ids = (req.Ids ?? new List<int>()).Distinct().ToList();
        if (ids.Count == 0 || ids.Count > 500) return BadRequest(Fail("VALIDATION", "Send the region ids in their new order."));
        var all = await _db.Regions.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name).ToListAsync();
        var ordered = ids.Select(id => all.FirstOrDefault(r => r.Id == id)).Where(r => r != null).Concat(all.Where(r => !ids.Contains(r.Id))).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var target = (i + 1) * 10;
            if (ordered[i]!.DisplayOrder != target) { ordered[i]!.DisplayOrder = target; ordered[i]!.UpdatedAt = DateTime.UtcNow; ordered[i]!.UpdatedBy = AdminName; }
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(ordered.Select(r => new { r!.Id, r.DisplayOrder }), "Order saved"));
    }

    /// <summary>Soft delete (the row is kept so the startup seeder never re-creates a seeded region).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await _db.Regions.Include(x => x.Sections).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(Fail("NOT_FOUND", "Region not found."));
        var images = RegionLogic.ImageUrls(r).ToList();
        r.IsDeleted = true; r.IsPublished = false;
        r.UpdatedAt = DateTime.UtcNow; r.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        foreach (var url in images) await DeleteIfUnused(url);
        return Ok(ApiResponse<object>.Ok(new { id }, "Region deleted"));
    }

    // ── Images ────────────────────────────────────────────────────────────────
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxImageBytes = 8L * 1024 * 1024;

    /// <summary>Photo for a region (hero / story section). JPG, PNG or WEBP up to 8 MB; contents checked by magic bytes.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(9 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 9 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(Fail("NO_FILE", "Choose an image to upload."));
        var ext = Path.GetExtension(Path.GetFileName(file.FileName ?? "")).ToLowerInvariant();
        if (!ImageExts.Contains(ext)) return BadRequest(Fail("UPLOAD_ERROR", "Use a JPG, PNG or WEBP image."));
        if (file.Length > MaxImageBytes) return BadRequest(Fail("UPLOAD_ERROR", "The image must be 8 MB or smaller."));
        try
        {
            var up = await _files.UploadAsync(file, "regions");
            var path = new Uri(up.Url, UriKind.RelativeOrAbsolute) is { IsAbsoluteUri: true } u ? u.AbsolutePath : up.Url;
            return Ok(ApiResponse<object>.Ok(new { url = path }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Fail("UPLOAD_ERROR", ex.Message));
        }
    }

    /// <summary>Remove an uploaded region image that no saved region uses (e.g. replaced before saving).</summary>
    [HttpDelete("upload")]
    public async Task<IActionResult> DeleteUpload([FromQuery] string? url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/uploads/regions/", StringComparison.OrdinalIgnoreCase) || path.Contains(".."))
            return BadRequest(Fail("INVALID_URL", "Only uploaded region images can be deleted here."));
        var deleted = await DeleteIfUnused(path);
        return Ok(ApiResponse<bool>.Ok(deleted, deleted ? "Deleted" : "Still used by a saved region"));
    }

    private async Task<bool> DeleteIfUnused(string url)
    {
        if (!url.StartsWith("/uploads/regions/", StringComparison.OrdinalIgnoreCase)) return false;   // site images / external links are never deleted
        var used = await _db.Regions.AnyAsync(r => !r.IsDeleted && (r.HeroImageUrl == url || r.CardImageUrl == url))
                   || await _db.RegionStorySections.AnyAsync(s => s.ImageUrl == url && !s.Region.IsDeleted);
        if (used) return false;
        _files.DeleteByUrl(url);
        return true;
    }

    private async Task<IActionResult?> Conflicts(Region r)
    {
        if (await _db.Regions.AnyAsync(x => x.Id != r.Id && x.Slug == r.Slug))
            return Conflict(Fail("SLUG_TAKEN", $"Another region already uses the address /regions/{r.Slug}.", "slug"));
        if (await _db.Regions.AnyAsync(x => x.Id != r.Id && x.MapStateId == r.MapStateId))
            return Conflict(Fail("MAP_STATE_TAKEN", $"{RegionLogic.MapStates[r.MapStateId]} already has a region story — edit that one instead.", "mapStateId"));
        return null;
    }
}
