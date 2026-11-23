using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Product;

namespace WahSpices.Application.Interfaces.Services;

public interface IProductService
{
    Task<ApiResponse<PagedResult<ProductDto>>> GetAllAsync(ProductListRequest request);
    Task<ApiResponse<List<ProductDto>>> GetFeaturedAsync(int limit = 8);
    Task<ApiResponse<List<string>>> GetCategoriesAsync();
    Task<ApiResponse<ProductDetailDto>> GetBySlugAsync(string slug);
    Task<ApiResponse<List<ProductDto>>> GetRelatedAsync(int productId, string category, int limit = 4);
    Task<ApiResponse<PagedResult<ProductDto>>> SearchAsync(string query, int page = 1, int limit = 20);
    Task<ApiResponse<List<ProductDto>>> GetByStateAsync(string state);
    /// <summary>
    /// Products for a region page: the explicit slugs first (in the given order), then — when stateKeys is not
    /// empty — other live products whose State matches one of the keys (case-insensitive). Drafts/deleted excluded.
    /// </summary>
    Task<List<ProductDto>> GetForRegionAsync(IReadOnlyList<string> slugs, IReadOnlyList<string> stateKeys, int limit = 24);
    Task<ApiResponse<PagedResult<ReviewDto>>> GetReviewsAsync(int productId, int? rating, string? sort, int page, int limit);
    Task<ApiResponse<ReviewDto>> SubmitReviewAsync(int productId, int userId, SubmitReviewRequest request);
    /// <summary>The signed-in customer's own review of a product (any status), or null.</summary>
    Task<ApiResponse<ReviewDto?>> GetMyReviewAsync(int productId, int userId);
}

public interface IAdminProductService
{
    Task<ApiResponse<PagedResult<ProductDto>>> GetAllAsync(string? category, string? search, int page, int limit);
    Task<ApiResponse<ProductDetailDto>> GetByIdAsync(int id);
    Task<ApiResponse<ProductDto>> CreateAsync(CreateProductRequest request, string? imageUrl = null);
    Task<ApiResponse<ProductDto>> UpdateAsync(int id, CreateProductRequest request);
    Task<ApiResponse<bool>> DeleteAsync(int id);
    Task<ApiResponse<bool>> ToggleStockAsync(int id, bool inStock);
    Task<ApiResponse<ProductDto>> SetImageAsync(int id, string? imageUrl);
}
