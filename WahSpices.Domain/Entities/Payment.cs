using WahSpices.Domain.Common;
using WahSpices.Domain.Enums;

namespace WahSpices.Domain.Entities;

public class Payment : BaseEntity
{
    public int OrderId { get; set; }
    public string RazorpayOrderId { get; set; } = string.Empty;   // order_xxx from Razorpay
    public string? RazorpayPaymentId { get; set; }                 // pay_xxx after success
    public string? RazorpaySignature { get; set; }                 // HMAC verification
    public decimal Amount { get; set; }                            // in INR
    public string Currency { get; set; } = "INR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentMethod Method { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDescription { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public class Refund : BaseEntity
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public int? ReturnOrderId { get; set; }
    public string? RazorpayRefundId { get; set; }      // rfnd_xxx from Razorpay
    public decimal Amount { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public RefundMethod Method { get; set; } = RefundMethod.OriginalPaymentMethod;
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public string? InitiatedBy { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Navigation
    public Payment Payment { get; set; } = null!;
    public Order Order { get; set; } = null!;
    public ReturnOrder? ReturnOrder { get; set; }
}
