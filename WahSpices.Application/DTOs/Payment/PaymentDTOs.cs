using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Payment;

// ── Create Razorpay order ─────────────────────────────────────────────────
public record CreatePaymentOrderRequest(int OrderId);

public record CreatePaymentOrderResponse(
    string RazorpayOrderId,
    decimal Amount,          // in INR
    long AmountInPaise,      // Razorpay expects paise
    string Currency,
    string KeyId,
    string OrderNumber,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone
);

// ── Verify payment after Razorpay callback ───────────────────────────────
public record VerifyPaymentRequest(
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature
);

public record VerifyPaymentResponse(
    bool Success,
    string Message,
    int OrderId,
    string OrderNumber,
    PaymentStatus PaymentStatus
);

// ── Payment details ───────────────────────────────────────────────────────
public record PaymentDetailsDto(
    int Id,
    int OrderId,
    string OrderNumber,
    string RazorpayOrderId,
    string? RazorpayPaymentId,
    decimal Amount,
    PaymentStatus Status,
    PaymentMethod Method,
    string? ErrorCode,
    string? ErrorDescription,
    DateTime CreatedAt,
    IEnumerable<RefundDetailsDto> Refunds
);

// ── Admin: payment list item ──────────────────────────────────────────────
public record PaymentListItemDto(
    int Id,
    int OrderId,
    string OrderNumber,
    string CustomerName,
    string RazorpayOrderId,
    string? RazorpayPaymentId,
    decimal Amount,
    PaymentStatus Status,
    PaymentMethod Method,
    DateTime CreatedAt,
    int RefundCount,
    decimal RefundedAmount
);

// ── Initiate refund ───────────────────────────────────────────────────────
public record InitiateRefundRequest(
    decimal Amount,
    string? Reason,
    string? Notes,
    int? ReturnOrderId,
    RefundMethod Method = RefundMethod.OriginalPaymentMethod
);

// ── Refund details ────────────────────────────────────────────────────────
public record RefundDetailsDto(
    int Id,
    int PaymentId,
    int OrderId,
    int? ReturnOrderId,
    string? RazorpayRefundId,
    decimal Amount,
    RefundStatus Status,
    RefundMethod Method,
    string? Reason,
    string? InitiatedBy,
    DateTime? ProcessedAt,
    DateTime CreatedAt
);

// ── Payment filter params ─────────────────────────────────────────────────
public class PaymentFilterParams
{
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 20;
    public string? Status { get; set; }
    public string? Search { get; set; }    // order number or razorpay id
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
