using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/recipes")]
[RequireModule("Recipes")]
public class AdminRecipesController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly IFileUploadService _files;

    public AdminRecipesController(AppDbContext db, IFileUploadService files)
    {
        _db = db;
        _files = files;
    }

    // ── GET all ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] bool? published,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var q = _db.Recipes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(r => r.Title.Contains(search) || (r.Description != null && r.Description.Contains(search)));
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(r => r.Category == category);
        if (published.HasValue) q = q.Where(r => r.IsPublished == published);

        var total = await q.CountAsync();
        var list  = await q.OrderByDescending(r => r.CreatedAt)
                           .Skip((page - 1) * limit).Take(limit).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            recipes = list.Select(r => new
            {
                r.Id, r.Title, r.Slug, r.Category, r.Description,
                r.CookTime, r.PrepTime, r.Servings, r.Difficulty,
                r.ImageUrl, r.VideoUrl, r.Featured, r.IsPublished,
                r.Tags, r.RelatedMasala, r.CreatedAt, r.PublishedAt
            }),
            pagination = new { total, page, limit }
        }));
    }

    // ── GET by id ────────────────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var r = await _db.Recipes.FindAsync(id);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Recipe not found."));
        return Ok(ApiResponse<object>.Ok(r));
    }

    // ── CREATE ───────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecipeRequest req)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);
        var slug = BuildSlug(req.Slug ?? req.Title);
        if (await _db.Recipes.AnyAsync(r => r.Slug == slug)) slug += "-" + DateTime.UtcNow.Ticks;

        var recipe = new Recipe
        {
            Title        = req.Title,
            Slug         = slug,
            Description  = req.Description,
            Category     = req.Category ?? "General",
            CookTime     = req.CookTime,
            PrepTime     = req.PrepTime,
            Servings     = req.Servings ?? 2,
            Difficulty   = req.Difficulty ?? "Easy",
            Ingredients  = req.Ingredients,
            Steps        = req.Steps,
            Tips         = req.Tips,
            ImageUrl     = req.ImageUrl,
            VideoUrl     = req.VideoUrl,
            RelatedMasala = req.RelatedMasala,
            Tags         = req.Tags,
            Featured     = req.Featured,
            IsPublished  = req.IsPublished,
            PublishedAt  = req.IsPublished ? DateTime.UtcNow : null,
            SeoTitle     = req.SeoTitle,
            SeoDescription = req.SeoDescription,
            CreatedBy    = AdminName
        };

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(MapRecipe(recipe)));
    }

    // ── UPDATE ───────────────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RecipeRequest req)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Recipe not found."));
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        // Media replaced or removed: drop the old uploaded file
        if (recipe.ImageUrl != req.ImageUrl) _files.DeleteByUrl(recipe.ImageUrl);
        if (recipe.VideoUrl != req.VideoUrl) _files.DeleteByUrl(recipe.VideoUrl);

        if (!string.IsNullOrWhiteSpace(req.Slug))
        {
            var slug = BuildSlug(req.Slug);
            if (slug != recipe.Slug && await _db.Recipes.AnyAsync(r => r.Slug == slug && r.Id != id))
                return Conflict(ApiResponse<object>.Fail("SLUG_TAKEN", "Another recipe already uses this slug.", "slug"));
            recipe.Slug = slug;
        }
        recipe.Title        = req.Title;
        recipe.Description  = req.Description;
        recipe.Category     = req.Category ?? recipe.Category;
        recipe.CookTime     = req.CookTime;
        recipe.PrepTime     = req.PrepTime;
        recipe.Servings     = req.Servings ?? recipe.Servings;
        recipe.Difficulty   = req.Difficulty ?? recipe.Difficulty;
        recipe.Ingredients  = req.Ingredients;
        recipe.Steps        = req.Steps;
        recipe.Tips         = req.Tips;
        recipe.ImageUrl     = req.ImageUrl;
        recipe.VideoUrl     = req.VideoUrl;
        recipe.RelatedMasala = req.RelatedMasala;
        recipe.Tags         = req.Tags;
        recipe.Featured     = req.Featured;
        recipe.IsPublished  = req.IsPublished;
        if (req.IsPublished && recipe.PublishedAt == null) recipe.PublishedAt = DateTime.UtcNow;
        recipe.SeoTitle     = req.SeoTitle;
        recipe.SeoDescription = req.SeoDescription;
        recipe.UpdatedAt    = DateTime.UtcNow;
        recipe.UpdatedBy    = AdminName;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapRecipe(recipe)));
    }

    // ── DELETE ───────────────────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Recipe not found."));

        _files.DeleteByUrl(recipe.ImageUrl);
        _files.DeleteByUrl(recipe.VideoUrl);

        _db.Recipes.Remove(recipe);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── TOGGLE PUBLISH ───────────────────────────────────────────────────────
    [HttpPut("{id:int}/publish")]
    public async Task<IActionResult> TogglePublish(int id)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Recipe not found."));
        recipe.IsPublished = !recipe.IsPublished;
        if (recipe.IsPublished && recipe.PublishedAt == null) recipe.PublishedAt = DateTime.UtcNow;
        recipe.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { recipe.Id, recipe.IsPublished }));
    }

    // ── UPLOAD MEDIA ─────────────────────────────────────────────────────────
    [HttpPost("upload")]
    [RequestSizeLimit(210 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 210 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("NO_FILE", "No file provided."));
        try
        {
            var result = await _files.UploadAsync(file, "recipes");
            return Ok(ApiResponse<object>.Ok(new { url = result.Url }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("UPLOAD_ERROR", ex.Message));
        }
    }

    // ── DELETE MEDIA FILE ─────────────────────────────────────────────────────
    [HttpDelete("upload")]
    public async Task<IActionResult> DeleteUpload([FromQuery] string url)
    {
        // Only recipe uploads, and never a file a saved recipe still uses
        var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/uploads/recipes/") || path.Contains(".."))
            return BadRequest(ApiResponse<object>.Fail("INVALID_URL", "Only uploaded recipe media can be deleted here."));
        if (await _db.Recipes.AnyAsync(r => (r.ImageUrl != null && r.ImageUrl.EndsWith(path)) || (r.VideoUrl != null && r.VideoUrl.EndsWith(path))))
            return Ok(ApiResponse<bool>.Ok(false, "File is used by a saved recipe; it is removed when the recipe is saved without it."));
        _files.DeleteByUrl(path);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private static ApiResponse<object>? Validate(RecipeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return ApiResponse<object>.Fail("REQUIRED", "Title is required.", "title");
        if (req.Servings.HasValue && req.Servings <= 0) return ApiResponse<object>.Fail("INVALID", "Servings must be at least 1.", "servings");
        foreach (var (json, field) in new[] { (req.Ingredients, "ingredients"), (req.Steps, "steps"), (req.Tags, "tags") })
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try { System.Text.Json.JsonDocument.Parse(json); }
            catch { return ApiResponse<object>.Fail("INVALID_JSON", $"{field} must be a JSON array.", field); }
        }
        return null;
    }

    private static object MapRecipe(Recipe r) => new
    {
        r.Id, r.Title, r.Slug, r.Category, r.Description,
        r.CookTime, r.PrepTime, r.Servings, r.Difficulty,
        r.ImageUrl, r.VideoUrl, r.Featured, r.IsPublished,
        r.Tags, r.RelatedMasala, r.CreatedAt, r.PublishedAt
    };

    private static string BuildSlug(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
}

public record RecipeRequest(
    string Title,
    string? Slug,
    string? Description,
    string? Category,
    string? CookTime,
    string? PrepTime,
    int? Servings,
    string? Difficulty,
    string? Ingredients,
    string? Steps,
    string? Tips,
    string? ImageUrl,
    string? VideoUrl,
    string? RelatedMasala,
    string? Tags,
    string? SeoTitle,
    string? SeoDescription,
    bool Featured = false,
    bool IsPublished = true
);
