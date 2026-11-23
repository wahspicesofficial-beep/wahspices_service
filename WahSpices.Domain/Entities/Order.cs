using WahSpices.Domain.Common;
using WahSpices.Domain.Enums;

namespace WahSpices.Domain.Entities;

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;  // WAH20260610001
    public int UserId { get; set; }
    public int AddressId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; }
    public string? CouponCode { get; set; }
    public decimal SubtotalMrp { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal CouponDiscount { get; set; }
    public decimal DeliveryCharge { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Notes { get; set; }
    public string? Courier { get; set; }
    public string? AwbNumber { get; set; }
    public string? CourierTrackingUrl { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? CancellationReason { get; set; }
    public string? ReturnReason { get; set; }
    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Address Address { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public Invoice? Invoice { get; set; }
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Mrp { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public class OrderStatusHistory : BaseEntity
{
    public int OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public new string? UpdatedBy { get; set; }
    public Order Order { get; set; } = null!;
}
