using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>Validation / business-rule failure of a raw-material or packing operation (→ HTTP 400/409).</summary>
public sealed class PackingException : Exception
{
    public string Code { get; }
    public string? Field { get; }
    public object? Details { get; }
    public PackingException(string code, string message, string? field = null, object? details = null) : base(message)
    {
        Code = code; Field = field; Details = details;
    }
}

public sealed record PackingLineInput(int ProductId, int? VariantId, int? PackWeightGrams, int Packets);

public sealed record PackingInput(
    int RawMaterialId,
    decimal? RawQuantityUsed,
    string? RawUnit,
    decimal? Wastage,
    string? WastageUnit,
    List<PackingLineInput>? Lines,
    DateTime? PackedOn,
    string? BatchNumber,
    string? PackedBy,
    string? Notes,
    bool ConfirmMismatch = false);

/// <summary>Result of validating a packing run (also returned by the dry-run preview).</summary>
public sealed record PackingPlan(
    RawMaterial RawMaterial,
    decimal RawUsedKg, decimal PackedKg, decimal WastageKg, decimal VarianceKg, decimal ToleranceKg,
    decimal RawStockBeforeKg, decimal RawStockAfterKg,
    List<PackingPlanLine> Lines,
    List<string> Warnings);

public sealed record PackingPlanLine(Product Product, ProductVariant? Variant, string PackLabel, int PackWeightGrams, int Packets, decimal TotalKg, int CurrentStock);

/// <summary>
/// Raw material → finished packets.
///  • Raw stock lives on <see cref="RawMaterial.CurrentStockKg"/> (kg, 3 decimals) and every movement is written to
///    <see cref="RawMaterialLedger"/>.
///  • A packing run deducts the raw kg used and adds packets to the pack-size <see cref="InventoryItem"/> of each
///    product variant (created on first use), writing InventoryLedger rows of type Packing. ProductStockSync then
///    flips Product.InStock / ProductVariant.InStock on the website.
///  • Reversal restores both sides (refused when the packets were already sold).
/// Callers save changes (and wrap create / reverse in a transaction — see AdminPackingController).
/// </summary>
public static class PackingRules
{
    /// <summary>Accepted difference between raw used and packed + wastage before the admin must confirm it.</summary>
    public static decimal ToleranceFor(decimal usedKg) => Math.Max(0.010m, Math.Round(usedKg * 0.005m, 3));

    public static readonly string[] Units = { "kg", "g" };

    /// <summary>Converts a quantity in kg / g / quintal / tonne to kg (3 decimals).</summary>
    public static decimal ToKg(decimal qty, string? unit, string field = "unit")
    {
        var factor = (unit ?? "kg").Trim().ToLowerInvariant() switch
        {
            "kg" or "kgs" or "kilogram" or "kilograms" => 1m,
            "g" or "gm" or "gms" or "gram" or "grams" => 0.001m,
            "quintal" or "qtl" => 100m,
            "ton" or "tonne" or "mt" => 1000m,
            _ => throw new PackingException("INVALID_UNIT", $"Unit '{unit}' is not a weight unit (use kg or g).", field),
        };
        return Math.Round(qty * factor, 3);
    }

    /// <summary>"500g" → 500, "1kg" → 1000, "1.5 Kg" → 1500, "250 gm" → 250; 0 when not a weight.</summary>
    public static int ParseWeightGrams(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return 0;
        var m = Regex.Match(label.Trim().ToLowerInvariant(), @"^([0-9]+(?:\.[0-9]+)?)\s*(kg|kgs|g|gm|gms|gram|grams)\b");
        if (!m.Success) return 0;
        var n = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return (int)Math.Round(m.Groups[2].Value.StartsWith("k") ? n * 1000 : n);
    }

    public static int PackGrams(ProductVariant v) => v.WeightGrams > 0 ? v.WeightGrams : ParseWeightGrams(v.Weight);

    public static string FormatKg(decimal kg) =>
        Math.Abs(kg) < 1 && kg != 0 ? $"{kg * 1000:0.#} g" : $"{kg:0.###} kg";

    // ── Raw stock movements ────────────────────────────────────────────────────

    /// <summary>Moves raw stock by <paramref name="deltaKg"/> (signed) and writes the ledger row. Never goes below zero.</summary>
    public static RawMaterialLedger Move(AppDbContext db, RawMaterial rm, decimal deltaKg, string type, string by,
        string? refType = null, string? refId = null, string? batch = null, string? reason = null, decimal? unitCostPerKg = null)
    {
        deltaKg = Math.Round(deltaKg, 3);
        var after = rm.CurrentStockKg + deltaKg;
        if (after < 0)
            throw new PackingException("INSUFFICIENT_RAW_STOCK",
                $"Only {FormatKg(rm.CurrentStockKg)} of {rm.Name} in stock — cannot take out {FormatKg(-deltaKg)}.", "quantity",
                new { availableKg = rm.CurrentStockKg, requestedKg = -deltaKg });
        rm.CurrentStockKg = after;
        rm.UpdatedAt = DateTime.UtcNow;
        rm.UpdatedBy = by;
        var row = new RawMaterialLedger
        {
            RawMaterial = rm, Type = type, QuantityKg = deltaKg, BalanceAfterKg = after, UnitCostPerKg = unitCostPerKg,
            ReferenceType = refType, ReferenceId = refId, BatchNumber = Trim(batch, 60), Reason = Trim(reason, 500),
            PerformedBy = by, CreatedBy = by,
        };
        db.RawMaterialLedgers.Add(row);
        return row;
    }

    /// <summary>Stock-in that carries a price (purchase / opening): updates the weighted-average cost per kg.</summary>
    public static RawMaterialLedger Receive(AppDbContext db, RawMaterial rm, decimal kg, decimal? costPerKg, string type, string by,
        string? refType = null, string? refId = null, string? batch = null, string? reason = null)
    {
        if (costPerKg is > 0 && kg > 0)
        {
            var oldQty = Math.Max(0, rm.CurrentStockKg);
            rm.CostPerKg = oldQty + kg == 0 ? costPerKg.Value
                : Math.Round((oldQty * rm.CostPerKg + kg * costPerKg.Value) / (oldQty + kg), 2);
        }
        return Move(db, rm, kg, type, by, refType, refId, batch, reason, costPerKg);
    }

    // ── Packing ────────────────────────────────────────────────────────────────

    /// <summary>Validates a packing run against current stock and computes all quantities (nothing is changed).</summary>
    public static async Task<PackingPlan> PlanAsync(AppDbContext db, PackingInput req, RawMaterial? lockedRaw = null)
    {
        var rm = lockedRaw ?? await db.RawMaterials.FirstOrDefaultAsync(r => r.Id == req.RawMaterialId && !r.IsDeleted);
        if (rm == null || rm.IsDeleted) throw new PackingException("NOT_FOUND", "Raw material not found.", "rawMaterialId");
        if (!rm.IsActive) throw new PackingException("INACTIVE", $"{rm.Name} is inactive — activate it before packing.", "rawMaterialId");

        var lines = (req.Lines ?? new()).Where(l => l != null && l.ProductId > 0).ToList();
        if (lines.Count == 0) throw new PackingException("NO_LINES", "Add at least one pack size with the number of packets.", "lines");
        if (lines.Count > 30) throw new PackingException("TOO_MANY_LINES", "A packing entry can have at most 30 pack sizes.", "lines");
        if (lines.Any(l => l.Packets <= 0)) throw new PackingException("INVALID_PACKETS", "Number of packets must be at least 1 on every row.", "lines");
        if (lines.Any(l => l.Packets > 1_000_000)) throw new PackingException("INVALID_PACKETS", "Number of packets looks wrong (more than 10 lakh).", "lines");
        if (lines.GroupBy(l => (l.ProductId, l.VariantId, l.VariantId.HasValue ? 0 : l.PackWeightGrams ?? 0)).Any(g => g.Count() > 1))
            throw new PackingException("DUPLICATE_LINE", "The same pack size appears twice — combine the packets into one row.", "lines");

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Include(p => p.Variants)
            .Where(p => productIds.Contains(p.Id) && !p.IsDeleted).ToDictionaryAsync(p => p.Id);
        var stockRows = await db.InventoryItems.AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId) && !i.IsDeleted).ToListAsync();

        var planLines = new List<PackingPlanLine>();
        foreach (var l in lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p))
                throw new PackingException("PRODUCT_NOT_FOUND", $"Product #{l.ProductId} not found.", "lines");
            var variants = p.Variants.Where(v => !v.IsDeleted).ToList();
            ProductVariant? variant = null;
            int grams;
            string label;
            if (variants.Count > 0)
            {
                if (!l.VariantId.HasValue)
                    throw new PackingException("VARIANT_REQUIRED", $"Choose the pack size of {p.Name}.", "lines");
                variant = variants.FirstOrDefault(v => v.Id == l.VariantId.Value)
                          ?? throw new PackingException("VARIANT_NOT_FOUND", $"That pack size does not belong to {p.Name}.", "lines");
                grams = PackGrams(variant);
                label = variant.Weight;
                if (grams <= 0)
                    throw new PackingException("PACK_WEIGHT_UNKNOWN", $"{p.Name} {variant.Weight}: set its weight in grams on the product first.", "lines");
            }
            else
            {
                if (l.VariantId.HasValue)
                    throw new PackingException("VARIANT_NOT_FOUND", $"{p.Name} has no pack sizes.", "lines");
                grams = l.PackWeightGrams is > 0 ? l.PackWeightGrams.Value : ParseWeightGrams(p.Weight);
                label = string.IsNullOrWhiteSpace(p.Weight) ? $"{grams}g" : p.Weight;
                if (grams <= 0)
                    throw new PackingException("PACK_WEIGHT_REQUIRED", $"Enter the pack weight (grams) for {p.Name}.", "lines");
            }
            if (grams > 100_000) throw new PackingException("PACK_WEIGHT_INVALID", $"Pack weight of {p.Name} looks wrong ({grams} g).", "lines");

            var current = variant != null
                ? stockRows.Where(r => r.VariantId == variant.Id).Sum(r => r.CurrentStock)
                : stockRows.Where(r => r.VariantId == null).Sum(r => r.CurrentStock);
            planLines.Add(new PackingPlanLine(p, variant, label, grams, l.Packets, Math.Round(l.Packets * grams / 1000m, 3), current));
        }

        var packedKg = planLines.Sum(x => x.TotalKg);
        var wastage = req.Wastage ?? 0;
        if (wastage < 0) throw new PackingException("INVALID_WASTAGE", "Wastage cannot be negative.", "wastage");
        var wastageKg = ToKg(wastage, req.WastageUnit ?? req.RawUnit, "wastageUnit");
        var usedKg = req.RawQuantityUsed.HasValue ? ToKg(req.RawQuantityUsed.Value, req.RawUnit, "rawUnit") : packedKg + wastageKg;
        if (req.RawQuantityUsed is <= 0) throw new PackingException("INVALID_QUANTITY", "Raw material used must be more than zero.", "rawQuantityUsed");

        var variance = Math.Round(usedKg - packedKg - wastageKg, 3);
        var tolerance = ToleranceFor(usedKg);
        var warnings = new List<string>();
        if (variance < -tolerance)
            throw new PackingException("PACKED_EXCEEDS_RAW",
                $"Packets ({FormatKg(packedKg)}) + wastage ({FormatKg(wastageKg)}) come to more than the raw material used ({FormatKg(usedKg)}). Check the packet counts or the quantity used.",
                "rawQuantityUsed", new { usedKg, packedKg, wastageKg, varianceKg = variance, toleranceKg = tolerance });
        if (variance > tolerance)
        {
            var msg = $"{FormatKg(variance)} of the {FormatKg(usedKg)} used is not accounted for (packed {FormatKg(packedKg)} + wastage {FormatKg(wastageKg)}).";
            if (!req.ConfirmMismatch)
                throw new PackingException("QUANTITY_MISMATCH", msg + " Record it as wastage, fix the numbers, or confirm to save the difference as unexplained loss.",
                    "rawQuantityUsed", new { usedKg, packedKg, wastageKg, varianceKg = variance, toleranceKg = tolerance });
            warnings.Add(msg + " Saved as unexplained loss.");
        }
        if (usedKg > rm.CurrentStockKg)
            throw new PackingException("INSUFFICIENT_RAW_STOCK",
                $"Only {FormatKg(rm.CurrentStockKg)} of {rm.Name} in stock — this packing needs {FormatKg(usedKg)}.", "rawQuantityUsed",
                new { availableKg = rm.CurrentStockKg, requestedKg = usedKg });

        var after = rm.CurrentStockKg - usedKg;
        if (after <= rm.ReorderLevelKg && rm.ReorderLevelKg > 0)
            warnings.Add($"{rm.Name} will be at {FormatKg(after)} — at or below its reorder level ({FormatKg(rm.ReorderLevelKg)}).");

        return new PackingPlan(rm, usedKg, packedKg, wastageKg, variance, tolerance, rm.CurrentStockKg, after, planLines, warnings);
    }

    /// <summary>Applies a validated plan: raw stock out, packets in, packing entry + ledgers added. Caller saves.</summary>
    public static async Task<PackingEntry> ApplyAsync(AppDbContext db, PackingInput req, PackingPlan plan, string by)
    {
        var rm = plan.RawMaterial;
        var packedOn = (req.PackedOn ?? DateTime.UtcNow);
        var number = await NextNumberAsync(db, packedOn);
        var batch = Trim(req.BatchNumber, 60);
        var entry = new PackingEntry
        {
            PackingNumber = number, RawMaterial = rm, RawUsedKg = plan.RawUsedKg, PackedKg = plan.PackedKg,
            WastageKg = plan.WastageKg, VarianceKg = plan.VarianceKg, CostPerKg = rm.CostPerKg, PackedOn = packedOn,
            BatchNumber = batch, PackedBy = Trim(req.PackedBy, 150) ?? by, Notes = Trim(req.Notes, 1000),
            Status = "Completed", CreatedBy = by,
        };
        db.PackingEntries.Add(entry);

        var lossNote = plan.WastageKg + Math.Max(0, plan.VarianceKg) > 0
            ? $" (packed {FormatKg(plan.PackedKg)}, wastage {FormatKg(plan.WastageKg)}{(plan.VarianceKg > 0 ? $", unexplained {FormatKg(plan.VarianceKg)}" : "")})" : "";
        Move(db, rm, -plan.RawUsedKg, "PackingConsumption", by, "Packing", number, batch,
            $"Packed into {string.Join(", ", plan.Lines.Select(l => $"{l.Packets}×{l.PackLabel}"))}{lossNote}", rm.CostPerKg);

        foreach (var l in plan.Lines)
        {
            var row = await EnsureStockRowAsync(db, l.Product, l.Variant, rm.Location, by);
            var packetCost = Math.Round(rm.CostPerKg * l.PackWeightGrams / 1000m, 2);
            if (packetCost > 0)
            {
                var oldQty = Math.Max(0, row.CurrentStock);
                row.CostPrice = oldQty == 0 || row.CostPrice <= 0 ? packetCost
                    : Math.Round((oldQty * row.CostPrice + l.Packets * packetCost) / (oldQty + l.Packets), 2);
            }
            row.CurrentStock += l.Packets;
            if (batch != null) row.BatchNumber = batch;
            row.ManufactureDate = packedOn;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = by;
            db.InventoryLedgers.Add(new InventoryLedger
            {
                InventoryItem = row, Type = StockAdjustmentType.Packing, Quantity = l.Packets, BalanceAfter = row.CurrentStock,
                Reason = $"Packed from {rm.Name} ({FormatKg(l.TotalKg)})", BatchNumber = batch, ReferenceId = number,
                PerformedBy = by, CreatedBy = by,
            });
            entry.Lines.Add(new PackingEntryLine
            {
                ProductId = l.Product.Id, VariantId = l.Variant?.Id, PackLabel = l.PackLabel, PackWeightGrams = l.PackWeightGrams,
                Packets = l.Packets, TotalKg = l.TotalKg, InventoryItem = row, CreatedBy = by,
            });
        }
        return entry;
    }

    /// <summary>Reverses a completed packing run: packets out of finished stock, raw kg back in. Caller saves.</summary>
    public static async Task ReverseAsync(AppDbContext db, PackingEntry entry, string? reason, string by)
    {
        if (entry.Status == "Reversed") throw new PackingException("ALREADY_REVERSED", $"{entry.PackingNumber} was already reversed.");
        var short_ = new List<string>();
        var rows = new Dictionary<int, InventoryItem>();
        foreach (var g in entry.Lines.Where(l => !l.IsDeleted).GroupBy(l => l.InventoryItemId))
        {
            var row = await db.InventoryItems.FindAsync(g.Key);
            var need = g.Sum(x => x.Packets);
            if (row == null || row.IsDeleted) { short_.Add($"{g.First().PackLabel}: stock row removed"); continue; }
            if (row.CurrentStock < need) short_.Add($"{g.First().PackLabel}: {row.CurrentStock} left, {need} needed");
            rows[g.Key] = row;
        }
        if (short_.Count > 0)
            throw new PackingException("PACKETS_ALREADY_USED",
                $"Cannot reverse {entry.PackingNumber} — some packets were already sold or moved ({string.Join("; ", short_)}). Adjust stock manually instead.");

        var why = Trim(reason, 500);
        foreach (var g in entry.Lines.Where(l => !l.IsDeleted).GroupBy(l => l.InventoryItemId))
        {
            var row = rows[g.Key];
            var qty = g.Sum(x => x.Packets);
            row.CurrentStock -= qty;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = by;
            db.InventoryLedgers.Add(new InventoryLedger
            {
                InventoryItemId = row.Id, Type = StockAdjustmentType.PackingReversal, Quantity = qty, BalanceAfter = row.CurrentStock,
                Reason = $"Packing {entry.PackingNumber} reversed{(why != null ? $": {why}" : "")}", BatchNumber = entry.BatchNumber,
                ReferenceId = entry.PackingNumber, PerformedBy = by, CreatedBy = by,
            });
        }
        Move(db, entry.RawMaterial, entry.RawUsedKg, "PackingReversal", by, "Packing", entry.PackingNumber, entry.BatchNumber,
            $"Packing {entry.PackingNumber} reversed{(why != null ? $": {why}" : "")}", entry.CostPerKg);

        entry.Status = "Reversed";
        entry.ReversedAt = DateTime.UtcNow;
        entry.ReversedBy = by;
        entry.ReversalReason = why;
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedBy = by;
    }

    /// <summary>The finished-goods row of a pack size (product-level row for products without variants); created on first use.</summary>
    public static async Task<InventoryItem> EnsureStockRowAsync(AppDbContext db, Product product, ProductVariant? variant, string location, string by)
    {
        var vid = variant?.Id;
        var pending = db.ChangeTracker.Entries<InventoryItem>()
            .Select(e => e.Entity).FirstOrDefault(i => i.ProductId == product.Id && i.VariantId == vid && !i.IsDeleted);
        if (pending != null) return pending;
        var rows = await db.InventoryItems.Where(i => i.ProductId == product.Id && i.VariantId == vid)
            .OrderBy(i => i.IsDeleted).ThenBy(i => i.Id).ToListAsync();
        var row = rows.FirstOrDefault(i => !i.IsDeleted);
        if (row != null) return row;
        row = rows.FirstOrDefault();
        if (row != null)
        {
            // revive a removed row (its ledger history stays attached)
            row.IsDeleted = false;
            row.CurrentStock = 0;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = by;
            return row;
        }

        var baseSku = variant != null
            ? (!string.IsNullOrWhiteSpace(variant.Sku) ? variant.Sku!.Trim() : $"{SkuStem(product)}-{SkuPart(variant.Weight)}")
            : SkuStem(product);
        var sku = baseSku;
        for (var n = 2; await db.InventoryItems.AnyAsync(i => i.Sku == sku) || db.ChangeTracker.Entries<InventoryItem>().Any(e => e.Entity.Sku == sku); n++)
            sku = $"{baseSku}-{n}";
        var productLevel = await db.InventoryItems.AsNoTracking()
            .Where(i => i.ProductId == product.Id && i.VariantId == null && !i.IsDeleted).FirstOrDefaultAsync();
        row = new InventoryItem
        {
            ProductId = product.Id, Product = product, VariantId = vid, Variant = variant, Sku = sku, CurrentStock = 0,
            ReorderPoint = productLevel?.ReorderPoint ?? 10, Location = string.IsNullOrWhiteSpace(location) ? "Warehouse A" : location,
            Unit = "pcs", CreatedBy = by,
        };
        db.InventoryItems.Add(row);
        return row;
    }

    private static string SkuStem(Product p)
    {
        var stem = Regex.Replace((p.Slug ?? p.Name).ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        return stem.Length > 40 ? stem[..40] : stem;
    }

    private static string SkuPart(string label) => Regex.Replace(label.ToUpperInvariant(), "[^A-Z0-9]+", "");

    public static async Task<string> NextNumberAsync(AppDbContext db, DateTime when)
    {
        var start = $"PK-{when.Year}-";
        var existing = await db.PackingEntries.Where(p => p.PackingNumber.StartsWith(start)).Select(p => p.PackingNumber).ToListAsync();
        var max = existing.Select(n => int.TryParse(n[start.Length..], out var k) ? k : 0).DefaultIfEmpty(0).Max();
        return $"{start}{max + 1:D4}";
    }

    public static async Task<string> NextRawCodeAsync(AppDbContext db)
    {
        var existing = await db.RawMaterials.Where(r => r.Code.StartsWith("RM-")).Select(r => r.Code).ToListAsync();
        var max = existing.Select(n => int.TryParse(n[3..], out var k) ? k : 0).DefaultIfEmpty(0).Max();
        return $"RM-{max + 1:D4}";
    }

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }
}
