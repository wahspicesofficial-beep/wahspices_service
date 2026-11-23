using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Order;

namespace WahSpices.Application.Interfaces.Services;

public interface ICartService
{
    Task<ApiResponse<CartDto>> GetCartAsync(int userId);
    Task<ApiResponse<CartSummaryDto>> AddItemAsync(int userId, int productId, int quantity);
    Task<ApiResponse<CartSummaryDto>> UpdateItemAsync(int userId, int productId, int quantity);
    Task<ApiResponse<CartSummaryDto>> RemoveItemAsync(int userId, int productId);
    Task<ApiResponse<CartSummaryDto>> ClearCartAsync(int userId);
    Task<ApiResponse<CouponResult>> ApplyCouponAsync(int userId, string couponCode);
    Task<ApiResponse<CartSummaryDto>> RemoveCouponAsync(int userId);
    Task<ApiResponse<CartSummaryDto>> SyncCartAsync(int userId, List<CartItemRequest> guestItems);
}

public record CartItemRequest(int ProductId, int Quantity);
public record CouponResult(string CouponCode, string DiscountType, decimal DiscountValue, decimal DiscountAmount, decimal NewGrandTotal);

public interface IOrderService
{
    Task<ApiResponse<PlaceOrderResponse>> PlaceOrderAsync(int userId, PlaceOrderRequest request);
    Task<ApiResponse<PagedResult<OrderDto>>> GetUserOrdersAsync(int userId, string? status, int page, int limit);
    Task<ApiResponse<OrderDto>> GetOrderDetailAsync(int userId, int orderId);
    /// <summary>Public tracking: order number + the phone or email used on the order (never by number alone).</summary>
    Task<ApiResponse<TrackOrderResponse>> TrackPublicAsync(string orderNumber, string contact);
    /// <summary>Logged-in customer tracking one of their own orders.</summary>
    Task<ApiResponse<TrackOrderResponse>> TrackForUserAsync(int userId, string orderNumber);
    Task<ApiResponse<bool>> CancelOrderAsync(int userId, int orderId, string reason);
    Task<ApiResponse<QuoteDto>> QuoteAsync(QuoteRequest request);
}

public record PlaceOrderResponse(
    string OrderId,
    string Status,
    decimal Amount,
    string PaymentStatus,
    PaymentSessionInfo? PaymentSession,
    string EstimatedDelivery,
    string ConfirmationSentTo,
    int Id = 0,
    string? OrderNumber = null,
    string? PaymentMethod = null,
    decimal Subtotal = 0,
    decimal CouponDiscount = 0,
    decimal DeliveryCharge = 0
);

public record PaymentSessionInfo(string SessionId, string RedirectUrl);
