using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.DTOs.Category;
using WahSpices.Application.Interfaces.Services;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/categories")]
[RequireModule("Categories")]
public class AdminCategoriesController : AdminBaseController
{
    private readonly ICategoryService _svc;
    public AdminCategoriesController(ICategoryService svc) => _svc = svc;

    /// <summary>Get all categories including inactive ones</summary>
    [RequireModule(AdminModules.AnyAdmin)]   // category picker on products/inventory pages
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _svc.GetAllAsync(includeInactive: true));

    /// <summary>Create a new category</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest(WahSpices.Application.Common.ApiResponse<object>.Fail("REQUIRED", "Name and slug are required."));
        var r = await _svc.CreateAsync(request);
        return r.Success ? StatusCode(201, r) : (r.Error?.Code == "SLUG_TAKEN" ? Conflict(r) : BadRequest(r));
    }

    /// <summary>Update an existing category</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(WahSpices.Application.Common.ApiResponse<object>.Fail("REQUIRED", "Name is required.", "name"));
        var r = await _svc.UpdateAsync(id, request);
        return r.Success ? Ok(r) : (r.Error?.Code == "NOT_FOUND" ? NotFound(r) : BadRequest(r));
    }

    /// <summary>Soft-delete a category (only if no products use it)</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await _svc.DeleteAsync(id);
        return r.Success ? Ok(r) : (r.Error?.Code == "NOT_FOUND" ? NotFound(r) : BadRequest(r));
    }

    /// <summary>Bulk reorder categories</summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<ReorderItem> items)
    {
        if (items == null || items.Count == 0)
            return BadRequest(WahSpices.Application.Common.ApiResponse<object>.Fail("REQUIRED", "Nothing to reorder."));
        var r = await _svc.ReorderAsync(items);
        return r.Success ? Ok(r) : BadRequest(r);
    }
}
