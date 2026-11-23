using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Order;

public record PlaceOrderRequest(
    int AddressId,
    string PaymentMethod,
    string? CouponCode,
    List<OrderItemRequest> Items,
    string? Notes,
    decimal? ExpectedTotal = null   // total the customer saw; a mismatch is rejected so they never pay a surprise amount
);

public record OrderItemRequest(int ProductId, int Quantity, int? VariantId = null);

public record OrderDto(
    int Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    string PaymentMethod,
    decimal SubtotalMrp,
    decimal Subtotal,
    decimal Discount,
    decimal CouponDiscount,
    decimal DeliveryCharge,
    decimal GrandTotal,
    string? CouponCode,
    string? Notes,
    string? Courier,
    string? AwbNumber,
    DateTime? EstimatedDelivery,
    DateTime? DeliveredAt,
    DateTime CreatedAt,
    List<OrderItemDto> Items,
    AddressSummaryDto DeliveryAddress,
    // detail extras (lists may leave them empty)
    string? CourierTrackingUrl = null,
    DateTime? DispatchedAt = null,
    string? CancellationReason = null,
    bool CanCancel = false,
    bool CanReturn = false,
    decimal RefundedAmount = 0,
    List<OrderHistoryDto>? History = null,
    OrderReturnInfoDto? Return = null,
    OrderPaymentInfoDto? Payment = null
);

public record OrderItemDto(
    int ProductId,
    string ProductName,
    string Weight,
    decimal Price,
    decimal Mrp,
    int Quantity,
    decimal LineTotal,
    int Id = 0,
    int? VariantId = null,
    string? ImageUrl = null,
    string? Slug = null
);

public record OrderHistoryDto(string Status, string? Note, DateTime CreatedAt);

/// <summary>Latest (non-cancelled) return of an order — status + refund, for the customer's order card.</summary>
public record OrderReturnInfoDto(
    int Id, string ReturnNumber, string Status, string Reason, decimal RefundAmount,
    string RefundMethod, string? RefundReferenceId, DateTime? RefundCompletedAt, string? RejectionReason,
    string? CourierName, string? AwbNumber, DateTime CreatedAt);

public record OrderPaymentInfoDto(
    string Method, string Status, decimal Amount, string? GatewayOrderId, string? GatewayPaymentId,
    decimal RefundedAmount, DateTime? PaidAt);

/// <summary>Price a cart exactly as POST /api/orders would (public: guests see the same numbers).</summary>
public record QuoteRequest(List<OrderItemRequest> Items, string? CouponCode);

public record QuoteLineDto(
    int ProductId, int? VariantId, string Name, string Weight, decimal Price, decimal Mrp,
    int Quantity, decimal LineTotal, bool Available, string? Message);

public record QuoteDto(
    List<QuoteLineDto> Items,
    int ItemCount,
    decimal SubtotalMrp,
    decimal Subtotal,
    decimal Discount,
    decimal CouponDiscount,
    string? CouponCode,
    string? CouponError,
    decimal DeliveryCharge,
    decimal FreeDeliveryAbove,
    decimal AmountForFreeDelivery,
    decimal GrandTotal,
    bool CodAvailable,
    bool MaintenanceMode,
    bool CanPlaceOrder,
    string? BlockReason
);

public record AddressSummaryDto(
    string Name,
    string Phone,
    string AddressLine,
    string City,
    string State,
    string Pincode
);

public record TrackOrderResponse(
    string OrderId,
    string Status,
    string? Courier,
    string? AwbNumber,
    string? CourierTrackingUrl,
    string? EstimatedDelivery,
    DateTime? DeliveredAt,
    List<TimelineStep> Timeline,
    AddressSummaryDto DeliveryAddress,
    string? OrderNumber = null,
    DateTime? CreatedAt = null,
    DateTime? DispatchedAt = null,
    string? PaymentMethod = null,
    string? PaymentStatus = null,
    decimal Subtotal = 0,
    decimal CouponDiscount = 0,
    decimal DeliveryCharge = 0,
    decimal GrandTotal = 0,
    string? CancellationReason = null,
    List<OrderItemDto>? Items = null,
    List<OrderHistoryDto>? History = null,
    OrderReturnInfoDto? Return = null
);

public record TimelineStep(
    string Step,
    string? Date,
    bool Done,
    string? Label = null,
    string? Note = null,
    bool Current = false,
    DateTime? At = null   // UTC instant of the step (Date is the same, pre-formatted in IST)
);

public record ApplyCouponRequest(string CouponCode);

public record CartDto(
    string CartId,
    List<CartItemDto> Items,
    CartSummaryDto Summary
);

public record CartItemDto(
    int CartItemId,
    int ProductId,
    string Name,
    string Slug,
    decimal Price,
    decimal Mrp,
    string Weight,
    int Quantity,
    decimal LineTotal,
    string? ImageUrl,
    bool InStock
);

public record CartSummaryDto(
    int ItemCount,
    decimal SubtotalMrp,
    decimal Subtotal,
    decimal Discount,
    decimal CouponDiscount,
    string? CouponCode,
    decimal DeliveryCharge,
    decimal FreeDeliveryAt,
    decimal GrandTotal
);
