using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// Store-wide rules shared by cart, checkout, orders and returns:
///  • delivery pricing / COD / maintenance mode come from SiteSettings (Admin → Settings), never appsettings;
///  • stock movements for website orders (deduct on placement, restore on cancel / return received).
/// Stock helpers are idempotent: every movement is written to InventoryLedger with ReferenceId = order/return
/// number, and a second call for the same reference is a no-op.
/// </summary>
public static class StoreRules
{
    /// <summary>The single SiteSettings row, or defaults when the admin never saved settings.</summary>
    public static async Task<SiteSettings> GetSettingsAsync(AppDbContext db) =>
        await db.SiteSettings.AsNoTracking().Where(s => !s.IsDeleted).OrderBy(s => s.Id).FirstOrDefaultAsync()
        ?? new SiteSettings();

    /// <summary>Delivery charge for an order value (after coupon). Threshold 0 = always free.</summary>
    public static decimal DeliveryFor(SiteSettings s, decimal orderValue) =>
        orderValue <= 0 ? 0 : (s.FreeDeliveryAbove <= 0 || orderValue >= s.FreeDeliveryAbove ? 0 : Math.Max(0, s.DeliveryCharge));

    /// <summary>Current stock per product (sum over its non-deleted inventory rows). Products without rows are absent.</summary>
    public static async Task<Dictionary<int, int>> StockByProductAsync(AppDbContext db, IEnumerable<int> productIds)
    {
        var ids = productIds.Distinct().ToList();
        return await db.InventoryItems
            .Where(i => ids.Contains(i.ProductId) && !i.IsDeleted)
            .GroupBy(i => i.ProductId)
            .Select(g => new { g.Key, Stock = g.Sum(i => i.CurrentStock) })
            .ToDictionaryAsync(x => x.Key, x => x.Stock);
    }

    /// <summary>
    /// Deduct stock for every item of an order (once). Caller saves changes.
    /// Pack-size aware: when the order line's variant has its own stock row (filled by packing runs) the packets
    /// come from that row; otherwise from the product-level row (legacy behaviour). The variant of each line is
    /// read from the order itself (pending in the change tracker, or from the database), so callers keep passing
    /// plain (ProductId, Quantity) lines.
    /// </summary>
    public static async Task DeductForOrderAsync(AppDbContext db, string orderNumber, IEnumerable<(int ProductId, int Quantity)> lines, string by)
    {
        if (await db.InventoryLedgers.AnyAsync(l => l.ReferenceId == orderNumber && l.Type == StockAdjustmentType.Reduction))
            return;
        foreach (var (productId, variantId, qty) in await ResolveVariantLinesAsync(db, orderNumber, lines))
        {
            var all = await db.InventoryItems
                .Where(i => i.ProductId == productId && !i.IsDeleted)
                .OrderByDescending(i => i.CurrentStock).ToListAsync();
            var rows = PickRows(all, variantId);
            var remaining = qty;
            for (var k = 0; k < rows.Count && remaining > 0; k++)
            {
                var inv = rows[k];
                // take from the fullest location first; the last row absorbs any shortfall (never below 0)
                var take = k == rows.Count - 1 ? remaining : Math.Min(remaining, Math.Max(0, inv.CurrentStock));
                if (take <= 0) continue;
                var newStock = Math.Max(0, inv.CurrentStock - take);
                db.InventoryLedgers.Add(new InventoryLedger
                {
                    InventoryItemId = inv.Id, Type = StockAdjustmentType.Reduction, Quantity = take,
                    BalanceAfter = newStock, Reason = $"Website order #{orderNumber}", ReferenceId = orderNumber,
                    PerformedBy = by, CreatedBy = by,
                });
                inv.CurrentStock = newStock;
                inv.UpdatedAt = DateTime.UtcNow;
                inv.UpdatedBy = by;
                remaining -= take;
            }
        }
    }

    /// <summary>
    /// Stock rows an order line draws from: the variant's own rows → else the product-level row →
    /// else (line without a variant) any row of the product. A variant with no row of its own while the
    /// product only has other pack-size rows is untracked, so nothing is deducted.
    /// </summary>
    private static List<InventoryItem> PickRows(List<InventoryItem> all, int? variantId)
    {
        if (variantId.HasValue)
        {
            var own = all.Where(i => i.VariantId == variantId).ToList();
            if (own.Count > 0) return own;
        }
        var productLevel = all.Where(i => i.VariantId == null).ToList();
        if (productLevel.Count > 0) return productLevel;
        return variantId.HasValue ? new List<InventoryItem>() : all;
    }

    /// <summary>(ProductId, VariantId, Quantity) per order line; falls back to the given lines when the order isn't found.</summary>
    private static async Task<List<(int ProductId, int? VariantId, int Quantity)>> ResolveVariantLinesAsync(
        AppDbContext db, string orderNumber, IEnumerable<(int ProductId, int Quantity)> lines)
    {
        var given = Merge(lines).ToList();
        var order = db.Orders.Local.FirstOrDefault(o => o.OrderNumber == orderNumber)
                    ?? await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        var items = order?.Items.Where(i => !i.IsDeleted && i.Quantity > 0).ToList();
        if (items != null && items.Count > 0)
        {
            var fromOrder = items.GroupBy(i => (i.ProductId, i.VariantId))
                .Select(g => (g.Key.ProductId, g.Key.VariantId, Quantity: g.Sum(x => x.Quantity))).ToList();
            // use the order's lines only when they describe the same products / quantities the caller passed
            var sums = fromOrder.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            if (given.Count == sums.Count && given.All(g => sums.TryGetValue(g.Item1, out var q) && q == g.Item2))
                return fromOrder;
        }
        return given.Select(g => (g.Item1, (int?)null, g.Item2)).ToList();
    }

    /// <summary>
    /// Put stock back (once per reference). Only restores when the order's stock was actually deducted.
    /// reference = order number (cancel) or return number (return received). Caller saves changes.
    /// Packets go back to the rows they were taken from (pack-size rows included), in deduction order.
    /// </summary>
    public static async Task RestoreAsync(AppDbContext db, string orderNumber, string reference, IEnumerable<(int ProductId, int Quantity)> lines, string reason, string by)
    {
        var deducted = await db.InventoryLedgers
            .Where(l => l.ReferenceId == orderNumber && l.Type == StockAdjustmentType.Reduction)
            .OrderBy(l => l.Id)
            .Select(l => new { l.InventoryItemId, l.Quantity, l.InventoryItem.ProductId })
            .ToListAsync();
        if (deducted.Count == 0) return;
        if (await db.InventoryLedgers.AnyAsync(l => l.ReferenceId == reference && l.Type == StockAdjustmentType.Return))
            return;
        foreach (var (productId, qty) in Merge(lines))
        {
            if (qty <= 0) continue;
            var remaining = qty;
            var puts = new List<(InventoryItem Row, int Qty)>();
            foreach (var d in deducted.Where(d => d.ProductId == productId))
            {
                if (remaining <= 0) break;
                var row = await db.InventoryItems.FindAsync(d.InventoryItemId);
                if (row == null || row.IsDeleted) continue;
                var put = Math.Min(remaining, d.Quantity);
                puts.Add((row, put));
                remaining -= put;
            }
            if (remaining > 0)
            {
                // more than was deducted (or the original rows are gone): first surviving row, else any row of the product
                var row = puts.Select(p => p.Row).FirstOrDefault()
                          ?? await db.InventoryItems.Where(i => i.ProductId == productId && !i.IsDeleted)
                                .OrderBy(i => i.VariantId.HasValue).FirstOrDefaultAsync();
                if (row != null) puts.Add((row, remaining));
            }
            foreach (var g in puts.GroupBy(p => p.Row))
            {
                var inv = g.Key;
                var put = g.Sum(x => x.Qty);
                inv.CurrentStock += put;
                inv.UpdatedAt = DateTime.UtcNow;
                inv.UpdatedBy = by;
                db.InventoryLedgers.Add(new InventoryLedger
                {
                    InventoryItemId = inv.Id, Type = StockAdjustmentType.Return, Quantity = put,
                    BalanceAfter = inv.CurrentStock, Reason = reason, ReferenceId = reference,
                    PerformedBy = by, CreatedBy = by,
                });
            }
        }
    }

    private static IEnumerable<(int, int)> Merge(IEnumerable<(int ProductId, int Quantity)> lines) =>
        lines.GroupBy(l => l.ProductId).Select(g => (g.Key, g.Sum(x => x.Quantity)));
}
