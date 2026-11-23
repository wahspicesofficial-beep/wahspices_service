using System.ComponentModel.DataAnnotations.Schema;
using WahSpices.Domain.Common;
using WahSpices.Domain.Enums;

namespace WahSpices.Domain.Entities;

public class PackTypeConfig : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Ingredients { get; set; }
    // CategorySlug is the source of truth — links to Categories.Slug
    public string CategorySlug { get; set; } = "blend-masala";
    // Optional sub-category — links to a Categories.Slug whose ParentId is the main category
    public string? SubCategorySlug { get; set; }
    [NotMapped] public ProductCategory Category { get; set; }
    public string? State { get; set; }
    public string? StateLabel { get; set; }
    public string? StateColor { get; set; }
    public decimal Price { get; set; }
    public decimal Mrp { get; set; }
    public string Weight { get; set; } = string.Empty;
    public string PackType { get; set; } = string.Empty;
    public string? Badge { get; set; }
    public string? BgColor { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Rating { get; set; } = 0;
    public int ReviewCount { get; set; } = 0;
    public bool InStock { get; set; } = true;
    public bool Featured { get; set; } = false;
    public bool IsDraft { get; set; } = false;
    public string? HsnCode { get; set; }
    public decimal GstRate { get; set; } = 18;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? Usages { get; set; }  // JSON array stored as string

    // Nutrition per 100g
    public decimal? CaloriesPer100g { get; set; }
    public string? ProteinPer100g { get; set; }
    public string? FatPer100g { get; set; }
    public string? CarbsPer100g { get; set; }
    public string? FiberPer100g { get; set; }

    // Navigation
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();
    /// <summary>Finished-goods stock rows: at most one product-level row (VariantId null) + one per variant.</summary>
    public ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}

/// <summary>
/// A sellable pack size of a product (e.g. 50g / 100g / 1kg), each with its own price.
/// Product.Price / Mrp / Weight mirror the default (smallest) variant for listing & sorting.
/// </summary>
public class ProductVariant : BaseEntity
{
    public int ProductId { get; set; }
    public string Weight { get; set; } = string.Empty;   // display label: "100g", "1kg"
    public int WeightGrams { get; set; }                 // for ordering & per-gram maths
    public decimal Price { get; set; }
    public decimal Mrp { get; set; }
    public string? Sku { get; set; }
    public bool InStock { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;

    public Product Product { get; set; } = null!;
}
