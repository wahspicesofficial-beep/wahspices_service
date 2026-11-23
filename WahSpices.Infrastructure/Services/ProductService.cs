using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Product;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

public class ProductService : IProductService, IAdminProductService
{
    private readonly AppDbContext _db;

    public ProductService(AppDbContext db) => _db = db;

    // ── Customer Facing ───────────────────────────────────────────────────
    public async Task<ApiResponse<PagedResult<ProductDto>>> GetAllAsync(ProductListRequest request)
    {
        var query = _db.Products.Where(p => !p.IsDeleted && !p.IsDraft).AsQueryable();

        // A category filter matches either the main category or the sub-category slug
        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(p => p.CategorySlug == request.Category || p.SubCategorySlug == request.Category);

        if (!string.IsNullOrWhiteSpace(request.SubCategory))
            query = query.Where(p => p.SubCategorySlug == request.SubCategory);

        if (!string.IsNullOrWhiteSpace(request.State))
            query = query.Where(p => p.State == request.State);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) ||
                (p.Description != null && p.Description.ToLower().Contains(s)));
        }

        if (request.Featured.HasValue) query = query.Where(p => p.Featured == request.Featured.Value);
        if (request.InStock.HasValue)  query = query.Where(p => p.InStock == request.InStock.Value);

        // Every ordering ends with Id so page boundaries are stable (infinite scroll never repeats or skips a product)
        var nameHit = request.Search?.Trim().ToLower() ?? "";
        query = request.Sort switch
        {
            "price_asc"  => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            "rating"     => query.OrderByDescending(p => p.Rating).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "newest"     => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            "name"       => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            // default: when searching, products whose NAME matches come before description-only matches
            _ when nameHit.Length > 0 => query.OrderByDescending(p => p.Name.ToLower().Contains(nameHit))
                                              .ThenByDescending(p => p.Featured).ThenByDescending(p => p.Rating).ThenBy(p => p.Name).ThenBy(p => p.Id),
            _            => query.OrderByDescending(p => p.Featured).ThenByDescending(p => p.Rating).ThenBy(p => p.Name).ThenBy(p => p.Id)
        };
        var page = Math.Max(1, request.Page);
        var limit = Math.Clamp(request.Limit, 1, 200);

        var total = await query.CountAsync();
        var items = (await query
            .Include(p => p.Variants)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync())
            .Select(MapDto).ToList();

        return ApiResponse<PagedResult<ProductDto>>.Ok(new PagedResult<ProductDto>
        {
            Items = items, Total = total, Page = page, Limit = limit
        });
    }

    public async Task<ApiResponse<List<ProductDto>>> GetFeaturedAsync(int limit = 8)
    {
        var items = await _db.Products
            .Where(p => p.Featured && p.InStock && !p.IsDeleted && !p.IsDraft)
            .OrderByDescending(p => p.Rating)
            .Take(limit)
            .Include(p => p.Variants)
            .ToListAsync();

        return ApiResponse<List<ProductDto>>.Ok(items.Select(MapDto).ToList());
    }

    public async Task<ApiResponse<List<string>>> GetCategoriesAsync()
    {
        var cats = await _db.Products
            .Where(p => !p.IsDeleted && !p.IsDraft)
            .Select(p => p.CategorySlug)
            .Distinct()
            .ToListAsync();

        return ApiResponse<List<string>>.Ok(cats);
    }

    public async Task<ApiResponse<ProductDetailDto>> GetBySlugAsync(string slug)
    {
        var p = await _db.Products
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Slug == slug && !x.IsDeleted && !x.IsDraft);

        if (p == null) return ApiResponse<ProductDetailDto>.Fail("NOT_FOUND", "Product not found");
        return ApiResponse<ProductDetailDto>.Ok(await BuildDetailAsync(p));
    }

    private async Task<ProductDetailDto> BuildDetailAsync(Product p)
    {

        // Only approved reviews are public and count toward the rating breakdown
        var reviews = await _db.Reviews
            .Where(r => r.ProductId == p.Id).Approved()
            .GroupBy(r => r.Rating)
            .Select(g => new { Star = g.Key, Count = g.Count() })
            .ToListAsync();

        var breakdown = new RatingBreakdown(
            reviews.FirstOrDefault(r => r.Star == 5)?.Count ?? 0,
            reviews.FirstOrDefault(r => r.Star == 4)?.Count ?? 0,
            reviews.FirstOrDefault(r => r.Star == 3)?.Count ?? 0,
            reviews.FirstOrDefault(r => r.Star == 2)?.Count ?? 0,
            reviews.FirstOrDefault(r => r.Star == 1)?.Count ?? 0
        );

        string[]? usages = null;
        if (!string.IsNullOrEmpty(p.Usages))
        {
            try { usages = JsonSerializer.Deserialize<string[]>(p.Usages); } catch { /* ignore */ }
        }

        var detail = new ProductDetailDto(
            p.Id, p.Name, p.Slug, p.Description, p.Ingredients,
            usages ?? Array.Empty<string>(),
            p.CategorySlug, p.State, p.StateLabel, p.StateColor,
            p.Price, p.Mrp, p.Weight, p.PackType,
            p.Badge, p.BgColor, p.ImageUrl,
            p.Rating, p.ReviewCount, breakdown,
            p.CaloriesPer100g.HasValue ? new NutritionDto(p.CaloriesPer100g, p.ProteinPer100g, p.FatPer100g, p.CarbsPer100g, p.FiberPer100g) : null,
            p.InStock, p.Featured, p.SeoTitle, p.SeoDescription,
            p.SubCategorySlug, MapVariants(p), p.HsnCode, p.GstRate, p.IsDraft
        );

        return detail;
    }

    public async Task<ApiResponse<List<ProductDto>>> GetRelatedAsync(int productId, string category, int limit = 4)
    {
        var items = await _db.Products
            .Where(p => p.Id != productId && !p.IsDeleted && !p.IsDraft && p.InStock &&
                p.CategorySlug == category)
            .OrderByDescending(p => p.Rating)
            .Take(limit)
            .Include(p => p.Variants)
            .ToListAsync();

        return ApiResponse<List<ProductDto>>.Ok(items.Select(MapDto).ToList());
    }

    public async Task<ApiResponse<PagedResult<ProductDto>>> SearchAsync(string query, int page = 1, int limit = 20)
    {
        var s = query.ToLower();
        var q = _db.Products
            .Where(p => !p.IsDeleted && !p.IsDraft &&
                (p.Name.ToLower().Contains(s) ||
                 (p.Description != null && p.Description.ToLower().Contains(s)) ||
                 p.CategorySlug.ToLower().Contains(s)));

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(p => p.Featured)
            .ThenByDescending(p => p.Rating)
            .Skip((page - 1) * limit).Take(limit)
            .Include(p => p.Variants)
            .ToListAsync();

        return ApiResponse<PagedResult<ProductDto>>.Ok(new PagedResult<ProductDto>
        {
            Items = items.Select(MapDto).ToList(), Total = total, Page = page, Limit = limit
        });
    }

    public async Task<ApiResponse<List<ProductDto>>> GetByStateAsync(string state)
    {
        var items = await _db.Products
            .Where(p => p.State == state && !p.IsDeleted && !p.IsDraft && p.InStock)
            .Include(p => p.Variants)
            .ToListAsync();

        return ApiResponse<List<ProductDto>>.Ok(items.Select(MapDto).ToList());
    }

    public async Task<List<ProductDto>> GetForRegionAsync(IReadOnlyList<string> slugs, IReadOnlyList<string> stateKeys, int limit = 24)
    {
        var slugList = slugs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).Distinct().Take(100).ToList();
        var keys = stateKeys.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        if (slugList.Count == 0 && keys.Count == 0) return new List<ProductDto>();

        var items = await _db.Products
            .Where(p => !p.IsDeleted && !p.IsDraft &&
                (slugList.Contains(p.Slug) || (p.State != null && keys.Contains(p.State.ToLower()))))
            .Include(p => p.Variants)
            .AsNoTracking()
            .ToListAsync();

        // Explicit picks keep the admin's order; auto-linked ones follow (featured, then best rated)
        var ordered = items
            .OrderBy(p => { var i = slugList.IndexOf(p.Slug.ToLowerInvariant()); return i < 0 ? int.MaxValue : i; })
            .ThenByDescending(p => p.Featured)
            .ThenByDescending(p => p.Rating)
            .ThenBy(p => p.Name)
            .Take(Math.Clamp(limit, 1, 100));
        return ordered.Select(MapDto).ToList();
    }

    public async Task<ApiResponse<PagedResult<ReviewDto>>> GetReviewsAsync(int productId, int? rating, string? sort, int page, int limit)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 50);
        var q = _db.Reviews
            .Include(r => r.User)
            .Where(r => r.ProductId == productId).Approved();

        if (rating is >= 1 and <= 5) q = q.Where(r => r.Rating == rating.Value);

        q = sort switch
        {
            "oldest"  => q.OrderBy(r => r.CreatedAt),
            "helpful" => q.OrderByDescending(r => r.HelpfulCount).ThenByDescending(r => r.CreatedAt),
            "highest" => q.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "lowest"  => q.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _         => q.OrderByDescending(r => r.CreatedAt)
        };

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * limit).Take(limit)
            .Select(r => new ReviewDto(r.Id, r.UserId, r.User.Name, r.Rating, r.Title, r.Comment,
                r.IsVerifiedPurchase, r.HelpfulCount, r.CreatedAt, r.Status, r.AdminReply,
                r.AdminReply != null ? (r.UpdatedAt ?? r.ModeratedAt) : null, null))
            .ToListAsync();

        return ApiResponse<PagedResult<ReviewDto>>.Ok(new PagedResult<ReviewDto>
        {
            Items = items, Total = total, Page = page, Limit = limit
        });
    }

    public async Task<ApiResponse<ReviewDto?>> GetMyReviewAsync(int productId, int userId)
    {
        var r = await _db.Reviews.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.UserId == userId && !x.IsDeleted);
        return ApiResponse<ReviewDto?>.Ok(r == null ? null : ToDto(r, includeRejection: true));
    }

    private static ReviewDto ToDto(Review r, bool includeRejection) =>
        new(r.Id, r.UserId, r.User?.Name ?? "", r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase,
            r.HelpfulCount, r.CreatedAt, r.Status, r.AdminReply,
            r.AdminReply != null ? (r.UpdatedAt ?? r.ModeratedAt) : null,
            includeRejection ? r.RejectionReason : null);

    public async Task<ApiResponse<ReviewDto>> SubmitReviewAsync(int productId, int userId, SubmitReviewRequest request)
    {
        // ── Validation ──
        if (request.Rating is < 1 or > 5)
            return ApiResponse<ReviewDto>.Fail("VALIDATION_ERROR", "Please choose a rating between 1 and 5 stars.", "rating");
        var title   = request.Title?.Trim();
        var comment = request.Comment?.Trim();
        if (string.IsNullOrWhiteSpace(comment) || comment.Length < 10)
            return ApiResponse<ReviewDto>.Fail("VALIDATION_ERROR", "Please write at least 10 characters about the product.", "comment");
        if (comment.Length > 2000)
            return ApiResponse<ReviewDto>.Fail("VALIDATION_ERROR", "Review is too long (max 2000 characters).", "comment");
        if (title?.Length > 150)
            return ApiResponse<ReviewDto>.Fail("VALIDATION_ERROR", "Title is too long (max 150 characters).", "title");
        if (string.IsNullOrEmpty(title)) title = null;

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted && !p.IsDraft);
        if (product == null) return ApiResponse<ReviewDto>.Fail("NOT_FOUND", "Product not found");

        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.IsDeleted) return ApiResponse<ReviewDto>.Fail("UNAUTHORIZED", "Please sign in again to post a review.");

        var verified = await ProductReviewStats.HasPurchasedAsync(_db, userId, productId);
        var settings = await ProductReviewStats.GetSettingsAsync(_db);
        var autoApprove = verified && settings.AutoApproveVerified;

        // One review per customer per product (unique index). A rejected review may be rewritten and resubmitted.
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);
        if (review != null && !review.IsDeleted && review.Status != "Rejected")
            return ApiResponse<ReviewDto>.Fail("ALREADY_REVIEWED",
                review.Status == "Pending" ? "Your review of this product is awaiting approval." : "You have already reviewed this product.");

        if (review == null)
        {
            review = new Review { ProductId = productId, UserId = userId, CreatedBy = userId.ToString() };
            _db.Reviews.Add(review);
        }
        else
        {
            review.IsDeleted = false;
            review.CreatedAt = DateTime.UtcNow;
            review.UpdatedAt = DateTime.UtcNow;
            review.AdminReply = null;
            review.HelpfulCount = 0;
        }
        review.Rating = request.Rating;
        review.Title = title;
        review.Comment = comment;
        review.IsVerifiedPurchase = verified;
        review.Status = autoApprove ? "Approved" : "Pending";
        review.IsApproved = autoApprove;
        review.RejectionReason = null;
        review.ModeratedAt = autoApprove ? DateTime.UtcNow : null;
        review.ModeratedBy = autoApprove ? "auto (verified purchase)" : null;

        await _db.SaveChangesAsync();
        // Rating/ReviewCount only ever reflect approved reviews
        if (autoApprove) await ProductReviewStats.RecomputeAsync(_db, productId);

        review.User = user;
        return ApiResponse<ReviewDto>.Ok(ToDto(review, includeRejection: false),
            autoApprove ? "Thanks! Your review is now live." : "Thanks! Your review is awaiting approval.");
    }

    // ── Admin Facing ──────────────────────────────────────────────────────
    async Task<ApiResponse<PagedResult<ProductDto>>> IAdminProductService.GetAllAsync(
        string? category, string? search, int page, int limit)
    {
        var q = _db.Products.Where(p => !p.IsDeleted).AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(p => p.CategorySlug == category || p.SubCategorySlug == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(s) || p.Slug.ToLower().Contains(s));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name)
            .Skip((page - 1) * limit).Take(limit)
            .Include(p => p.Variants)
            .ToListAsync();

        return ApiResponse<PagedResult<ProductDto>>.Ok(new PagedResult<ProductDto>
        {
            Items = items.Select(MapDto).ToList(), Total = total, Page = page, Limit = limit
        });
    }

    async Task<ApiResponse<ProductDetailDto>> IAdminProductService.GetByIdAsync(int id)
    {
        // Admin can open drafts too, so don't go through the customer-facing slug lookup
        var p = await _db.Products.Include(x => x.Variants).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return ApiResponse<ProductDetailDto>.Fail("NOT_FOUND", "Product not found");
        return ApiResponse<ProductDetailDto>.Ok(await BuildDetailAsync(p));
    }

    public async Task<ApiResponse<ProductDto>> CreateAsync(CreateProductRequest request, string? imageUrl = null)
    {
        // Auto-generate slug from name if not provided
        var baseSlug = string.IsNullOrWhiteSpace(request.Slug)
            ? System.Text.RegularExpressions.Regex.Replace(request.Name.ToLower().Trim(), @"[^a-z0-9]+", "-").Trim('-')
            : request.Slug.ToLower().Trim();

        // Ensure uniqueness by appending a counter if needed
        var slug = baseSlug;
        var counter = 1;
        while (await _db.Products.AnyAsync(p => p.Slug == slug && !p.IsDeleted))
            slug = $"{baseSlug}-{counter++}";

        var p = new Product
        {
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            Ingredients = request.Ingredients,
            CategorySlug = string.IsNullOrWhiteSpace(request.Category) ? "blend-masala" : request.Category.ToLower(),
            State = request.State,
            StateLabel = request.StateLabel,
            StateColor = request.StateColor,
            Price = request.Price,
            Mrp = request.Mrp,
            Weight = request.Weight,
            PackType = request.PackType ?? "PouchPack",
            Badge = request.Badge,
            BgColor = request.BgColor,
            HsnCode = request.HsnCode,
            GstRate = request.GstRate,
            Featured = request.Featured,
            IsDraft = request.IsDraft,
            ImageUrl = imageUrl ?? request.ImageUrl,
            SubCategorySlug = string.IsNullOrWhiteSpace(request.SubCategory) ? null : request.SubCategory.ToLower(),
            Usages = request.Usages is { Length: > 0 } ? JsonSerializer.Serialize(request.Usages) : null,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription,
            InStock = true,
            CreatedBy = "admin"
        };

        if (request.Variants is { Count: > 0 })
            ApplyVariants(p, request.Variants);

        _db.Products.Add(p);
        await _db.SaveChangesAsync();
        return ApiResponse<ProductDto>.Ok(MapDto(p), "Product created");
    }

    public async Task<ApiResponse<ProductDto>> UpdateAsync(int id, CreateProductRequest request)
    {
        var p = await _db.Products.Include(x => x.Variants).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return ApiResponse<ProductDto>.Fail("NOT_FOUND", "Product not found");

        p.Name = request.Name;
        p.Description = request.Description;
        p.Ingredients = request.Ingredients;
        if (!string.IsNullOrWhiteSpace(request.Category))
            p.CategorySlug = request.Category.ToLower();
        p.State = request.State;
        p.StateLabel = request.StateLabel;
        p.StateColor = request.StateColor;
        p.Price = request.Price;
        p.Mrp = request.Mrp;
        p.Weight = request.Weight;
        if (!string.IsNullOrWhiteSpace(request.PackType)) p.PackType = request.PackType;
        p.Badge = request.Badge;
        p.BgColor = request.BgColor;
        p.HsnCode = request.HsnCode;
        p.GstRate = request.GstRate;
        p.Featured = request.Featured;
        p.IsDraft = request.IsDraft;
        p.SubCategorySlug = string.IsNullOrWhiteSpace(request.SubCategory) ? null : request.SubCategory.ToLower();
        if (request.Usages != null)
            p.Usages = request.Usages.Length > 0 ? JsonSerializer.Serialize(request.Usages) : null;
        if (request.ImageUrl != null) p.ImageUrl = request.ImageUrl == "" ? null : request.ImageUrl;
        if (request.SeoTitle != null) p.SeoTitle = request.SeoTitle;
        if (request.SeoDescription != null) p.SeoDescription = request.SeoDescription;
        // Variants: null = leave untouched, list = replace set (matched by Id)
        if (request.Variants != null)
            ApplyVariants(p, request.Variants);
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = "admin";
        await _db.SaveChangesAsync();

        return ApiResponse<ProductDto>.Ok(MapDto(p), "Product updated");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(int id)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return ApiResponse<bool>.Fail("NOT_FOUND", "Product not found");

        p.IsDeleted = true;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ApiResponse<bool>.Ok(true, "Product deleted");
    }

    public async Task<ApiResponse<bool>> ToggleStockAsync(int id, bool inStock)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return ApiResponse<bool>.Fail("NOT_FOUND", "Product not found");

        // "Out of stock" from the admin is a manual override that inventory changes won't undo;
        // "In stock" clears the override and lets inventory decide (untracked products are sellable).
        await ProductStockSync.SetManualOutOfStockAsync(_db, id, manualOut: !inStock);
        var tracked = await _db.InventoryItems.Where(i => i.ProductId == id && !i.IsDeleted)
            .Select(i => (int?)i.CurrentStock).ToListAsync();
        int? qty = tracked.Count == 0 ? null : tracked.Sum(x => x ?? 0);
        p.InStock = ProductStockSync.Effective(!inStock, qty);
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        if (inStock && !p.InStock)
            return ApiResponse<bool>.Ok(false, "Manual out-of-stock removed, but inventory is 0 — the product stays out of stock until stock is added.");
        return ApiResponse<bool>.Ok(true, inStock ? "Product marked in stock" : "Product marked out of stock");
    }

    public async Task<ApiResponse<ProductDto>> SetImageAsync(int id, string? imageUrl)
    {
        var p = await _db.Products.Include(x => x.Variants).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return ApiResponse<ProductDto>.Fail("NOT_FOUND", "Product not found");
        p.ImageUrl = imageUrl;
        p.UpdatedAt = DateTime.UtcNow;
        p.UpdatedBy = "admin";
        await _db.SaveChangesAsync();
        return ApiResponse<ProductDto>.Ok(MapDto(p), "Image updated");
    }

    // ── Variants ──────────────────────────────────────────────────────────
    /// Replaces the product's variant set with <paramref name="requests"/> (matched by Id),
    /// then mirrors the default variant onto Product.Price/Mrp/Weight so listing & sorting work.
    private static void ApplyVariants(Product p, List<ProductVariantRequest> requests)
    {
        var keepIds = requests.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToHashSet();
        foreach (var v in p.Variants.Where(v => !v.IsDeleted && !keepIds.Contains(v.Id)))
        {
            v.IsDeleted = true;
            v.UpdatedAt = DateTime.UtcNow;
        }

        var ordered = requests.OrderBy(r => r.WeightGrams).ToList();
        var defaultReq = ordered.FirstOrDefault(r => r.IsDefault) ?? ordered.FirstOrDefault();
        for (int i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            var v = r.Id.HasValue ? p.Variants.FirstOrDefault(x => x.Id == r.Id.Value) : null;
            if (v == null)
            {
                v = new ProductVariant { CreatedBy = "admin" };
                p.Variants.Add(v);
            }
            v.Weight       = r.Weight.Trim();
            v.WeightGrams  = r.WeightGrams;
            v.Price        = r.Price;
            v.Mrp          = r.Mrp < r.Price ? r.Price : r.Mrp;
            v.Sku          = r.Sku;
            v.InStock      = r.InStock;
            v.IsDefault    = ReferenceEquals(r, defaultReq);
            v.DisplayOrder = i;
            v.IsDeleted    = false;
            v.UpdatedAt    = DateTime.UtcNow;
        }

        var def = p.Variants.Where(v => !v.IsDeleted).FirstOrDefault(v => v.IsDefault);
        if (def != null)
        {
            p.Price  = def.Price;
            p.Mrp    = def.Mrp;
            p.Weight = def.Weight;
        }
    }

    private static List<ProductVariantDto> MapVariants(Product p) =>
        p.Variants
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.WeightGrams).ThenBy(v => v.DisplayOrder)
            .Select(v => new ProductVariantDto(
                v.Id, v.Weight, v.WeightGrams, v.Price, v.Mrp,
                v.Mrp > 0 ? Math.Round((v.Mrp - v.Price) / v.Mrp * 100, 0) : 0,
                v.Sku, v.InStock, v.IsDefault))
            .ToList();

    // ── Mapping ───────────────────────────────────────────────────────────
    private static ProductDto MapDto(Product p) =>
        new(p.Id, p.Name, p.Slug, p.Description, p.CategorySlug,
            p.State, p.StateLabel, p.StateColor, p.Price, p.Mrp,
            p.Mrp > 0 ? Math.Round((p.Mrp - p.Price) / p.Mrp * 100, 0) : 0,
            p.Weight, p.PackType, p.Badge, p.BgColor, p.ImageUrl,
            p.Rating, p.ReviewCount, p.InStock, p.Featured, p.HsnCode, p.GstRate,
            p.SubCategorySlug, MapVariants(p));
}
