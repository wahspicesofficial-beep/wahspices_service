using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Category;

namespace WahSpices.Application.Interfaces.Services;

public interface ICategoryService
{
    Task<ApiResponse<List<CategoryDto>>> GetAllAsync(bool includeInactive = false);
    Task<ApiResponse<CategoryDto>> CreateAsync(CreateCategoryRequest request);
    Task<ApiResponse<CategoryDto>> UpdateAsync(int id, UpdateCategoryRequest request);
    Task<ApiResponse<bool>> DeleteAsync(int id);
    Task<ApiResponse<bool>> ReorderAsync(List<ReorderItem> items);
}
