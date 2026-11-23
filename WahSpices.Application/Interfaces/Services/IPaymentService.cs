using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Payment;

namespace WahSpices.Application.Interfaces.Services;

public interface IPaymentService
{
    /// <summary>Create a Razorpay order before the checkout. Called by the customer.</summary>
    Task<CreatePaymentOrderResponse> CreateOrderAsync(int orderId, int userId);

    /// <summary>Verify HMAC signature returned by Razorpay and mark order as paid.</summary>
    Task<VerifyPaymentResponse> VerifyPaymentAsync(VerifyPaymentRequest request, int userId);

    /// <summary>Get payment details for an order (customer-facing).</summary>
    Task<PaymentDetailsDto?> GetPaymentByOrderIdAsync(int orderId, int userId);

    /// <summary>Admin: list all payments with filters.</summary>
    Task<PagedResult<PaymentListItemDto>> GetAllPaymentsAsync(PaymentFilterParams filter);

    /// <summary>Admin: get payment details by payment id.</summary>
    Task<PaymentDetailsDto?> GetPaymentByIdAsync(int paymentId);

    /// <summary>Admin: initiate a Razorpay refund for a payment.</summary>
    Task<RefundDetailsDto> InitiateRefundAsync(int paymentId, InitiateRefundRequest request, string adminName);

    /// <summary>Admin: fetch latest status of a refund from Razorpay and update DB.</summary>
    Task<RefundDetailsDto?> SyncRefundStatusAsync(int refundId);
}
