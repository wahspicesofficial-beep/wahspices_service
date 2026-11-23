using WahSpices.Domain.Enums;

namespace WahSpices.Infrastructure.Email;

public sealed record OrderEmailItem(string Name, string Weight, int Qty, decimal UnitPrice, decimal Mrp, decimal LineTotal, string? ImagePath, string? Slug);

public sealed record OrderEmailAddress(string Name, string Phone, string Line, string? Landmark, string City, string State, string Pincode);

public sealed class OrderEmailModel
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public DateTime PlacedAtUtc { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerEmail { get; init; } = "";
    public List<OrderEmailItem> Items { get; init; } = new();
    public decimal SubtotalMrp { get; init; }
    public decimal Subtotal { get; init; }
    public decimal CouponDiscount { get; init; }
    public string? CouponCode { get; init; }
    public decimal Delivery { get; init; }
    public decimal GrandTotal { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public OrderStatus Status { get; init; }
    public OrderEmailAddress? Address { get; init; }
    public DateTime? EstimatedDeliveryUtc { get; init; }
    public DateTime? DeliveredAtUtc { get; init; }
    public string? Courier { get; init; }
    public string? AwbNumber { get; init; }
    public string? CourierTrackingUrl { get; init; }
    public string? CancellationReason { get; init; }
    public string? PaymentReference { get; init; }
    /// <summary>Set when the invoice PDF is attached to this email (adds an "Invoice attached" note).</summary>
    public string? AttachedInvoiceNumber { get; set; }

    public bool IsCod => PaymentMethod == PaymentMethod.CashOnDelivery;
    public bool PaidOnline => !IsCod && PaymentStatus is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;
}

public enum CancelSource { Customer, Store, PaymentTimeout }

public enum ReturnEmailKind { Requested, Approved, Rejected, PickedUp, Received, Refunded }

public sealed record ReturnEmailItem(string Name, string Weight, int Qty, decimal UnitPrice, decimal LineTotal, string? ImagePath, string? Slug);

public sealed class ReturnEmailModel
{
    public string ReturnNumber { get; init; } = "";
    public int OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string CustomerEmail { get; init; } = "";
    public ReturnType Type { get; init; }
    public string Reason { get; init; } = "";
    public List<ReturnEmailItem> Items { get; init; } = new();
    public decimal RefundAmount { get; init; }
    public RefundMethod RefundMethod { get; init; }
    public string? RefundReference { get; init; }
    public string? CreditNoteNumber { get; init; }
    public string? PickupAddress { get; init; }
    public DateTime? PickupScheduledUtc { get; init; }
    public string? Courier { get; init; }
    public string? AwbNumber { get; init; }
    public string? RejectionReason { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public DateTime RequestedAtUtc { get; init; }
}

public sealed record InvoiceEmailLine(string Name, string? Hsn, int Qty, decimal Rate, decimal Taxable, decimal GstRate, decimal Total);

public sealed class InvoiceEmailModel
{
    public string InvoiceNumber { get; init; } = "";
    public DateTime DateUtc { get; init; }
    public string Status { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string CompanyAddress { get; init; } = "";
    public string? CompanyGstin { get; init; }
    public string? StateCodeLine { get; init; }
    public string? CompanyContact { get; init; }
    public string CustomerName { get; init; } = "";
    public string? CustomerEmail { get; init; }
    public string? CustomerPhone { get; init; }
    public List<InvoiceEmailLine> Lines { get; init; } = new();
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
    public decimal Igst { get; init; }
    public decimal Delivery { get; init; }
    public decimal Total { get; init; }
    public string? BankLine { get; init; }
    public string? Terms { get; init; }
    public string? FooterText { get; init; }
    public decimal RoundOff { get; init; }
    public string? Message { get; init; }
    public bool PdfAttached { get; init; }
}
