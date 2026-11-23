using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

public class CartItem : BaseEntity
{
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string? CouponCode { get; set; }

    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public class WishlistItem : BaseEntity
{
    public int UserId { get; set; }
    public int ProductId { get; set; }

    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public class Coupon : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = "percentage"; // percentage | flat
    public decimal DiscountValue { get; set; }
    public decimal? MinCartValue { get; set; }
    public decimal? MaxDiscount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;
}
