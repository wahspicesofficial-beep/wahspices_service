using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Product;

public record ProductListRequest(
    string? Category,
    string? Search,
    string? State,
    string? Sort,
    bool? Featured,
    bool? InStock,
    int Page = 1,
    int Limit = 20,
    string? SubCategory = null
);

public record ProductVariantDto(
    int Id,
    string Weight,
    int WeightGrams,
    decimal Price,
    decimal Mrp,
    decimal Discount,
    string? Sku,
    bool InStock,
    bool IsDefault
);

public record ProductVariantRequest(
    int? Id,
    string Weight,
    int WeightGrams,
    decimal Price,
    decimal Mrp,
    string? Sku = null,
    bool InStock = true,
    bool IsDefault = false
);

public record ProductDto(
    int Id,
    string Name,
    string Slug,
    string? Description,
    string Category,
    string? State,
    string? StateLabel,
    string? StateColor,
    decimal Price,
    decimal Mrp,
    decimal Discount,
    string Weight,
    string PackType,
    string? Badge,
    string? BgColor,
    string? ImageUrl,
    decimal Rating,
    int ReviewCount,
    bool InStock,
    bool Featured,
    string? HsnCode,
    decimal GstRate,
    string? SubCategory,
    List<ProductVariantDto> Variants
);

public record ProductDetailDto(
    int Id,
    string Name,
    string Slug,
    string? Description,
    string? Ingredients,
    string[] Usages,
    string Category,
    string? State,
    string? StateLabel,
    string? StateColor,
    decimal Price,
    decimal Mrp,
    string Weight,
    string PackType,
    string? Badge,
    string? BgColor,
    string? ImageUrl,
    decimal Rating,
    int ReviewCount,
    RatingBreakdown RatingBreakdown,
    NutritionDto? Nutrition,
    bool InStock,
    bool Featured,
    string? SeoTitle,
    string? SeoDescription,
    string? SubCategory,
    List<ProductVariantDto> Variants,
    string? HsnCode,
    decimal GstRate,
    bool IsDraft
);

public record RatingBreakdown(int Star5, int Star4, int Star3, int Star2, int Star1);

public record NutritionDto(
    decimal? Calories,
    string? Protein,
    string? Fat,
    string? Carbs,
    string? Fiber
);

public record ReviewDto(
    int Id,
    int UserId,
    string UserName,
    int Rating,
    string? Title,
    string? Comment,
    bool IsVerifiedPurchase,
    int HelpfulCount,
    DateTime CreatedAt,
    string Status = "Approved",          // Pending | Approved | Rejected (only the author sees non-approved)
    string? AdminReply = null,           // public reply from the WAH! team
    DateTime? RepliedAt = null,
    string? RejectionReason = null       // shown to the author only
);

public record SubmitReviewRequest(
    int Rating,
    string? Title,
    string? Comment
);

public record CreateProductRequest(
    string Name,
    string? Slug,
    string? Description,
    string? Ingredients,
    string Category,
    string? State,
    string? StateLabel,
    string? StateColor,
    decimal Price,
    decimal Mrp,
    string Weight,
    string PackType,
    string? Badge,
    string? BgColor,
    string? HsnCode,
    decimal GstRate = 18,
    bool Featured = false,
    bool IsDraft = false,
    string? SubCategory = null,
    string[]? Usages = null,
    string? ImageUrl = null,
    string? SeoTitle = null,
    string? SeoDescription = null,
    List<ProductVariantRequest>? Variants = null
);
