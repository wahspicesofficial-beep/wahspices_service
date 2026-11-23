using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>Review moderation helpers shared by the public product API and the admin Reviews module.</summary>
public static class ProductReviewStats
{
    public const string SettingsKey = "reviewSettings";

    public sealed class ReviewSettings
    {
        /// <summary>Reviews from customers who bought the product are published without moderation.</summary>
        public bool AutoApproveVerified { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>Only approved, non-deleted reviews are public and count toward ratings.</summary>
    public static IQueryable<Review> Approved(this IQueryable<Review> q) =>
        q.Where(r => !r.IsDeleted && r.Status == "Approved");

    /// <summary>
    /// Recompute Product.Rating / ReviewCount from the approved reviews in the database and save.
    /// Call after the review change itself has been saved.
    /// </summary>
    public static async Task RecomputeAsync(AppDbContext db, IEnumerable<int> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var stats = await db.Reviews.Where(r => ids.Contains(r.ProductId)).Approved()
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count(), Avg = g.Average(x => (double)x.Rating) })
            .ToDictionaryAsync(x => x.ProductId, ct);
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var p in products)
        {
            var s = stats.GetValueOrDefault(p.Id);
            p.ReviewCount = s?.Count ?? 0;
            p.Rating = s == null ? 0 : Math.Round((decimal)s.Avg, 1);
        }
        await db.SaveChangesAsync(ct);
    }

    public static Task RecomputeAsync(AppDbContext db, int productId, CancellationToken ct = default) =>
        RecomputeAsync(db, new[] { productId }, ct);

    public static async Task<ReviewSettings> GetSettingsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var block = await db.SiteContentBlocks.AsNoTracking().FirstOrDefaultAsync(b => b.Key == SettingsKey && !b.IsDeleted, ct);
        if (string.IsNullOrWhiteSpace(block?.Json)) return new ReviewSettings();
        try { return JsonSerializer.Deserialize<ReviewSettings>(block.Json, Json) ?? new ReviewSettings(); }
        catch { return new ReviewSettings(); }
    }

    public static async Task SaveSettingsAsync(AppDbContext db, ReviewSettings settings, string by, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, Json);
        var block = await db.SiteContentBlocks.FirstOrDefaultAsync(b => b.Key == SettingsKey, ct);
        if (block == null)
            db.SiteContentBlocks.Add(new SiteContentBlock { Key = SettingsKey, Json = json, IsPublic = false, CreatedBy = by });
        else
        {
            block.Json = json;
            block.IsDeleted = false;
            block.IsPublic = false;
            block.UpdatedAt = DateTime.UtcNow;
            block.UpdatedBy = by;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A purchase counts as verified when the user has a non-cancelled order containing the product
    /// (cancelled / returned orders don't count).
    /// </summary>
    public static Task<bool> HasPurchasedAsync(AppDbContext db, int userId, int productId, CancellationToken ct = default) =>
        db.OrderItems.AnyAsync(oi => oi.ProductId == productId && oi.Order.UserId == userId && !oi.Order.IsDeleted
            && oi.Order.Status != OrderStatus.Cancelled && oi.Order.Status != OrderStatus.Returned, ct);
}
