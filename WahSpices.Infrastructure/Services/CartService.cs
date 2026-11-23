using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Order;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public CartService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // Delivery pricing comes from Admin → Settings (SiteSettings), same as order placement
    private SiteSettings? _settings;
    private SiteSettings Settings => _settings ??= StoreRules.GetSettingsAsync(_db).GetAwaiter().GetResult();
    private decimal FreeDeliveryAt => Settings.FreeDeliveryAbove;
    private decimal Delivery(decimal orderValue) => StoreRules.DeliveryFor(Settings, orderValue);

    // ── Get cart ──────────────────────────────────────────────────────────
    public async Task<ApiResponse<CartDto>> GetCartAsync(int userId)
    {
        var items = await _db.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId && !c.IsDeleted && !c.Product.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var couponCode = items.FirstOrDefault()?.CouponCode;
        var coupon = couponCode != null ? await GetActiveCoupon(couponCode) : null;

        var cartItems = items.Select(c => new CartItemDto(
            c.Id, c.ProductId, c.Product.Name, c.Product.Slug,
            c.Product.Price, c.Product.Mrp, c.Product.Weight,
            c.Quantity, c.Product.Price * c.Quantity,
            c.Product.ImageUrl, c.Product.InStock
        )).ToList();

        var summary = CalculateSummary(cartItems, coupon, couponCode);

        return ApiResponse<CartDto>.Ok(new CartDto($"cart-{userId}", cartItems, summary));
    }

    // ── Add item ──────────────────────────────────────────────────────────
    public async Task<ApiResponse<CartSummaryDto>> AddItemAsync(int userId, int productId, int quantity)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);
        if (product == null) return ApiResponse<CartSummaryDto>.Fail("NOT_FOUND", "Product not found");
        if (!product.InStock) return ApiResponse<CartSummaryDto>.Fail("OUT_OF_STOCK", "Product is out of stock");

        var existing = await _db.CartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && !c.IsDeleted);

        if (existing != null)
        {
            existing.Quantity += quantity;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.CartItems.Add(new CartItem
            {
                UserId = userId, ProductId = productId,
                Quantity = quantity, CreatedBy = userId.ToString()
            });
        }

        await _db.SaveChangesAsync();
        return await GetSummary(userId);
    }

    // ── Update item ───────────────────────────────────────────────────────
    public async Task<ApiResponse<CartSummaryDto>> UpdateItemAsync(int userId, int productId, int quantity)
    {
        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && !c.IsDeleted);

        if (item == null) return ApiResponse<CartSummaryDto>.Fail("NOT_FOUND", "Item not in cart");

        if (quantity <= 0)
        {
            item.IsDeleted = true;
        }
        else
        {
            item.Quantity = quantity;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return await GetSummary(userId);
    }

    // ── Remove item ───────────────────────────────────────────────────────
    public async Task<ApiResponse<CartSummaryDto>> RemoveItemAsync(int userId, int productId)
    {
        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && !c.IsDeleted);

        if (item != null)
        {
            item.IsDeleted = true;
            await _db.SaveChangesAsync();
        }

        return await GetSummary(userId);
    }

    // ── Clear cart ────────────────────────────────────────────────────────
    public async Task<ApiResponse<CartSummaryDto>> ClearCartAsync(int userId)
    {
        var items = await _db.CartItems
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .ToListAsync();

        items.ForEach(i => i.IsDeleted = true);
        await _db.SaveChangesAsync();

        return ApiResponse<CartSummaryDto>.Ok(EmptySummary());
    }

    // ── Apply coupon ──────────────────────────────────────────────────────
    public async Task<ApiResponse<CouponResult>> ApplyCouponAsync(int userId, string couponCode)
    {
        var coupon = await GetActiveCoupon(couponCode);
        if (coupon == null)
            return ApiResponse<CouponResult>.Fail("INVALID_COUPON", "Coupon is invalid or expired");

        var items = await _db.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId && !c.IsDeleted && !c.Product.IsDeleted)
            .ToListAsync();

        if (!items.Any())
            return ApiResponse<CouponResult>.Fail("EMPTY_CART", "Your cart is empty");

        var subtotal = items.Sum(c => c.Product.Price * c.Quantity);

        if (coupon.MinCartValue.HasValue && subtotal < coupon.MinCartValue.Value)
            return ApiResponse<CouponResult>.Fail("MIN_CART",
                $"Minimum cart value of ₹{coupon.MinCartValue:F0} required for this coupon");

        var discount = coupon.DiscountType == "percentage"
            ? subtotal * coupon.DiscountValue / 100
            : coupon.DiscountValue;

        if (coupon.MaxDiscount.HasValue) discount = Math.Min(discount, coupon.MaxDiscount.Value);

        var delivery = Delivery(subtotal - discount);
        var newTotal = subtotal - discount + delivery;

        // Store coupon on all cart items
        items.ForEach(i => { i.CouponCode = couponCode.ToUpper(); i.UpdatedAt = DateTime.UtcNow; });
        await _db.SaveChangesAsync();

        return ApiResponse<CouponResult>.Ok(new CouponResult(
            couponCode.ToUpper(), coupon.DiscountType, coupon.DiscountValue, discount, newTotal),
            "Coupon applied successfully");
    }

    // ── Remove coupon ─────────────────────────────────────────────────────
    public async Task<ApiResponse<CartSummaryDto>> RemoveCouponAsync(int userId)
    {
        var items = await _db.CartItems
            .Where(c => c.UserId == userId && !c.IsDeleted && c.CouponCode != null)
            .ToListAsync();

        items.ForEach(i => { i.CouponCode = null; i.UpdatedAt = DateTime.UtcNow; });
        await _db.SaveChangesAsync();

        return await GetSummary(userId);
    }

    // ── Sync cart ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<CartSummaryDto>> SyncCartAsync(int userId, List<CartItemRequest> guestItems)
    {
        foreach (var g in guestItems)
        {
            var existing = await _db.CartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == g.ProductId && !c.IsDeleted);

            if (existing != null)
                existing.Quantity = Math.Max(existing.Quantity, g.Quantity);
            else
                _db.CartItems.Add(new CartItem
                {
                    UserId = userId, ProductId = g.ProductId,
                    Quantity = g.Quantity, CreatedBy = userId.ToString()
                });
        }

        await _db.SaveChangesAsync();
        return await GetSummary(userId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private async Task<ApiResponse<CartSummaryDto>> GetSummary(int userId)
    {
        var items = await _db.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId && !c.IsDeleted && !c.Product.IsDeleted)
            .ToListAsync();

        var couponCode = items.FirstOrDefault()?.CouponCode;
        var coupon = couponCode != null ? await GetActiveCoupon(couponCode) : null;

        var cartItems = items.Select(c => new CartItemDto(
            c.Id, c.ProductId, c.Product.Name, c.Product.Slug,
            c.Product.Price, c.Product.Mrp, c.Product.Weight,
            c.Quantity, c.Product.Price * c.Quantity,
            c.Product.ImageUrl, c.Product.InStock
        )).ToList();

        return ApiResponse<CartSummaryDto>.Ok(CalculateSummary(cartItems, coupon, couponCode));
    }

    private CartSummaryDto CalculateSummary(List<CartItemDto> items, Coupon? coupon, string? couponCode)
    {
        if (!items.Any()) return EmptySummary();

        var subtotalMrp = items.Sum(i => i.Mrp * i.Quantity);
        var subtotal    = items.Sum(i => i.Price * i.Quantity);
        var discount    = subtotalMrp - subtotal;

        decimal couponDiscount = 0;
        if (coupon != null)
        {
            couponDiscount = coupon.DiscountType == "percentage"
                ? subtotal * coupon.DiscountValue / 100
                : coupon.DiscountValue;
            if (coupon.MaxDiscount.HasValue)
                couponDiscount = Math.Min(couponDiscount, coupon.MaxDiscount.Value);
        }

        var taxableTotal = subtotal - couponDiscount;
        var delivery = Delivery(taxableTotal);
        var grandTotal = taxableTotal + delivery;

        return new CartSummaryDto(
            items.Count, subtotalMrp, subtotal, discount, couponDiscount,
            couponCode, delivery, FreeDeliveryAt, grandTotal);
    }

    private CartSummaryDto EmptySummary() =>
        new(0, 0, 0, 0, 0, null, 0, FreeDeliveryAt, 0);

    private async Task<Coupon?> GetActiveCoupon(string code) =>
        await _db.Coupons.FirstOrDefaultAsync(c =>
            c.Code == code.ToUpper() && c.IsActive && !c.IsDeleted &&
            (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow) &&
            (c.MaxUses == null || c.UsedCount < c.MaxUses));
}
