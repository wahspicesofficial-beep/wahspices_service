using WahSpices.Domain.Common;
using WahSpices.Domain.Enums;

namespace WahSpices.Domain.Entities;

public class ReturnOrder : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;   // RET/2026/001
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;
    /// <summary>JSON array of customer-uploaded photo paths (served only to the owner and admins).</summary>
    public string? PhotoUrls { get; set; }
    public ReturnType Type { get; set; } = ReturnType.Refund;
    public string Reason { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }

    // Pickup / logistics
    public string? PickupAddress { get; set; }
    public string? CourierName { get; set; }
    public string? AwbNumber { get; set; }
    public DateTime? PickupScheduledAt { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // ── Tax calculations (auto-populated on Approve) ──────────────────────────
    public GSTType GstType { get; set; } = GSTType.IntraState;
    public TaxPriceType PriceType { get; set; } = TaxPriceType.Inclusive;

    public decimal SubtotalMrp { get; set; }          // Sum of MRP × qty
    public decimal SubtotalSelling { get; set; }       // Sum of selling price × qty (paid amount)

    public decimal TaxableValue { get; set; }          // Selling price ex-GST
    public decimal CgstRate { get; set; }              // e.g. 2.5
    public decimal SgstRate { get; set; }              // e.g. 2.5
    public decimal IgstRate { get; set; }              // e.g. 5 (only for inter-state)
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalGstAmount { get; set; }        // CGST+SGST or IGST

    public decimal TcsRate { get; set; }               // Section 52 TCS % (default 1)
    public decimal TcsAmount { get; set; }             // TCS collected on original order

    public decimal CreditNoteAmount { get; set; }      // TaxableValue + TotalGst (GST reversed)
    public decimal GrandRefundAmount { get; set; }     // Final amount refunded to customer

    // Credit note details
    public string? CreditNoteNumber { get; set; }      // CN/2026/001
    public DateTime? CreditNoteDate { get; set; }

    // Refund tracking
    public RefundMethod RefundMethod { get; set; } = RefundMethod.OriginalPaymentMethod;
    public string? RefundReferenceId { get; set; }     // Gateway txn / UTR
    public DateTime? RefundInitiatedAt { get; set; }
    public DateTime? RefundCompletedAt { get; set; }
    public string? RejectionReason { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();
}

public class ReturnItem : BaseEntity
{
    public int ReturnOrderId { get; set; }
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }             // Selling price per unit
    public decimal UnitMrp { get; set; }
    public decimal LineTotal { get; set; }             // UnitPrice × Quantity

    // Per-item tax breakdown
    public decimal GstRate { get; set; }               // Total GST % (e.g. 5, 12, 18)
    public decimal TaxableValue { get; set; }          // Line amount ex-GST
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal LineTotalWithTax { get; set; }      // TaxableValue + TotalTax

    public string ReturnReason { get; set; } = string.Empty;
    public ReturnItemCondition Condition { get; set; } = ReturnItemCondition.Unopened;
    public bool IsApproved { get; set; } = true;

    // Navigation
    public ReturnOrder ReturnOrder { get; set; } = null!;
    public OrderItem OrderItem { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
