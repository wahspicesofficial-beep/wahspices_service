using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Content;

/// <summary>
/// Public "Recipes &amp; Knowledge" API for the customer website: published blog posts and recipes only.
/// Managed from the admin Blog / Recipes modules.
/// </summary>
[ApiController]
[Produces("application/json")]
public class PublicContentController : ControllerBase
{
    private readonly AppDbContext _db;
    public PublicContentController(AppDbContext db) => _db = db;

    private const string DefaultBlogColor = "#1a5c2e";
    private static readonly Dictionary<string, string> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Recipe"] = "#c8371e", ["Health"] = "#2e7d32", ["Knowledge"] = "#1565c0",
    };

    public record RelatedProductDto(int Id, string Name, string Slug, string? ImageUrl, decimal Price, decimal Mrp, string Weight, bool InStock);

    // ── Blog ────────────────────────────────────────────────────────────────

    /// <summary>Published blog posts (newest first). Filter by category / featured / search; paged.</summary>
    [HttpGet("api/blog")]
    public async Task<IActionResult> GetPosts(
        [FromQuery] string? category, [FromQuery] bool? featured, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int limit = 12)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 50);
        var published = _db.BlogPosts.AsNoTracking().Where(b => !b.IsDeleted && b.IsPublished);
        var q = published;
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
            q = q.Where(b => b.Category == category);
        if (featured.HasValue) q = q.Where(b => b.Featured == featured.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(b => b.Title.Contains(s) || b.Excerpt.Contains(s) || (b.Tags != null && b.Tags.Contains(s)));
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(b => b.Featured).ThenByDescending(b => b.PublishedAt ?? b.CreatedAt)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        var categories = await published.Select(b => b.Category).Distinct().ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Items = rows.Select(b => MapPost(b, withContent: false, null)).ToList(),
            Total = total, Page = page, Limit = limit,
            Categories = categories.Where(c => !string.IsNullOrWhiteSpace(c)).OrderBy(c => c).ToList(),
        }));
    }

    /// <summary>One published blog post by slug, with the catalog products it mentions.</summary>
    [HttpGet("api/blog/{slug}")]
    public async Task<IActionResult> GetPost(string slug)
    {
        var b = await _db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && !x.IsDeleted && x.IsPublished);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Article not found"));

        // Related products: the post's Masala field and/or tags that exactly match a product name
        var names = ParseList(b.Tags);
        if (!string.IsNullOrWhiteSpace(b.Masala)) names.Insert(0, b.Masala!);
        var related = await RelatedByNamesAsync(names);
        return Ok(ApiResponse<object>.Ok(MapPost(b, withContent: true, related)));
    }

    // ── Recipes ─────────────────────────────────────────────────────────────

    /// <summary>Published recipes (newest first). Filter by category / difficulty / featured / masala; paged.</summary>
    [HttpGet("api/recipes")]
    public async Task<IActionResult> GetRecipes(
        [FromQuery] string? category, [FromQuery] string? difficulty, [FromQuery] bool? featured,
        [FromQuery] string? masala, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int limit = 12)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 50);
        var published = _db.Recipes.AsNoTracking().Where(r => !r.IsDeleted && r.IsPublished);
        var q = published;
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
            q = q.Where(r => r.Category == category);
        if (!string.IsNullOrWhiteSpace(difficulty)) q = q.Where(r => r.Difficulty == difficulty);
        if (featured.HasValue) q = q.Where(r => r.Featured == featured.Value);
        if (!string.IsNullOrWhiteSpace(masala)) q = q.Where(r => r.RelatedMasala == masala);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.Title.Contains(s) || (r.Description != null && r.Description.Contains(s)) || (r.Tags != null && r.Tags.Contains(s)));
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(r => r.Featured).ThenByDescending(r => r.PublishedAt ?? r.CreatedAt)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        var products = await ProductsBySlugAsync(rows.Select(r => r.RelatedMasala));
        var categories = await published.Select(r => r.Category).Distinct().ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Items = rows.Select(r => MapRecipe(r, full: false, products)).ToList(),
            Total = total, Page = page, Limit = limit,
            Categories = categories.Where(c => !string.IsNullOrWhiteSpace(c)).OrderBy(c => c).ToList(),
        }));
    }

    /// <summary>One published recipe by slug (ingredients, steps, tips and the masala it uses).</summary>
    [HttpGet("api/recipes/{slug}")]
    public async Task<IActionResult> GetRecipe(string slug)
    {
        var r = await _db.Recipes.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && !x.IsDeleted && x.IsPublished);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Recipe not found"));
        var products = await ProductsBySlugAsync(new[] { r.RelatedMasala });
        return Ok(ApiResponse<object>.Ok(MapRecipe(r, full: true, products)));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private object MapPost(BlogPost b, bool withContent, List<RelatedProductDto>? related) => new
    {
        b.Id, b.Title, b.Slug, b.Excerpt,
        Content = withContent ? b.Content : null,
        b.Category, b.Author, b.AuthorAvatar, b.ImageUrl,
        BgColor = !string.IsNullOrWhiteSpace(b.BgColor) ? b.BgColor : CategoryColors.GetValueOrDefault(b.Category ?? "", DefaultBlogColor),
        b.ReadTime, Tags = ParseList(b.Tags), b.Masala, b.Featured, b.VideoUrl,
        PublishedAt = b.PublishedAt ?? b.CreatedAt,
        SeoTitle = withContent ? b.SeoTitle : null,
        SeoDescription = withContent ? b.SeoDescription : null,
        RelatedProducts = related,
    };

    private static object MapRecipe(Recipe r, bool full, Dictionary<string, RelatedProductDto> products) => new
    {
        r.Id, r.Title, r.Slug, r.Description, r.Category, r.CookTime, r.PrepTime, r.Servings, r.Difficulty,
        Ingredients = full ? ParseList(r.Ingredients) : null,
        Steps = full ? ParseSteps(r.Steps) : null,
        Tips = full ? r.Tips : null,
        r.ImageUrl, r.VideoUrl, r.RelatedMasala,
        RelatedProduct = r.RelatedMasala != null ? products.GetValueOrDefault(r.RelatedMasala) : null,
        Tags = ParseList(r.Tags), r.Featured,
        PublishedAt = r.PublishedAt ?? r.CreatedAt,
        SeoTitle = full ? r.SeoTitle : null,
        SeoDescription = full ? r.SeoDescription : null,
    };

    private async Task<Dictionary<string, RelatedProductDto>> ProductsBySlugAsync(IEnumerable<string?> slugs)
    {
        var list = slugs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.Products.AsNoTracking()
            .Where(p => list.Contains(p.Slug) && !p.IsDeleted && !p.IsDraft)
            .Select(p => new RelatedProductDto(p.Id, p.Name, p.Slug, p.ImageUrl, p.Price, p.Mrp, p.Weight, p.InStock))
            .ToDictionaryAsync(p => p.Slug);
    }

    private async Task<List<RelatedProductDto>> RelatedByNamesAsync(List<string> names)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return new();
        var rows = await _db.Products.AsNoTracking()
            .Where(p => (list.Contains(p.Name) || list.Contains(p.Slug)) && !p.IsDeleted && !p.IsDraft)
            .Select(p => new RelatedProductDto(p.Id, p.Name, p.Slug, p.ImageUrl, p.Price, p.Mrp, p.Weight, p.InStock))
            .ToListAsync();
        // Keep the author's order
        return rows.OrderBy(p => list.FindIndex(n => n.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || n.Equals(p.Slug, StringComparison.OrdinalIgnoreCase)))
            .Take(4).ToList();
    }

    /// <summary>JSON array string → list; tolerates comma/newline separated text.</summary>
    private static List<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        var t = raw.Trim();
        if (t.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
            }
            catch (JsonException) { /* fall through to plain text */ }
        }
        return t.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>Steps are stored as [{"step":1,"instruction":"…"}] (admin) or a plain string array.</summary>
    private static List<string> ParseSteps(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        var t = raw.Trim();
        if (t.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                var steps = new List<(int order, string text)>();
                var i = 0;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    i++;
                    if (e.ValueKind == JsonValueKind.String) { var s = e.GetString(); if (!string.IsNullOrWhiteSpace(s)) steps.Add((i, s.Trim())); }
                    else if (e.ValueKind == JsonValueKind.Object)
                    {
                        var text = e.TryGetProperty("instruction", out var ins) ? ins.GetString()
                                 : e.TryGetProperty("text", out var tx) ? tx.GetString() : null;
                        var order = e.TryGetProperty("step", out var st) && st.TryGetInt32(out var n) ? n : i;
                        if (!string.IsNullOrWhiteSpace(text)) steps.Add((order, text.Trim()));
                    }
                }
                return steps.OrderBy(s => s.order).Select(s => s.text).ToList();
            }
            catch (JsonException) { /* plain text */ }
        }
        return t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
