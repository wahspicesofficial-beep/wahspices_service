using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Return;

// ── Requests ──────────────────────────────────────────────────────────────────

public class InitiateReturnRequest
{
    public int OrderId { get; set; }
    public ReturnType Type { get; set; } = ReturnType.Refund;
    public string Reason { get; set; } = string.Empty;
    public List<ReturnItemInput> Items { get; set; } = new();
    public RefundMethod PreferredRefundMethod { get; set; } = RefundMethod.OriginalPaymentMethod;
}

public class ReturnItemInput
{
    public int OrderItemId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ReturnItemCondition Condition { get; set; } = ReturnItemCondition.Unopened;
}

public class ApproveReturnRequest
{
    public string? AdminNotes { get; set; }
    public GSTType? GstTypeOverride { get; set; }   // Override auto-detected GST type
    public bool IncludeTcs { get; set; } = false;
    public decimal TcsRate { get; set; } = 1m;
    public string? PickupAddress { get; set; }
    public DateTime? PickupScheduledAt { get; set; }
}

public class ProcessRefundRequest
{
    public RefundMethod RefundMethod { get; set; }
    public string? RefundReferenceId { get; set; }
    public string? Notes { get; set; }
}

public class ReturnFilterRequest
{
    public ReturnStatus? Status { get; set; }
    public string? Search { get; set; }             // Order number / return number
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 20;
}

// ── Responses ─────────────────────────────────────────────────────────────────

public class ReturnSummaryDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public ReturnStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public ReturnType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal GrandRefundAmount { get; set; }
    public string? CreditNoteNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RefundCompletedAt { get; set; }
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public RefundMethod RefundMethod { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public int PhotoCount { get; set; }
}

public class ReturnStatsDto
{
    public int Total { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public int Open { get; set; }                 // Requested → Received (not yet refunded/rejected/cancelled)
    public int AwaitingAction { get; set; }       // Requested (needs approve / reject)
    public int AwaitingRefund { get; set; }       // Received / QualityCheck / Processed
    public decimal RefundedAmount { get; set; }
    public decimal PendingRefundAmount { get; set; }
}

public class ReturnDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public ReturnStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public ReturnType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public string? RejectionReason { get; set; }

    // Logistics
    public string? PickupAddress { get; set; }
    public string? CourierName { get; set; }
    public string? AwbNumber { get; set; }
    public DateTime? PickupScheduledAt { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Tax breakdown
    public ReturnTaxDto TaxBreakdown { get; set; } = new();

    // Refund info
    public RefundMethod RefundMethod { get; set; }
    public string? RefundReferenceId { get; set; }
    public DateTime? RefundInitiatedAt { get; set; }
    public DateTime? RefundCompletedAt { get; set; }

    // Items
    public List<ReturnItemDto> Items { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    // Customer + order context (admin detail / customer page)
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public DateTime? OrderDeliveredAt { get; set; }
    /// <summary>Admin actions allowed from the current status (enforced flow): approve, reject, pickup, received, refund.</summary>
    public List<string> NextActions { get; set; } = new();
    /// <summary>Customer photos: API paths (relative to /api) that need the caller's token — never public URLs.</summary>
    public List<ReturnPhotoDto> Photos { get; set; } = new();
}

public class ReturnPhotoDto
{
    public int Index { get; set; }
    public string Url { get; set; } = string.Empty;   // e.g. "/returns/12/photos/0" or "/admin/returns/12/photos/0"
}

public class ReturnItemDto
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitMrp { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GstRate { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal LineTotalWithTax { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    public ReturnItemCondition Condition { get; set; }
    public bool IsApproved { get; set; }
}

// ── Tax breakdown DTO (shown on return details & credit note) ─────────────────

public class ReturnTaxDto
{
    public GSTType GstType { get; set; }
    public string GstTypeLabel { get; set; } = string.Empty;
    public TaxPriceType PriceType { get; set; }

    public decimal SubtotalMrp { get; set; }
    public decimal SubtotalSelling { get; set; }        // Total paid (GST-inclusive selling price)
    public decimal TotalTaxableValue { get; set; }      // Ex-GST taxable amount

    // GST on credit note (reversed from original)
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalGstReversed { get; set; }

    // TCS (Section 52 — returned if applicable)
    public decimal TcsRate { get; set; }
    public decimal TcsAmount { get; set; }

    // Delivery charge: refunded only when every item of the order is returned (full return)
    public bool IsFullReturn { get; set; }
    public decimal OrderDeliveryCharge { get; set; }
    public decimal DeliveryChargeRefund { get; set; }
    public decimal CouponDiscountDeducted { get; set; }  // full return: refund is capped at what was actually paid
    public string DeliveryNote { get; set; } = string.Empty;

    // Final amounts
    public decimal CreditNoteAmount { get; set; }       // TaxableValue + GST
    public decimal GrandRefundAmount { get; set; }      // What customer actually gets back

    // Credit note reference
    public string? CreditNoteNumber { get; set; }
    public DateTime? CreditNoteDate { get; set; }

    // Formula explanation (for transparency)
    public List<TaxFormulaLine> FormulaBreakdown { get; set; } = new();
}

public class TaxFormulaLine
{
    public string Label { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

// ── Tax preview (before submitting return) ────────────────────────────────────

public class ReturnTaxPreviewDto
{
    public string OrderNumber { get; set; } = string.Empty;
    public List<ReturnItemPreviewDto> Items { get; set; } = new();
    public ReturnTaxDto TaxSummary { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class ReturnItemPreviewDto
{
    public int OrderItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GstRate { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal TotalTax { get; set; }
    public decimal RefundableAmount { get; set; }
}
