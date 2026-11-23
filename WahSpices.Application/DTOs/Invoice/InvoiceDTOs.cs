using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Invoice;

// ── Requests ─────────────────────────────────────────────────────────────────

public record CreateInvoiceRequest(
    int? OrderId,
    string? CustomerName,  // nullable so the service returns a readable validation message
    string? CustomerEmail,
    string? CustomerPhone,
    string? CustomerGstin,
    string? Type,       // "B2C" | "B2B"
    string? GstType,    // "IntraState" | "InterState" | "Export"
    string? BillingAddress,
    string? ShippingAddress,
    List<CreateInvoiceLineItemRequest>? Items,
    decimal DeliveryCharge,
    string? Notes,
    string? PlaceOfSupply = null,   // buyer's state (name or GST code) — decides CGST+SGST vs IGST when GstType is empty
    DateTime? InvoiceDate = null,
    string? PaymentMethod = null,
    string? Status = null,          // "Paid" | "Unpaid" (default)
    bool SendEmail = false          // email the PDF to CustomerEmail after saving
);

public record CreateInvoiceLineItemRequest(
    int? ProductId,
    string? ProductName,
    string? HsnCode,
    int Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal MrpPrice,
    decimal DiscountPercent,
    decimal GstRate,
    int? InventoryItemId = null,
    int? VariantId = null
);

public record SendInvoiceRequest(
    string Via,       // "email" | "whatsapp"
    string? Email,
    string? Phone,
    string? BaseUrl,  // frontend base URL for invoice link
    string? Message = null   // optional personal note shown in the email
);

public record UpdateEInvoiceRequest(string? Irn, string? AckNo, DateTime? AckDate, string? SignedQrCode);

public record UpdateInvoiceSettingsRequest(
    string? CompanyName,
    string? CompanyLogo,
    string? Address,
    string? City,
    string? State,
    string? Pincode,
    string? Phone,
    string? Email,
    string? Website,
    string? Gstin,
    string? Pan,
    string? Fssai,
    string? BankName,
    string? BankAccount,
    string? BankIfsc,
    string? BankBranch,
    string? PrimaryColor,
    string? InvoicePrefix,
    string? TermsAndConditions,
    string? FooterText,
    bool ShowSignature,
    bool ShowStamp,
    bool ShowHsnColumn,
    bool ShowDiscountColumn,
    // GST state code (e.g. "24" = Gujarat). No column of its own: it is the official code of State, so sending
    // stateCode alone sets State, and a stateCode that contradicts State is rejected.
    string? StateCode = null,
    string? InvoiceTitle = null,
    string? UdyamNumber = null,
    string? UpiId = null,
    string? SignatureImage = null,
    string? StampImage = null,
    string? AuthorisedSignatory = null,
    string? Declaration = null,
    string? NumberFormat = null,
    bool ShowBankDetails = true,
    bool ShowUpiQr = true,
    bool ShowMrpColumn = true,
    bool ShowAmountInWords = true,
    bool ShowHsnSummary = true,
    bool AutoGenerateOnOrder = true,
    bool AttachPdfToEmails = true,
    bool EInvoiceEnabled = false
);

public class InvoiceFilterParams
{
    public int Page { get; init; } = 1;
    public int Limit { get; init; } = 20;
    public string? Status { get; init; }
    public string? Type { get; init; }
    public string? Search { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}

// ── Responses ─────────────────────────────────────────────────────────────────

public record InvoiceSummaryDto(
    int Total,
    decimal TotalBilled,        // excludes cancelled
    decimal TotalGst,           // excludes cancelled
    int PaidCount,
    int UnpaidCount,            // Unpaid + PartiallyPaid + Overdue
    decimal UnpaidAmount,
    Dictionary<string, int> ByStatus
);

public record InvoiceListItemDto(
    int Id,
    string InvoiceNumber,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    InvoiceType Type,
    InvoiceStatus Status,
    decimal Subtotal,
    decimal TotalAmount,
    decimal GstAmount,
    DateTime CreatedAt,
    int? OrderId,
    string? OrderNumber,
    int ItemCount,
    string Source = "Online",
    DateTime? LastSentAt = null,
    int SentCount = 0,
    bool HasIrn = false,
    string? LastSentTo = null
);

public record InvoiceLineItemDto(
    int Id,
    string ProductName,
    string? HsnCode,
    int Quantity,
    string Unit,
    decimal UnitPrice,
    decimal MrpPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxableAmount,
    decimal GstRate,
    decimal CgstRate,
    decimal SgstRate,
    decimal IgstRate,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TaxAmount,
    decimal LineTotal
);

public record InvoiceDto(
    int Id,
    string InvoiceNumber,
    DateTime CreatedAt,
    int? OrderId,
    string? OrderNumber,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? CustomerGstin,
    string? BillingAddress,
    string? ShippingAddress,
    InvoiceType Type,
    InvoiceStatus Status,
    GSTType GstType,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal DeliveryCharge,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TotalAmount,
    string? Irn,
    string? Notes,
    List<InvoiceLineItemDto> LineItems,
    InvoiceSettingsDto Settings,
    DateTime? InvoiceDate = null,
    string? PaymentMethod = null,
    string? PlaceOfSupply = null,
    decimal CouponDiscount = 0,
    decimal RoundOff = 0,
    string? IrnAckNo = null,
    DateTime? IrnAckDate = null,
    string? SignedQrCode = null,
    string Source = "Online",
    DateTime? LastSentAt = null,
    string? LastSentTo = null,
    int SentCount = 0
);

public record InvoiceSettingsDto(
    int Id,
    string CompanyName,
    string? CompanyLogo,
    string Address,
    string City,
    string State,
    string Pincode,
    string Phone,
    string Email,
    string? Website,
    string? Gstin,
    string? Pan,
    string? Fssai,
    string? BankName,
    string? BankAccount,
    string? BankIfsc,
    string? BankBranch,
    string? PrimaryColor,
    string? InvoicePrefix,
    string? TermsAndConditions,
    string? FooterText,
    bool ShowSignature,
    bool ShowStamp,
    bool ShowHsnColumn,
    bool ShowDiscountColumn,
    string? StateCode = null,     // "24" — derived from State (GstStates)
    string? PlaceOfSupply = null, // "24-Gujarat"
    string InvoiceTitle = "Tax Invoice",
    string? UdyamNumber = null,
    string? UpiId = null,
    string? SignatureImage = null,
    string? StampImage = null,
    string? AuthorisedSignatory = null,
    string? Declaration = null,
    string NumberFormat = "FY",
    bool ShowBankDetails = true,
    bool ShowUpiQr = true,
    bool ShowMrpColumn = true,
    bool ShowAmountInWords = true,
    bool ShowHsnSummary = true,
    bool AutoGenerateOnOrder = true,
    bool AttachPdfToEmails = true,
    bool EInvoiceEnabled = false
);

public record UpdateInvoiceStatusRequest(string? Status); // "Paid" | "Unpaid" | "PartiallyPaid" | "Overdue" | "Cancelled"

public record SendInvoiceResultDto(
    bool Success,
    string Via,
    string? WhatsAppUrl,
    string? Message
);
