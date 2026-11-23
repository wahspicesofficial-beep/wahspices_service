using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// Keeps Product.InStock in line with inventory.
///
/// Rule: a product is in stock on the website when
///   • it is NOT on the admin's manual "out of stock" list (SiteContentBlock "stockOverrides"), and
///   • it has no active InventoryItem (untracked products are sellable), or the sum of its rows' CurrentStock &gt; 0
///     (a product can have one product-level row plus one row per pack-size variant, filled by packing runs).
/// A variant with its own stock row (InventoryItem.VariantId) is in stock when that row's CurrentStock &gt; 0;
/// variants without a row keep the flag the admin set on the product form.
///
/// <see cref="StockSyncInterceptor"/> applies the rule automatically whenever any code saves a change to an
/// InventoryItem's CurrentStock / IsDeleted / ProductId (manual adjust, GRN, invoice, order deduction, production …),
/// so callers don't have to remember to sync. <see cref="SyncAsync"/> can also be called explicitly.
/// </summary>
public static class ProductStockSync
{
    public const string OverridesKey = "stockOverrides";

    private sealed class Overrides
    {
        public List<int> ManualOutOfStock { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>Product ids the admin explicitly marked out of stock.</summary>
    public static async Task<HashSet<int>> GetManualOutOfStockAsync(AppDbContext db, CancellationToken ct = default)
    {
        var block = await db.SiteContentBlocks.AsNoTracking().FirstOrDefaultAsync(b => b.Key == OverridesKey && !b.IsDeleted, ct);
        return Parse(block?.Json);
    }

    private static HashSet<int> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<int>();
        try { return (JsonSerializer.Deserialize<Overrides>(json, Json)?.ManualOutOfStock ?? new()).ToHashSet(); }
        catch { return new HashSet<int>(); }
    }

    /// <summary>Add/remove a product from the manual out-of-stock list (tracked change — caller saves).</summary>
    public static async Task SetManualOutOfStockAsync(AppDbContext db, int productId, bool manualOut, CancellationToken ct = default)
    {
        var block = await db.SiteContentBlocks.FirstOrDefaultAsync(b => b.Key == OverridesKey, ct);
        var set = Parse(block?.Json);
        if (manualOut) set.Add(productId); else set.Remove(productId);
        var json = JsonSerializer.Serialize(new Overrides { ManualOutOfStock = set.OrderBy(x => x).ToList() }, Json);
        if (block == null)
            db.SiteContentBlocks.Add(new SiteContentBlock { Key = OverridesKey, Json = json, IsPublic = false, CreatedBy = "system" });
        else
        {
            block.Json = json;
            block.IsDeleted = false;
            block.IsPublic = false;
            block.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Effective availability for one product.</summary>
    public static bool Effective(bool manualOut, int? trackedStock) => !manualOut && (trackedStock == null || trackedStock > 0);

    /// <summary>
    /// Recompute Product.InStock for the given products from the database state + pending tracked changes
    /// and save. Use from services that change stock outside EF tracking (none today) or to repair data.
    /// Returns the number of products whose InStock flag changed.
    /// </summary>
    public static async Task<int> SyncAsync(AppDbContext db, IEnumerable<int>? productIds = null, CancellationToken ct = default)
    {
        var manual = await GetManualOutOfStockAsync(db, ct);
        var q = db.Products.Where(p => !p.IsDeleted);
        if (productIds != null)
        {
            var ids = productIds.Distinct().ToList();
            q = q.Where(p => ids.Contains(p.Id));
        }
        var products = await q.ToListAsync(ct);
        var pids = products.Select(p => p.Id).ToList();
        var stock = await db.InventoryItems.Where(i => !i.IsDeleted && pids.Contains(i.ProductId))
            .GroupBy(i => i.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.CurrentStock) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var changed = 0;
        foreach (var p in products)
        {
            var want = Effective(manual.Contains(p.Id), stock.TryGetValue(p.Id, out var s) ? s : null);
            if (p.InStock != want) { p.InStock = want; p.UpdatedAt = DateTime.UtcNow; changed++; }
        }

        // Pack-size rows → ProductVariant.InStock (variants without a stock row keep the admin's flag)
        var variantStock = await db.InventoryItems.Where(i => !i.IsDeleted && i.VariantId != null && pids.Contains(i.ProductId))
            .GroupBy(i => i.VariantId!.Value).Select(g => new { g.Key, Qty = g.Sum(x => x.CurrentStock) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        if (variantStock.Count > 0)
        {
            var vids = variantStock.Keys.ToList();
            foreach (var v in await db.ProductVariants.Where(v => vids.Contains(v.Id)).ToListAsync(ct))
            {
                var want = variantStock[v.Id] > 0;
                if (v.InStock != want) { v.InStock = want; v.UpdatedAt = DateTime.UtcNow; changed++; }
            }
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>
    /// Called by the interceptor before EF writes: adjusts Product.InStock (sum of all the product's rows) and
    /// ProductVariant.InStock (the variant's own rows) for every product whose inventory rows were touched.
    /// </summary>
    internal static async Task ApplyPendingAsync(AppDbContext db, CancellationToken ct)
    {
        var touched = db.ChangeTracker.Entries<InventoryItem>()
            .Where(IsStockRelevant)
            .ToList();
        if (touched.Count == 0) return;

        var productIds = new HashSet<int>();
        var variantIds = new HashSet<int>();
        foreach (var entry in touched)
        {
            if (entry.Entity.ProductId > 0) productIds.Add(entry.Entity.ProductId);
            if (entry.Entity.VariantId is int v) variantIds.Add(v);
            if (entry.State == EntityState.Modified)
            {
                // A row that moved to another product / variant: resync where it came from too
                if (entry.Property(x => x.ProductId).IsModified && entry.Property(x => x.ProductId).OriginalValue is int oldP && oldP > 0)
                    productIds.Add(oldP);
                if (entry.Property(x => x.VariantId).IsModified && entry.Property(x => x.VariantId).OriginalValue is int oldV)
                    variantIds.Add(oldV);
            }
        }

        // Rows as they WILL be after this save: DB rows (tracked instances keep their pending values) + added rows
        var rows = await db.InventoryItems.AsTracking().Where(i => productIds.Contains(i.ProductId)).ToListAsync(ct);
        foreach (var added in db.ChangeTracker.Entries<InventoryItem>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            if (productIds.Contains(added.ProductId) && !rows.Contains(added)) rows.Add(added);
        bool Active(InventoryItem i) => !i.IsDeleted && db.Entry(i).State != EntityState.Deleted;

        var manual = await GetManualOutOfStockAsync(db, ct);
        foreach (var pid in productIds)
        {
            var product = await db.Products.FindAsync(new object[] { pid }, ct);
            if (product == null) continue;
            var mine = rows.Where(r => r.ProductId == pid && Active(r)).ToList();
            SetInStock(db, product, Effective(manual.Contains(pid), mine.Count == 0 ? null : mine.Sum(r => r.CurrentStock)));
        }

        // Variant availability follows its own stock row; untracked variants keep the admin's flag
        foreach (var vid in variantIds)
        {
            var variant = await db.ProductVariants.FindAsync(new object[] { vid }, ct);
            if (variant == null) continue;
            var mine = rows.Where(r => r.VariantId == vid && Active(r)).ToList();
            if (mine.Count == 0) continue;
            var want = mine.Sum(r => r.CurrentStock) > 0;
            if (variant.InStock == want) continue;
            var e = db.Entry(variant);
            e.Property(x => x.InStock).CurrentValue = want;
            e.Property(x => x.UpdatedAt).CurrentValue = DateTime.UtcNow;
            if (e.State == EntityState.Unchanged)
            {
                e.Property(x => x.InStock).IsModified = true;
                e.Property(x => x.UpdatedAt).IsModified = true;
            }
        }
    }

    // DetectChanges has already run when the interceptor fires, so set values through the Entry API
    // (marks the property modified explicitly instead of relying on snapshot comparison).
    private static void SetInStock(AppDbContext db, Product product, bool want)
    {
        if (product.InStock == want) return;
        var e = db.Entry(product);
        e.Property(p => p.InStock).CurrentValue = want;
        e.Property(p => p.UpdatedAt).CurrentValue = DateTime.UtcNow;
        if (e.State == EntityState.Unchanged)
        {
            e.Property(p => p.InStock).IsModified = true;
            e.Property(p => p.UpdatedAt).IsModified = true;
        }
    }

    private static bool IsStockRelevant(EntityEntry<InventoryItem> e) => e.State switch
    {
        EntityState.Added => true,
        EntityState.Deleted => true,
        EntityState.Modified => e.Property(x => x.CurrentStock).IsModified
                                || e.Property(x => x.IsDeleted).IsModified
                                || e.Property(x => x.ProductId).IsModified
                                || e.Property(x => x.VariantId).IsModified,
        _ => false,
    };
}

/// <summary>EF interceptor that runs <see cref="ProductStockSync.ApplyPendingAsync"/> on every SaveChanges.</summary>
public sealed class StockSyncInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db)
            ProductStockSync.ApplyPendingAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db)
            await ProductStockSync.ApplyPendingAsync(db, cancellationToken);
        return result;
    }
}
