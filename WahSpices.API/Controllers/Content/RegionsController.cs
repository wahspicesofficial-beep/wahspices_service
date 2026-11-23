using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Content;

/// <summary>Public "Regional Flavours": published state stories for the home carousel, the India map and /regions pages.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/regions")]
[Produces("application/json")]
public class RegionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IProductService _products;
    public RegionsController(AppDbContext db, IProductService products) { _db = db; _products = products; }

    /// <summary>Published regions in display order. home=true → only those flagged "show on home".</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? home)
    {
        var q = _db.Regions.AsNoTracking().Where(r => r.IsPublished);
        if (home == true) q = q.Where(r => r.ShowOnHome);
        var rows = await q.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name).ToListAsync();
        Response.Headers.CacheControl = "no-cache";
        return Ok(ApiResponse<object>.Ok(rows.Select(RegionLogic.Summary)));
    }

    /// <summary>Map states that have a published story (for highlighting the India map).</summary>
    [HttpGet("map")]
    public async Task<IActionResult> Map()
    {
        var rows = await _db.Regions.AsNoTracking().Where(r => r.IsPublished)
            .OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name)
            .Select(r => new { mapStateId = r.MapStateId, slug = r.Slug, name = r.Name, tagline = r.Tagline, accentColor = r.AccentColor })
            .ToListAsync();
        Response.Headers.CacheControl = "no-cache";
        return Ok(ApiResponse<object>.Ok(rows));
    }

    /// <summary>One published region with its story, products and the other regions (for "explore more").</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug)
    {
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (slug.Length > 80 || !RegionLogic.SlugRegex.IsMatch(slug))
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Region not found."));
        var r = await _db.Regions.AsNoTracking().Include(x => x.Sections)
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Region not found."));

        var products = await _products.GetForRegionAsync(RegionLogic.FromJson<string>(r.ProductSlugsJson), RegionLogic.StateKeys(r));
        var others = await _db.Regions.AsNoTracking().Where(x => x.IsPublished && x.Id != r.Id)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();
        Response.Headers.CacheControl = "no-cache";
        return Ok(ApiResponse<object>.Ok(RegionLogic.Detail(r, products, others.Select(RegionLogic.Summary))));
    }
}
