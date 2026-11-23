using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

// ═══════════════════════════════════════════════════════════════════════════
// Company finance & compliance: partners, expense splits, partner ledger,
// assets register, licences/registrations and the alert log.
// ExpenseEntry (ERP.cs) carries PayerType / PaidByPartnerId / SplitMethod / SourceType / SourceId.
// ═══════════════════════════════════════════════════════════════════════════

public static class CompanyPayer
{
    public const string Company = "Company";   // paid from the company account (cash / bank / UPI = ExpenseEntry.PaidBy)
    public const string Partner = "Partner";   // a partner paid with personal money (ExpenseEntry.PaidByPartnerId)
}

public static class ExpenseSplitMethod
{
    public const string None   = "None";    // company expense — no split between partners
    public const string Share  = "Share";   // by partners' profit/loss share %
    public const string Equal  = "Equal";   // equally between active partners
    public const string Custom = "Custom";  // custom amounts / percents per partner
    public static readonly string[] All = { None, Share, Equal, Custom };
}

/// <summary>A business partner (profit/loss share %).</summary>
public class Partner : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal SharePercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? JoinedOn { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Per-partner share of one expense. PartnerId null = borne by the company itself (no split).</summary>
public class ExpenseSplit : BaseEntity
{
    public int ExpenseEntryId { get; set; }
    public int? PartnerId { get; set; }
    public decimal Percent { get; set; }
    public decimal Amount { get; set; }

    public ExpenseEntry ExpenseEntry { get; set; } = null!;
    public Partner? Partner { get; set; }
}

public static class PartnerTxnType
{
    public const string Contribution = "Contribution";  // partner → company (capital in)
    public const string Withdrawal   = "Withdrawal";    // company → partner (drawings)
    public const string Settlement   = "Settlement";    // partner ↔ partner / partner ↔ company (squaring up expense shares)
    public static readonly string[] All = { Contribution, Withdrawal, Settlement };
}

/// <summary>Money moving between partners and the company. A null party = the company.</summary>
public class PartnerTransaction : BaseEntity
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = PartnerTxnType.Contribution;
    public int? FromPartnerId { get; set; }
    public int? ToPartnerId { get; set; }
    public decimal Amount { get; set; }
    public string? Mode { get; set; }        // Cash / UPI / Bank …
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public Partner? FromPartner { get; set; }
    public Partner? ToPartner { get; set; }
}

public static class AssetStatus
{
    public const string InUse = "InUse", UnderRepair = "UnderRepair", Disposed = "Disposed", Lost = "Lost";
    public static readonly string[] All = { InUse, UnderRepair, Disposed, Lost };
}

/// <summary>Company assets register (printer, stamps/seals, furniture, computers, machines …).</summary>
public class CompanyAsset : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;    // MasterData "CompanyAssetCategory"
    public DateTime PurchaseDate { get; set; }
    public decimal Cost { get; set; }
    public string? Vendor { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? DocumentUrl { get; set; }                 // "companydoc:{file}" (App_Data, admin-only download)
    public int? ExpenseEntryId { get; set; }                 // linked purchase expense (with split)
    public string? Location { get; set; }
    public string? AssignedTo { get; set; }
    public string? SerialNumber { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public string Status { get; set; } = AssetStatus.InUse;
    public DateTime? DisposalDate { get; set; }
    public decimal? DisposalValue { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Status-change history of an asset.</summary>
public class CompanyAssetHistory : BaseEntity
{
    public int CompanyAssetId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Note { get; set; }
}

/// <summary>Licence / registration (FSSAI, GST, Udyam, trade licence …). A renewal is a new row linked to the previous one.</summary>
public class CompanyLicence : BaseEntity
{
    public string LicenceType { get; set; } = string.Empty;  // MasterData "LicenceType"
    public string Title { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? IssuingAuthority { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }                // null = lifetime (e.g. Udyam)
    public decimal Fee { get; set; }
    public int? ExpenseEntryId { get; set; }                 // fee expense (with split)
    public int ReminderDays { get; set; } = 30;
    public string? DocumentUrl { get; set; }
    public string? Notes { get; set; }
    public int? PreviousLicenceId { get; set; }              // the period this one renewed
    public int? RenewedById { get; set; }                    // set on the old period once renewed (it no longer alerts)
}

/// <summary>Alert emails already sent (one per item + expiry date + threshold), so the daily job never repeats one.</summary>
public class CompanyAlertLog : BaseEntity
{
    public string Kind { get; set; } = "Licence";            // Licence | Warranty
    public int RefId { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int Threshold { get; set; }                       // 30 / 15 / 7 / 1 days before, 0 = on/after expiry
    public DateTime SentAt { get; set; }
    public string? Recipient { get; set; }
}
