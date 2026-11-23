using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Product;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Services;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/products")]
[RequireModule("Products")]
public class AdminProductsController : AdminBaseController
{
    private readonly IAdminProductService _service;
    private readonly AppDbContext _db;
    private readonly IFileUploadService _uploads;

    public AdminProductsController(IAdminProductService service, AppDbContext db, IFileUploadService uploads)
    {
        _service = service;
        _db = db;
        _uploads = uploads;
    }

    [RequireModule(AdminModules.AnyAdmin)]   // product pickers across modules (catalog is public anyway)
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? category, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var result = await _service.GetAllAsync(category, search, page, limit);
        return Ok(result);
    }

    [RequireModule(AdminModules.AnyAdmin)]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var result = await _service.CreateAsync(request);
        return result.Success ? StatusCode(201, result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateProductRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("{id:int}/stock")]
    public async Task<IActionResult> ToggleStock(int id, [FromBody] ToggleStockRequest request)
    {
        var result = await _service.ToggleStockAsync(id, request.InStock);
        return Ok(result);
    }

    /// Upload / replace the product image (multipart field "file").
    [HttpPost("{id:int}/image")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("NO_FILE", "Please choose an image file."));

        var existing = await _db.Products.Where(p => p.Id == id).Select(p => p.ImageUrl).FirstOrDefaultAsync();
        UploadResult up;
        try { up = await _uploads.UploadAsync(file, "products"); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object>.Fail("INVALID_FILE", ex.Message)); }

        // Store a host-independent path; the frontend prefixes the API origin.
        var relative = new Uri(up.Url, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(up.Url).AbsolutePath : up.Url;
        var result = await _service.SetImageAsync(id, relative);
        if (!result.Success) { _uploads.DeleteByUrl(relative); return NotFound(result); }

        // Remove the previous uploaded file (seeded catalog images are kept)
        if (existing != null && existing != relative && !existing.StartsWith("/uploads/products/catalog/"))
            _uploads.DeleteByUrl(existing);
        return Ok(result);
    }

    // ── Pack Types ────────────────────────────────────────────────────────────

    [RequireModule(AdminModules.AnyAdmin)]
    [HttpGet("pack-types")]
    public async Task<IActionResult> GetPackTypes()
    {
        var types = await _db.PackTypeConfigs.Where(p => !p.IsDeleted).OrderBy(p => p.Name).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { packTypes = types.Select(p => new { p.Id, p.Name, p.IsActive }) }));
    }

    [HttpPost("pack-types")]
    public async Task<IActionResult> CreatePackType([FromBody] PackTypeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Name is required."));
        var exists = await _db.PackTypeConfigs.AnyAsync(p => p.Name == req.Name.Trim() && !p.IsDeleted);
        if (exists) return Conflict(ApiResponse<object>.Fail("DUPLICATE", "Pack type already exists."));
        var pt = new PackTypeConfig { Name = req.Name.Trim(), IsActive = true, CreatedBy = AdminName };
        _db.PackTypeConfigs.Add(pt);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { pt.Id, pt.Name, pt.IsActive }));
    }

    [HttpPut("pack-types/{id:int}")]
    public async Task<IActionResult> UpdatePackType(int id, [FromBody] PackTypeRequest req)
    {
        var pt = await _db.PackTypeConfigs.FindAsync(id);
        if (pt == null || pt.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Pack type not found."));
        if (!string.IsNullOrWhiteSpace(req.Name) && req.Name.Trim() != pt.Name)
        {
            var newName = req.Name.Trim();
            if (await _db.PackTypeConfigs.AnyAsync(p => p.Id != id && p.Name == newName && !p.IsDeleted))
                return Conflict(ApiResponse<object>.Fail("DUPLICATE", "Pack type already exists."));
            // Keep products pointing at the renamed pack type
            var oldName = pt.Name;
            var products = await _db.Products.Where(p => p.PackType == oldName).ToListAsync();
            foreach (var p in products) { p.PackType = newName; p.UpdatedAt = DateTime.UtcNow; }
            pt.Name = newName;
        }
        if (req.IsActive.HasValue) pt.IsActive = req.IsActive.Value;
        pt.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { pt.Id, pt.Name, pt.IsActive }));
    }

    [HttpDelete("pack-types/{id:int}")]
    public async Task<IActionResult> DeletePackType(int id)
    {
        var pt = await _db.PackTypeConfigs.FindAsync(id);
        if (pt == null || pt.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Pack type not found."));
        var inUse = await _db.Products.CountAsync(p => p.PackType == pt.Name);
        if (inUse > 0)
            return BadRequest(ApiResponse<object>.Fail("IN_USE", $"{inUse} product(s) use this pack type. Change them first, or mark the pack type inactive."));
        pt.IsDeleted = true; pt.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}

public record ToggleStockRequest(bool InStock);
public record PackTypeRequest(string? Name, bool? IsActive);
