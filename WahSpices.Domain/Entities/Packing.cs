using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

// ── Raw material → packing (repacking) ─────────────────────────────────────────
//
//   Purchase (GRN line with RawMaterialId)  ──►  RawMaterial.CurrentStockKg  (+ RawMaterialLedger "Purchase")
//   Packing run                             ──►  RawMaterial −kg             (RawMaterialLedger "PackingConsumption")
//                                           ──►  InventoryItem (product variant) +packets (InventoryLedger "Packing")
//   Reversal                                ──►  both sides restored (ledgers "PackingReversal")
//
// Raw stock is always stored in KILOGRAMS (decimal, 3 places = gram precision); the UI may enter g or kg.

/// <summary>Loose/bulk material bought from suppliers (e.g. Jeera 500 kg) before it is packed into retail packets.</summary>
public class RawMaterial : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;          // RM-0001 (unique)
    public string? Category { get; set; }                     // Whole spice, Powder, Blend …
    public string DisplayUnit { get; set; } = "kg";           // kg | g — how the admin prefers to see it
    public decimal CurrentStockKg { get; set; }
    public decimal ReorderLevelKg { get; set; }
    public decimal CostPerKg { get; set; }                    // weighted average of purchases / opening stock
    public string? HsnCode { get; set; }
    public string Location { get; set; } = "Warehouse A";
    /// <summary>Finished product this material is usually packed into (pre-selects it on the packing form).</summary>
    public int? DefaultProductId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public Product? DefaultProduct { get; set; }
    public ICollection<RawMaterialLedger> Ledgers { get; set; } = new List<RawMaterialLedger>();
}

/// <summary>Every raw-material stock movement. QuantityKg is signed: + in, − out.</summary>
public class RawMaterialLedger : BaseEntity
{
    public int RawMaterialId { get; set; }
    /// <summary>Opening | Purchase | Adjustment | Damage | PackingConsumption | PackingReversal</summary>
    public string Type { get; set; } = "Adjustment";
    public decimal QuantityKg { get; set; }
    public decimal BalanceAfterKg { get; set; }
    public decimal? UnitCostPerKg { get; set; }
    public string? ReferenceType { get; set; }               // GRN | Packing
    public string? ReferenceId { get; set; }                 // GRN-2026-0001 / PK-2026-0001
    public string? BatchNumber { get; set; }
    public string? Reason { get; set; }
    public string PerformedBy { get; set; } = string.Empty;

    public RawMaterial RawMaterial { get; set; } = null!;
}

/// <summary>One packing run: X kg of a raw material packed into packets of one or more pack sizes.</summary>
public class PackingEntry : BaseEntity
{
    public string PackingNumber { get; set; } = string.Empty; // PK-2026-0001
    public int RawMaterialId { get; set; }
    public decimal RawUsedKg { get; set; }                    // deducted from raw stock
    public decimal PackedKg { get; set; }                     // Σ packets × pack weight
    public decimal WastageKg { get; set; }                    // declared loss (spillage, sieving, samples …)
    public decimal VarianceKg { get; set; }                   // RawUsed − Packed − Wastage (unexplained difference)
    public decimal CostPerKg { get; set; }                    // raw cost snapshot at packing time
    public DateTime PackedOn { get; set; } = DateTime.UtcNow;
    public string? BatchNumber { get; set; }
    public string? PackedBy { get; set; }                     // who physically packed (free text)
    public string? Notes { get; set; }
    public string Status { get; set; } = "Completed";         // Completed | Reversed
    public DateTime? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }

    public RawMaterial RawMaterial { get; set; } = null!;
    public ICollection<PackingEntryLine> Lines { get; set; } = new List<PackingEntryLine>();
}

/// <summary>Packets of one pack size produced by a packing run (credited to that variant's InventoryItem).</summary>
public class PackingEntryLine : BaseEntity
{
    public int PackingEntryId { get; set; }
    public int ProductId { get; set; }
    public int? VariantId { get; set; }
    public string PackLabel { get; set; } = string.Empty;     // "1kg", "500g"
    public int PackWeightGrams { get; set; }
    public int Packets { get; set; }
    public decimal TotalKg { get; set; }
    public int InventoryItemId { get; set; }

    public PackingEntry PackingEntry { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? Variant { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;
}
