using WahSpices.Domain.Common;
using WahSpices.Domain.Enums;

namespace WahSpices.Domain.Entities;

// ── Inventory ────────────────────────────────────────
public class InventoryItem : BaseEntity
{
    public int ProductId { get; set; }
    /// <summary>
    /// Pack-size row: finished-goods stock of one ProductVariant (e.g. Jeera 1kg packets), filled by packing runs.
    /// Null = product-level row (legacy / products without variants). A product may have one product-level row
    /// plus one row per variant; the website's Product.InStock uses the sum of all its rows, ProductVariant.InStock
    /// follows its own row (see ProductStockSync).
    /// </summary>
    public int? VariantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int ReorderPoint { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public string Location { get; set; } = "Warehouse A";
    public decimal CostPrice { get; set; }
    public string Unit { get; set; } = "pcs";

    public Product Product { get; set; } = null!;
    public ProductVariant? Variant { get; set; }
    public ICollection<InventoryLedger> Ledgers { get; set; } = new List<InventoryLedger>();
}

public class InventoryLedger : BaseEntity
{
    public int InventoryItemId { get; set; }
    public StockAdjustmentType Type { get; set; }
    public int Quantity { get; set; }
    public int BalanceAfter { get; set; }
    public string? Reason { get; set; }
    public string? BatchNumber { get; set; }
    public string? ReferenceId { get; set; }  // GRN ID / Order ID
    public string PerformedBy { get; set; } = string.Empty;

    public InventoryItem InventoryItem { get; set; } = null!;
}

// ── Purchase ─────────────────────────────────────────
public class Vendor : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Gstin { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Address { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankName { get; set; }
    public decimal Rating { get; set; } = 5;
    public decimal OutstandingDues { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

public class PurchaseOrder : BaseEntity
{
    public string PoNumber { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public decimal TotalAmount { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string? Items { get; set; }   // JSON
    public string? Notes { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsGrnDone { get; set; } = false;

    public Vendor Vendor { get; set; } = null!;
    public ICollection<PurchaseOrderItem> LineItems { get; set; } = new List<PurchaseOrderItem>();
}

public class PurchaseOrderItem : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal TotalAmount { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

public class GoodsReceiptNote : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public string GrnNumber { get; set; } = string.Empty;
    public string? VendorInvoiceNo { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal TaxableAmount { get; set; } = 0;
    public decimal CgstAmount { get; set; } = 0;
    public decimal SgstAmount { get; set; } = 0;
    public decimal IgstAmount { get; set; } = 0;
    public string GstType { get; set; } = "IntraState";
    public bool AutoItcCreated { get; set; } = false;
    public string? Notes { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public ICollection<GrnItem> Items { get; set; } = new List<GrnItem>();
}

public class GrnItem : BaseEntity
{
    public int GrnId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public int OrderedQty { get; set; }
    public int ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = "kg";
    public int? InventoryItemId { get; set; }
    /// <summary>Raw material received on this line (stock goes to RawMaterial.CurrentStockKg instead of finished goods).</summary>
    public int? RawMaterialId { get; set; }
    public decimal GstRate { get; set; } = 0;

    public GoodsReceiptNote Grn { get; set; } = null!;
    public virtual InventoryItem? InventoryItem { get; set; }
}

public class VendorPayment : BaseEntity
{
    public int VendorId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public string PaymentRef { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Mode { get; set; } = "NEFT";
    public string? ReferenceNo { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public Vendor Vendor { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
}

public class VendorPriceHistory : BaseEntity
{
    public int VendorId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = "kg";
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    public Vendor Vendor { get; set; } = null!;
}

public class PurchaseReturn : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Notes { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

// ── Production / Manufacturing ────────────────────────
public class BomRecipe : BaseEntity
{
    public string ProductName { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public string? Description { get; set; }
    public decimal ExpectedYield { get; set; } = 100;

    public Product? Product { get; set; }
    public ICollection<BomIngredient> Ingredients { get; set; } = new List<BomIngredient>();
}

public class BomIngredient : BaseEntity
{
    public int BomRecipeId { get; set; }
    public string RawMaterialName { get; set; } = string.Empty;
    public decimal QuantityPer { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal? CostPerUnit { get; set; }

    public BomRecipe BomRecipe { get; set; } = null!;
}

public class ProductionOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public int? BomRecipeId { get; set; }
    public decimal TargetQuantity { get; set; }
    public decimal ActualQuantity { get; set; } = 0;
    public decimal WastageQuantity { get; set; } = 0;
    public string Unit { get; set; } = "kg";
    public string Shift { get; set; } = "Morning";
    public DateTime PlannedDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "Planned";
    public decimal TotalCost { get; set; } = 0;
    public decimal CostPerUnit { get; set; } = 0;
    public decimal YieldPercentage { get; set; } = 0;
    public string? Notes { get; set; }

    public Product? Product { get; set; }
    public BomRecipe? BomRecipe { get; set; }
    public ICollection<ProductionBatch> Batches { get; set; } = new List<ProductionBatch>();
    public ICollection<QualityCheck> QualityChecks { get; set; } = new List<QualityCheck>();
    public ICollection<WastageRecord> WastageRecords { get; set; } = new List<WastageRecord>();
}

public class ProductionBatch : BaseEntity
{
    public int ProductionOrderId { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public DateTime ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public string? FssaiLicenseNo { get; set; }
    public string Status { get; set; } = "Active";

    public ProductionOrder ProductionOrder { get; set; } = null!;
}

public class QualityCheck : BaseEntity
{
    public int ProductionOrderId { get; set; }
    public string CheckedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Remarks { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    public ProductionOrder ProductionOrder { get; set; } = null!;
}

public class WastageRecord : BaseEntity
{
    public int ProductionOrderId { get; set; }
    public decimal WastageQty { get; set; }
    public string Unit { get; set; } = "kg";
    public string Reason { get; set; } = string.Empty;
    public string? Stage { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public ProductionOrder ProductionOrder { get; set; } = null!;
}

// ── Billing ──────────────────────────────────────────
public class Invoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty; // WAH/2026/049
    public int? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerGstin { get; set; }
    public string? BillingAddress { get; set; }   // JSON
    public string? ShippingAddress { get; set; }  // JSON
    public InvoiceType Type { get; set; } = InvoiceType.B2C;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DeliveryCharge { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public GSTType GstType { get; set; } = GSTType.IntraState;
    public string? Irn { get; set; }
    public string? IrnAckNo { get; set; }
    public DateTime? IrnAckDate { get; set; }
    public string? QrCodeUrl { get; set; }
    public string? PdfUrl { get; set; }
    public string? Items { get; set; }  // legacy JSON (kept for compat)
    public string? Notes { get; set; }
    public DateTime? InvoiceDate { get; set; }        // printed date (manual invoices may differ from CreatedAt)
    public string? PaymentMethod { get; set; }        // "Cash on Delivery", "UPI", "Cash", …
    public string? PlaceOfSupply { get; set; }        // official state name of the buyer
    public decimal CouponDiscount { get; set; }       // part of DiscountAmount that came from a coupon
    public decimal RoundOff { get; set; }
    public string? SignedQrCode { get; set; }         // e-invoice signed QR (from the IRP)
    public string Source { get; set; } = "Online";    // Online (website order) | Offline (created in admin)
    public DateTime? LastSentAt { get; set; }
    public string? LastSentTo { get; set; }
    public int SentCount { get; set; }

    public Order? Order { get; set; }
    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}

public class InvoiceLineItem : BaseEntity
{
    public int InvoiceId { get; set; }
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public int Quantity { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal UnitPrice { get; set; }      // selling price per unit (incl GST)
    public decimal MrpPrice { get; set; }        // MRP per unit
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }   // ex-GST
    public decimal GstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public int? InventoryItemId { get; set; }
    public bool StockDeducted { get; set; } = false;

    public Invoice Invoice { get; set; } = null!;
    public Product? Product { get; set; }
}

public class InvoiceSettings : BaseEntity
{
    // Company info
    public string CompanyName { get; set; } = "WAH! Spices";
    public string? CompanyLogo { get; set; }    // URL or base64
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? Fssai { get; set; }
    // Bank details
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
    // Template
    public string PrimaryColor { get; set; } = "#1a5c2e";
    public string InvoicePrefix { get; set; } = "WAH";
    public string? TermsAndConditions { get; set; }
    public string? FooterText { get; set; }
    public bool ShowSignature { get; set; } = false;
    public bool ShowStamp { get; set; } = false;
    public bool ShowHsnColumn { get; set; } = true;
    public bool ShowDiscountColumn { get; set; } = true;
    // PDF layout / behaviour (all editable in Admin → Billing → Template)
    public string InvoiceTitle { get; set; } = "Tax Invoice";
    public string? UdyamNumber { get; set; }
    public string? UpiId { get; set; }
    public string? SignatureImage { get; set; }   // data URL or https URL
    public string? StampImage { get; set; }
    public string? AuthorisedSignatory { get; set; }
    public string? Declaration { get; set; }
    public string NumberFormat { get; set; } = "FY";   // FY → WAH/26-27/001, Year → WAH/2026/001
    public bool ShowBankDetails { get; set; } = true;
    public bool ShowUpiQr { get; set; } = true;
    public bool ShowMrpColumn { get; set; } = true;
    public bool ShowAmountInWords { get; set; } = true;
    public bool ShowHsnSummary { get; set; } = true;
    public bool AutoGenerateOnOrder { get; set; } = true;
    public bool AttachPdfToEmails { get; set; } = true;
    public bool EInvoiceEnabled { get; set; } = false;
}

// ── Staff ────────────────────────────────────────────
public class Employee : BaseEntity
{
    public string EmployeeId { get; set; } = string.Empty;  // EMP-001
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public EmploymentType EmploymentType { get; set; }
    public decimal Salary { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? ConfirmationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? AadharNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankIfsc { get; set; }
    public string? PfNumber { get; set; }
    public string? EsicNumber { get; set; }
    public string? ReportingManagerId { get; set; }
    public string? PhotoUrl { get; set; }
    public int NoticePeriodDays { get; set; } = 30;
    public string? Branch { get; set; }

    public ICollection<AttendanceRecord> Attendance { get; set; } = new List<AttendanceRecord>();
    public ICollection<LeaveApplication> LeaveApplications { get; set; } = new List<LeaveApplication>();
    public ICollection<PayrollEntry> PayrollEntries { get; set; } = new List<PayrollEntry>();
    public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    public ICollection<IncrementHistory> Increments { get; set; } = new List<IncrementHistory>();
    public ICollection<EmployeeTransfer> Transfers { get; set; } = new List<EmployeeTransfer>();
}

// ── Attendance ───────────────────────────────────────
public class AttendanceRecord : BaseEntity
{
    public int EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public decimal OvertimeHours { get; set; } = 0;
    public string? Remark { get; set; }
    public string MarkedBy { get; set; } = string.Empty;

    public Employee Employee { get; set; } = null!;
}

public class ShiftMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int LateThresholdMinutes { get; set; } = 15;
    public int OvertimeThresholdMinutes { get; set; } = 30;
    public bool IsActive { get; set; } = true;
}

public class EmployeeShift : BaseEntity
{
    public int EmployeeId { get; set; }
    public int ShiftId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public virtual Employee Employee { get; set; } = null!;
    public virtual ShiftMaster Shift { get; set; } = null!;
}

public class AttendanceRegularization : BaseEntity
{
    public int EmployeeId { get; set; }
    public DateTime AttendanceDate { get; set; }
    public string RequestedStatus { get; set; } = string.Empty;
    public string? RequestedInTime { get; set; }
    public string? RequestedOutTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RegStatus { get; set; } = "Pending";
    public string? ReviewedBy { get; set; }
    public string? ReviewRemarks { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public virtual Employee Employee { get; set; } = null!;
}

// ── Leave ────────────────────────────────────────────
public class LeaveApplication : BaseEntity
{
    public int EmployeeId { get; set; }
    public LeaveType LeaveType { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public string? ApprovedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ActionAt { get; set; }

    public Employee Employee { get; set; } = null!;
}

public class LeaveBalance : BaseEntity
{
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int CasualLeaveBalance { get; set; } = 12;
    public int EarnedLeaveBalance { get; set; } = 15;
    public int SickLeaveBalance { get; set; } = 7;
    public int MaternityLeaveBalance { get; set; } = 0;
    public int PaternityLeaveBalance { get; set; } = 0;
    public int UsedCasual { get; set; } = 0;
    public int UsedEarned { get; set; } = 0;
    public int UsedSick { get; set; } = 0;

    public Employee Employee { get; set; } = null!;
}

// ── Payroll ──────────────────────────────────────────
public class PayrollEntry : BaseEntity
{
    public int EmployeeId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public int DaysWorked { get; set; }
    public int LossOfPayDays { get; set; } = 0;
    public decimal BasicSalary { get; set; }
    public decimal Hra { get; set; }
    public decimal Ta { get; set; }
    public decimal Da { get; set; }
    public decimal Bonus { get; set; } = 0;
    public decimal OvertimePay { get; set; } = 0;
    public decimal GrossSalary { get; set; }
    public decimal Epf { get; set; }
    public decimal Esic { get; set; }
    public decimal ProfessionalTax { get; set; }
    public decimal Tds { get; set; } = 0;
    public decimal OtherDeductions { get; set; } = 0;
    public decimal NetSalary { get; set; }
    public bool IsApproved { get; set; } = false;
    public bool IsDisbursed { get; set; } = false;
    public DateTime? DisbursedAt { get; set; }
    public string? SlipUrl { get; set; }

    public Employee Employee { get; set; } = null!;
}

// ── Expense ──────────────────────────────────────────
public class ExpenseEntry : BaseEntity
{
    public DateTime Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaidBy { get; set; } = string.Empty; // PettyCash | BankTransfer | UPI
    public bool IsApproved { get; set; } = false;
    public string? ApprovedBy { get; set; }
    // Approval rule (ExpenseApproval): the admin who added an expense can't approve it — another partner does.
    public int? CreatedByAdminId { get; set; }
    public int? ApprovedByAdminId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? Notes { get; set; }
    // Company finance (CompanyFinance.cs): who paid + how the cost is split between partners
    public string PayerType { get; set; } = "Company";       // Company | Partner
    public int? PaidByPartnerId { get; set; }
    public string SplitMethod { get; set; } = "None";        // None | Share | Equal | Custom
    public string? SourceType { get; set; }                  // Asset | Licence (expense created from that record)
    public int? SourceId { get; set; }
    public List<ExpenseSplit> Splits { get; set; } = new();
}

// ── Expense Budget ────────────────────────────────────
public class ExpenseBudget : BaseEntity
{
    public string Category { get; set; } = string.Empty;
    public decimal MonthlyLimit { get; set; }
}

// ── Expense Category Config ───────────────────────────
public class ExpenseCategoryConfig : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "gray"; // blue | green | red | yellow | purple | orange | pink | indigo | gray | teal
    public bool IsActive { get; set; } = true;
}

// ── GST ──────────────────────────────────────────────
public class HsnCode : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal GstRate { get; set; }
    public decimal CessRate { get; set; } = 0;
    public bool IsExportLut { get; set; } = false;
    public bool IsActive { get; set; } = true;
}

public class ItcEntry : BaseEntity
{
    public string Period { get; set; } = string.Empty; // Jun 2026
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierGstin { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal TaxableAmount { get; set; }
    public decimal IgstAmount { get; set; } = 0;
    public decimal CgstAmount { get; set; } = 0;
    public decimal SgstAmount { get; set; } = 0;
    public decimal TotalItc { get; set; }
    public bool IsEligible { get; set; } = true;
    public string SourceType { get; set; } = "Manual"; // Manual | GRN
    public int? SourceId { get; set; }
}

// ── CRM ──────────────────────────────────────────────
public class CrmCustomer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Category { get; set; } = "Retail"; // Wholesale | Retail | Ecom | Distributor
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public decimal CreditLimit { get; set; } = 0;
    public decimal OutstandingDues { get; set; } = 0;
    public DateTime? DuesSince { get; set; }
    public bool IsBlocked { get; set; } = false;
    public string? BlockReason { get; set; }
    public int LoyaltyPoints { get; set; } = 0;
    public string LoyaltyTier { get; set; } = "Bronze"; // Bronze | Silver | Gold | Platinum
    public bool IsActive { get; set; } = true;
    public DateTime? LastOrderDate { get; set; }
    public DateTime? BirthDate { get; set; }
    public DateTime? AnniversaryDate { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public virtual ICollection<CrmPayment> Payments { get; set; } = new List<CrmPayment>();
    public virtual ICollection<CrmPriceRule> PriceRules { get; set; } = new List<CrmPriceRule>();
}

public class CrmPayment : BaseEntity
{
    public int CrmCustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string Mode { get; set; } = "Cash"; // Cash | UPI | NEFT | Cheque | Card
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public virtual CrmCustomer Customer { get; set; } = null!;
}

public class CrmPriceRule : BaseEntity
{
    public int CrmCustomerId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public decimal CustomPrice { get; set; }
    public decimal DiscountPercent { get; set; } = 0;
    public virtual CrmCustomer Customer { get; set; } = null!;
}

// ── Roles ─────────────────────────────────────────────
public class AdminRole : BaseEntity
{
    public string Name { get; set; } = string.Empty;          // "Super Admin", "Manager"
    public string? Description { get; set; }
    public string Color { get; set; } = "bg-gray-100 text-gray-700"; // Tailwind badge class
    public string? Permissions { get; set; }                  // JSON: { "Dashboard": true, ... }
    public bool IsSystem { get; set; } = false;               // system roles can't be deleted
    public bool IsActive { get; set; } = true;

    public ICollection<AdminUser> Users { get; set; } = new List<AdminUser>();
}

// ── Access Control ───────────────────────────────────
public class AdminUser : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Manager";            // kept for display / JWT claim
    public int? AdminRoleId { get; set; }                    // FK to AdminRoles
    public string? Permissions { get; set; }                  // user-level overrides JSON
    public bool IsActive { get; set; } = true;
    public bool IsTwoFactorEnabled { get; set; } = false;
    /// <summary>Set when an admin creates the account or resets its password: the next sign-in must choose a new
    /// password before any session is issued. Cleared once the user sets their own password.</summary>
    public bool MustChangePassword { get; set; } = false;
    public string? TotpSecret { get; set; }
    public string? TotpBackupCodes { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; } = 0;
    public bool IsLocked { get; set; } = false;
    public DateTime? LockedUntil { get; set; }

    public AdminRole? AdminRole { get; set; }
    public ICollection<AdminSession> Sessions { get; set; } = new List<AdminSession>();
}

public class AdminSession : BaseEntity
{
    public int AdminUserId { get; set; }
    public string JwtJti { get; set; } = string.Empty;  // JWT ID for revocation
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Location { get; set; }          // rough geo: "Mumbai, IN"
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;

    public AdminUser Admin { get; set; } = null!;
}

public class AuditLog : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class Holiday : BaseEntity
{
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "National"; // National | Optional | Festival
    public int Year { get; set; }
}

public class OtpRecord : BaseEntity
{
    public string Phone { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string Purpose { get; set; } = "login";
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
}

public class NewsletterSubscriber : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? CouponCode { get; set; }
}

public class ContactMessage : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public bool IsResolved { get; set; } = false;
    public string Status { get; set; } = "New";          // New | InProgress | Resolved | Spam
    public string? AdminReply { get; set; }
    public DateTime? RepliedAt { get; set; }
    public string? RepliedBy { get; set; }
    public int? UserId { get; set; }

    /// <summary>Every email reply the team has sent for this message (AdminReply/RepliedAt keep the latest one).</summary>
    public ICollection<ContactMessageReply> Replies { get; set; } = new List<ContactMessageReply>();
}

/// <summary>One email reply sent from Admin → Contact Messages to the person who wrote in.</summary>
public class ContactMessageReply : BaseEntity
{
    public int ContactMessageId { get; set; }
    public ContactMessage ContactMessage { get; set; } = null!;
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? SentBy { get; set; }
}

public class Feedback : BaseEntity
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Product { get; set; }
    public int Rating { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Recommend { get; set; }
    public string Status { get; set; } = "Pending";      // Pending | Approved | Rejected
    public bool IsFeatured { get; set; }                 // shown as a testimonial on the website
    public string? AdminReply { get; set; }
    public DateTime? RepliedAt { get; set; }
    public string? RepliedBy { get; set; }
    public int? UserId { get; set; }
}

// ── Site Settings (single row, Id = 1) ───────────────
public class SiteSettings : BaseEntity
{
    // General
    public string SiteName { get; set; } = "Wah Spices";
    public string Tagline { get; set; } = "Pehle bite mein — WAH!";
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Gstin { get; set; }
    public string? Fssai { get; set; }
    public string? UdyamNumber { get; set; }   // MSME Udyam registration, e.g. UDYAM-GJ-01-0646120
    // Shipping
    public decimal FreeDeliveryAbove { get; set; } = 499;
    public decimal DeliveryCharge { get; set; } = 49;
    public string AvgDeliveryDays { get; set; } = "3-5";
    public bool CodAvailable { get; set; } = true;
    // Notifications
    public bool OrderEmails { get; set; } = true;
    public bool LowStockAlerts { get; set; } = true;
    public bool ReviewAlerts { get; set; } = false;
    public bool WeeklyReport { get; set; } = true;
    // Other
    public bool MaintenanceMode { get; set; } = false;
}

// ── HR Masters ───────────────────────────────────────
public class DepartmentMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DesignationMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public int Level { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class SalaryStructure : BaseEntity
{
    public int EmployeeId { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal HraPercent { get; set; } = 40;
    public decimal TaAmount { get; set; } = 0;
    public decimal DaPercent { get; set; } = 0;
    public decimal SpecialAllowance { get; set; } = 0;
    public decimal PfPercent { get; set; } = 12;
    public decimal EsicPercent { get; set; } = 0.75m;
    public decimal ProfessionalTax { get; set; } = 200;
    public decimal GrossSalary { get; set; }
    public decimal NetSalary { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public bool IsActive { get; set; } = true;
    public virtual Employee Employee { get; set; } = null!;
}

public class IncrementHistory : BaseEntity
{
    public int EmployeeId { get; set; }
    public decimal OldSalary { get; set; }
    public decimal NewSalary { get; set; }
    public decimal IncrementPercent { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public virtual Employee Employee { get; set; } = null!;
}

public class EmployeeDocument : BaseEntity
{
    public int EmployeeId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public string? FileUrl { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsVerified { get; set; } = false;
    public string? Notes { get; set; }
    public virtual Employee Employee { get; set; } = null!;
}

public class EmployeeTransfer : BaseEntity
{
    public int EmployeeId { get; set; }
    public string FromDepartment { get; set; } = string.Empty;
    public string ToDepartment { get; set; } = string.Empty;
    public string? FromBranch { get; set; }
    public string? ToBranch { get; set; }
    public string? FromDesignation { get; set; }
    public string? ToDesignation { get; set; }
    public DateTime TransferDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public virtual Employee Employee { get; set; } = null!;
}

// ── Field Sales ───────────────────────────────────────
public class RetailClient : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? GstNumber { get; set; }
    public string ClientType { get; set; } = "Retailer"; // Retailer, Wholesaler, Distributor
    public decimal CreditLimit { get; set; } = 0;
    public decimal CurrentOutstanding { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public ICollection<SalesOrder> Orders { get; set; } = new List<SalesOrder>();
    public ICollection<SalesOrderBill> Bills { get; set; } = new List<SalesOrderBill>();
}

public class SalesOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public string SalespersonName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, Confirmed, Dispatched, Delivered, Billed, Cancelled
    public decimal SubTotal { get; set; } = 0;
    public decimal GstAmount { get; set; } = 0;
    public decimal TotalAmount { get; set; } = 0;
    public string? Notes { get; set; }
    public int? BillId { get; set; }
    public bool IsBilled { get; set; } = false;
    public virtual RetailClient Client { get; set; } = null!;
    public virtual SalesOrderBill? Bill { get; set; }
    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
}

public class SalesOrderItem : BaseEntity
{
    public int SalesOrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; } = 0;
    public decimal GstPercent { get; set; } = 0;
    public decimal TotalAmount { get; set; } = 0;
    public virtual SalesOrder SalesOrder { get; set; } = null!;
}

public class SalesOrderBill : BaseEntity
{
    public string BillNumber { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public DateTime BillDate { get; set; }
    public decimal SubTotal { get; set; } = 0;
    public decimal GstAmount { get; set; } = 0;
    public decimal TotalAmount { get; set; } = 0;
    public decimal PaidAmount { get; set; } = 0;
    public decimal BalanceAmount { get; set; } = 0;
    public string PaymentStatus { get; set; } = "Unpaid"; // Unpaid, PartialPaid, Paid
    public string? Notes { get; set; }
    public virtual RetailClient Client { get; set; } = null!;
    public ICollection<SalesOrder> Orders { get; set; } = new List<SalesOrder>();
}

// ── Barcode ───────────────────────────────────────────
public class BarcodeLog : BaseEntity
{
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string BarcodeValue { get; set; } = string.Empty;
    public string BarcodeType { get; set; } = "Code128"; // Code128, EAN13, QR
    public string Action { get; set; } = "Scan";         // Scan, Generate, Print
    public int Quantity { get; set; } = 1;
    public string? Notes { get; set; }
    public virtual Product? Product { get; set; }
}
