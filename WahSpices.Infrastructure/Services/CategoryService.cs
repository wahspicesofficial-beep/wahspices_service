using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Category;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;
    public CategoryService(AppDbContext db) => _db = db;

    public async Task<ApiResponse<List<CategoryDto>>> GetAllAsync(bool includeInactive = false)
    {
        var q = _db.Categories.Where(c => !c.IsDeleted);
        if (!includeInactive) q = q.Where(c => c.IsActive);

        var cats = await q.OrderBy(c => c.DisplayOrder)
            .Select(c => Map(c))
            .ToListAsync();

        return ApiResponse<List<CategoryDto>>.Ok(cats);
    }

    public async Task<ApiResponse<CategoryDto>> CreateAsync(CreateCategoryRequest request)
    {
        if (await _db.Categories.AnyAsync(c => c.Slug == request.Slug && !c.IsDeleted))
            return ApiResponse<CategoryDto>.Fail("SLUG_TAKEN", "A category with this slug already exists", "slug");

        var parentError = await ValidateParentAsync(request.ParentId, null);
        if (parentError != null) return ApiResponse<CategoryDto>.Fail("INVALID_PARENT", parentError, "parentId");

        var cat = new Category
        {
            Name = request.Name,
            Slug = request.Slug.ToLower().Trim(),
            Icon = request.Icon,
            Description = request.Description,
            DisplayOrder = request.DisplayOrder,
            ParentId = request.ParentId,
            IsActive = true,
            CreatedBy = "admin"
        };

        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return ApiResponse<CategoryDto>.Ok(Map(cat), "Category created");
    }

    public async Task<ApiResponse<CategoryDto>> UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (cat == null) return ApiResponse<CategoryDto>.Fail("NOT_FOUND", "Category not found");

        var parentError = await ValidateParentAsync(request.ParentId, id);
        if (parentError != null) return ApiResponse<CategoryDto>.Fail("INVALID_PARENT", parentError, "parentId");

        cat.Name = request.Name;
        cat.ParentId = request.ParentId;
        cat.Icon = request.Icon;
        cat.Description = request.Description;
        cat.DisplayOrder = request.DisplayOrder;
        cat.IsActive = request.IsActive;
        cat.UpdatedAt = DateTime.UtcNow;
        cat.UpdatedBy = "admin";

        await _db.SaveChangesAsync();
        return ApiResponse<CategoryDto>.Ok(Map(cat), "Category updated");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(int id)
    {
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (cat == null) return ApiResponse<bool>.Fail("NOT_FOUND", "Category not found");

        if (await _db.Categories.AnyAsync(c => c.ParentId == cat.Id && !c.IsDeleted))
            return ApiResponse<bool>.Fail("HAS_CHILDREN", "Delete or move this category's sub-categories first.");

        var hasProducts = await _db.Products.AnyAsync(p => (p.CategorySlug == cat.Slug || p.SubCategorySlug == cat.Slug) && !p.IsDeleted);
        if (hasProducts)
            return ApiResponse<bool>.Fail("HAS_PRODUCTS", "Cannot delete a category that has products. Remove or reassign products first.");

        cat.IsDeleted = true;
        cat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ApiResponse<bool>.Ok(true, "Category deleted");
    }

    public async Task<ApiResponse<bool>> ReorderAsync(List<ReorderItem> items)
    {
        foreach (var item in items)
        {
            var cat = await _db.Categories.FindAsync(item.Id);
            if (cat != null && !cat.IsDeleted)
                cat.DisplayOrder = item.DisplayOrder;
        }
        await _db.SaveChangesAsync();
        return ApiResponse<bool>.Ok(true, "Categories reordered");
    }

    /// Only one level of nesting: a parent must exist and itself be a main category.
    private async Task<string?> ValidateParentAsync(int? parentId, int? selfId)
    {
        if (parentId == null) return null;
        if (parentId == selfId) return "A category cannot be its own parent.";
        var parent = await _db.Categories.FirstOrDefaultAsync(c => c.Id == parentId && !c.IsDeleted);
        if (parent == null) return "Parent category not found.";
        if (parent.ParentId != null) return "Sub-categories can only be one level deep.";
        if (selfId != null && await _db.Categories.AnyAsync(c => c.ParentId == selfId && !c.IsDeleted))
            return "This category has its own sub-categories, so it cannot become a sub-category.";
        return null;
    }

    private static CategoryDto Map(Category c) =>
        new(c.Id, c.Name, c.Slug, c.Icon, c.Description, c.DisplayOrder, c.IsActive, c.ParentId);
}
