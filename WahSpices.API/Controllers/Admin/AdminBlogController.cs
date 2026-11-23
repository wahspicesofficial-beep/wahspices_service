using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/blog")]
[RequireModule("Blog")]
public class AdminBlogController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly IFileUploadService _files;

    public AdminBlogController(AppDbContext db, IFileUploadService files)
    {
        _db = db;
        _files = files;
    }

    // ── GET all ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? published,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var q = _db.BlogPosts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(b => b.Title.Contains(search) || b.Category.Contains(search));
        if (published.HasValue) q = q.Where(b => b.IsPublished == published);

        var total = await q.CountAsync();
        var list  = await q.OrderByDescending(b => b.CreatedAt)
                           .Skip((page - 1) * limit).Take(limit).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            posts = list.Select(b => new
            {
                b.Id, b.Title, b.Slug, b.Category, b.Author, b.Excerpt,
                b.ImageUrl, b.VideoUrl, b.ReadTime, b.Featured,
                b.IsPublished, b.Tags, b.CreatedAt, b.PublishedAt
            }),
            pagination = new { total, page, limit }
        }));
    }

    // ── GET by id ────────────────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var b = await _db.BlogPosts.FindAsync(id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Post not found."));
        return Ok(ApiResponse<object>.Ok(b));
    }

    // ── CREATE ───────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BlogPostRequest req)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);
        var slug = BuildSlug(req.Slug ?? req.Title);
        if (await _db.BlogPosts.AnyAsync(b => b.Slug == slug)) slug += "-" + DateTime.UtcNow.Ticks;

        var post = new BlogPost
        {
            Title       = req.Title,
            Slug        = slug,
            Excerpt     = req.Excerpt,
            Content     = req.Content ?? string.Empty,
            Category    = req.Category,
            Author      = req.Author ?? "Admin",
            ImageUrl    = req.ImageUrl,
            VideoUrl    = req.VideoUrl,
            ReadTime    = req.ReadTime ?? "5 min read",
            Tags        = req.Tags,
            Featured    = req.Featured,
            IsPublished = req.IsPublished,
            PublishedAt = req.IsPublished ? DateTime.UtcNow : null,
            SeoTitle    = req.SeoTitle,
            SeoDescription = req.SeoDescription,
            CreatedBy   = AdminName
        };

        _db.BlogPosts.Add(post);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(MapPost(post)));
    }

    // ── UPDATE ───────────────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] BlogPostRequest req)
    {
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Post not found."));
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        // Media replaced or removed: drop the old uploaded file
        if (post.ImageUrl != req.ImageUrl) _files.DeleteByUrl(post.ImageUrl);
        if (post.VideoUrl != req.VideoUrl) _files.DeleteByUrl(post.VideoUrl);

        if (!string.IsNullOrWhiteSpace(req.Slug))
        {
            var slug = BuildSlug(req.Slug);
            if (slug != post.Slug && await _db.BlogPosts.AnyAsync(b => b.Slug == slug && b.Id != id))
                return Conflict(ApiResponse<object>.Fail("SLUG_TAKEN", "Another post already uses this slug.", "slug"));
            post.Slug = slug;
        }
        post.Title      = req.Title;
        post.Excerpt    = req.Excerpt;
        post.Content    = req.Content ?? post.Content;
        post.Category   = req.Category;
        post.Author     = req.Author ?? post.Author;
        post.ImageUrl   = req.ImageUrl;
        post.VideoUrl   = req.VideoUrl;
        post.ReadTime   = req.ReadTime ?? post.ReadTime;
        post.Tags       = req.Tags;
        post.Featured   = req.Featured;
        post.IsPublished = req.IsPublished;
        if (req.IsPublished && post.PublishedAt == null) post.PublishedAt = DateTime.UtcNow;
        post.SeoTitle   = req.SeoTitle;
        post.SeoDescription = req.SeoDescription;
        post.UpdatedAt  = DateTime.UtcNow;
        post.UpdatedBy  = AdminName;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapPost(post)));
    }

    // ── DELETE ───────────────────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Post not found."));

        // Delete associated media files
        _files.DeleteByUrl(post.ImageUrl);
        _files.DeleteByUrl(post.VideoUrl);

        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── TOGGLE PUBLISH ───────────────────────────────────────────────────────
    [HttpPut("{id:int}/publish")]
    public async Task<IActionResult> TogglePublish(int id)
    {
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Post not found."));
        post.IsPublished = !post.IsPublished;
        if (post.IsPublished && post.PublishedAt == null) post.PublishedAt = DateTime.UtcNow;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { post.Id, post.IsPublished }));
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
            var result = await _files.UploadAsync(file, "blog");
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
        // Only blog uploads, and never a file a saved post still uses
        var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/uploads/blog/") || path.Contains(".."))
            return BadRequest(ApiResponse<object>.Fail("INVALID_URL", "Only uploaded blog media can be deleted here."));
        if (await _db.BlogPosts.AnyAsync(b => (b.ImageUrl != null && b.ImageUrl.EndsWith(path)) || (b.VideoUrl != null && b.VideoUrl.EndsWith(path))))
            return Ok(ApiResponse<bool>.Ok(false, "File is used by a saved post; it is removed when the post is saved without it."));
        _files.DeleteByUrl(path);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static ApiResponse<object>? Validate(BlogPostRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return ApiResponse<object>.Fail("REQUIRED", "Title is required.", "title");
        if (string.IsNullOrWhiteSpace(req.Excerpt)) return ApiResponse<object>.Fail("REQUIRED", "Excerpt is required.", "excerpt");
        if (string.IsNullOrWhiteSpace(req.Category)) return ApiResponse<object>.Fail("REQUIRED", "Category is required.", "category");
        return null;
    }

    private static object MapPost(BlogPost b) => new
    {
        b.Id, b.Title, b.Slug, b.Category, b.Author, b.Excerpt,
        b.ImageUrl, b.VideoUrl, b.ReadTime, b.Featured,
        b.IsPublished, b.Tags, b.CreatedAt, b.PublishedAt
    };

    private static string BuildSlug(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
}

public record BlogPostRequest(
    string Title,
    string? Slug,
    string Excerpt,
    string? Content,
    string Category,
    string? Author,
    string? ImageUrl,
    string? VideoUrl,
    string? ReadTime,
    string? Tags,
    string? SeoTitle,
    string? SeoDescription,
    bool Featured = false,
    bool IsPublished = true
);
