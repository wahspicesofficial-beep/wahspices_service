using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

/// <summary>
/// Admin-editable lookup values (dropdown lists) — e.g. inventory locations, units,
/// recipe categories, payment modes. Grouped by <see cref="Type"/>; <see cref="Value"/> is
/// what gets stored on records, <see cref="Label"/> is what the UI shows.
/// </summary>
public class MasterDataValue : BaseEntity
{
    public string Type { get; set; } = string.Empty;     // e.g. "InventoryLocation"
    public string Value { get; set; } = string.Empty;    // stored value, e.g. "Warehouse A"
    public string Label { get; set; } = string.Empty;    // display text
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>System values back business logic (or seeded defaults) and can't be deleted, only hidden.</summary>
    public bool IsSystem { get; set; }
}

/// <summary>Log of GST returns marked as filed from the admin (GSTR-1 / GSTR-3B).</summary>
public class GstReturnFiling : BaseEntity
{
    public string ReturnType { get; set; } = "GSTR3B";   // GSTR1 | GSTR3B
    public int Month { get; set; }
    public int Year { get; set; }
    public string Status { get; set; } = "Filed";        // Filed | Draft
    public DateTime FiledOn { get; set; } = DateTime.UtcNow;
    public string? Arn { get; set; }                     // acknowledgement reference from the GST portal
    public decimal TaxableValue { get; set; }
    public decimal TotalTax { get; set; }
    public decimal ItcClaimed { get; set; }
    public decimal NetPayable { get; set; }
    public string? Notes { get; set; }
    public string? FiledBy { get; set; }
}

/// <summary>
/// Admin-editable website content stored as JSON per section (header, footer, contact page,
/// help/FAQ, about, policies, trust badges …). Public sections are served by GET /api/site-content.
/// </summary>
public class SiteContentBlock : BaseEntity
{
    public string Key { get; set; } = string.Empty;      // e.g. "header", "footer", "contact"
    public string Json { get; set; } = "{}";
    public bool IsPublic { get; set; } = true;
}
