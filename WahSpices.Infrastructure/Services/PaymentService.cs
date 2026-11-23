using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Payment;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentService> _logger;
    private readonly HttpClient _http;
    private readonly IInventoryService _inventory;
    private readonly IInvoiceService _invoice;
    private readonly IOrderEmails _emails;

    private string KeyId => _config["Razorpay:KeyId"] ?? throw new InvalidOperationException("Razorpay KeyId not configured");
    private string KeySecret => _config["Razorpay:KeySecret"] ?? throw new InvalidOperationException("Razorpay KeySecret not configured");

    public PaymentService(AppDbContext db, IConfiguration config,
        ILogger<PaymentService> logger, HttpClient httpClient,
        IInventoryService inventory, IInvoiceService invoice, IOrderEmails emails)
    {
        _emails = emails;
        _db = db;
        _config = config;
        _logger = logger;
        _http = httpClient;
        _inventory = inventory;
        _invoice = invoice;
    }

    // ── Create Razorpay order ─────────────────────────────────────────────
    public async Task<CreatePaymentOrderResponse> CreateOrderAsync(int orderId, int userId)
    {
        var order = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId && !o.IsDeleted)
            ?? throw AppException.NotFound("Order");

        if (order.PaymentMethod == PaymentMethod.CashOnDelivery)
            throw new AppException("COD orders do not require online payment", 400, "INVALID_OPERATION");

        if (order.PaymentStatus == PaymentStatus.Paid)
            throw new AppException("This order is already paid", 409, "ALREADY_PAID");
        if (order.Status == OrderStatus.Cancelled)
            throw new AppException("This order was cancelled and can't be paid", 400, "ORDER_CANCELLED");

        // Check if we already have a pending Razorpay order
        var existing = await _db.Payments
            .FirstOrDefaultAsync(p => p.OrderId == orderId && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Initiated) && !p.IsDeleted);

        if (existing != null)
        {
            return BuildCreateResponse(existing, order);
        }

        // Create Razorpay order via REST API
        var amountInPaise = (long)(order.GrandTotal * 100);
        var payload = new
        {
            amount = amountInPaise,
            currency = "INR",
            receipt = order.OrderNumber,
            notes = new { order_id = order.Id.ToString(), order_number = order.OrderNumber }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("orders", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Razorpay create order failed: {Body}", responseBody);
            throw new AppException("Failed to create payment order with Razorpay", 502, "RAZORPAY_ERROR");
        }

        var rzpOrder = JsonDocument.Parse(responseBody).RootElement;
        var razorpayOrderId = rzpOrder.GetProperty("id").GetString()!;

        // Save payment record
        var payment = new Payment
        {
            OrderId = orderId,
            RazorpayOrderId = razorpayOrderId,
            Amount = order.GrandTotal,
            Currency = "INR",
            Status = PaymentStatus.Initiated,
            Method = order.PaymentMethod,
            CreatedBy = userId.ToString()
        };

        _db.Payments.Add(payment);

        // Update order
        order.RazorpayOrderId = razorpayOrderId;
        order.PaymentStatus = PaymentStatus.Initiated;
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return BuildCreateResponse(payment, order);
    }

    // ── Verify Razorpay payment signature ─────────────────────────────────
    public async Task<VerifyPaymentResponse> VerifyPaymentAsync(VerifyPaymentRequest request, int userId)
    {
        var payment = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(p => p.RazorpayOrderId == request.RazorpayOrderId && !p.IsDeleted)
            ?? throw AppException.NotFound("Payment");

        if (payment.Order.UserId != userId)
            throw AppException.Forbidden();

        // HMAC-SHA256 verification
        var expectedSignature = ComputeHmac($"{request.RazorpayOrderId}|{request.RazorpayPaymentId}", KeySecret);

        if (!string.Equals(expectedSignature, request.RazorpaySignature, StringComparison.OrdinalIgnoreCase))
        {
            var alreadyFailed = payment.Status == PaymentStatus.Failed;
            payment.Status = PaymentStatus.Failed;
            payment.ErrorCode = "SIGNATURE_MISMATCH";
            payment.ErrorDescription = "Payment signature verification failed";
            payment.UpdatedAt = DateTime.UtcNow;

            payment.Order.PaymentStatus = PaymentStatus.Failed;
            payment.Order.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            if (!alreadyFailed && payment.Order.Status != OrderStatus.Cancelled) _emails.PaymentFailed(payment.OrderId);

            return new VerifyPaymentResponse(false, "Payment verification failed", payment.OrderId,
                payment.Order.OrderNumber, PaymentStatus.Failed);
        }

        // Paid after the order was auto-cancelled (payment window expired → stock already released):
        // record the money, keep the order cancelled and flag it for a refund instead of confirming it.
        if (payment.Order.Status == OrderStatus.Cancelled)
        {
            var alreadyPaid = payment.Status == PaymentStatus.Paid;   // a repeated verify call must not email twice
            payment.RazorpayPaymentId = request.RazorpayPaymentId;
            payment.RazorpaySignature = request.RazorpaySignature;
            payment.Status = PaymentStatus.Paid;
            payment.ErrorCode = "PAID_AFTER_CANCEL";
            payment.ErrorDescription = "Payment received after the order was cancelled — refund required";
            payment.UpdatedAt = DateTime.UtcNow;
            payment.Order.RazorpayPaymentId = request.RazorpayPaymentId;
            payment.Order.PaymentStatus = PaymentStatus.Paid;
            payment.Order.UpdatedAt = DateTime.UtcNow;
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = payment.OrderId, Status = OrderStatus.Cancelled,
                Note = "Payment received after the order was cancelled — it will be refunded to the original payment method",
                UpdatedBy = userId.ToString()
            });
            await _db.SaveChangesAsync();
            _logger.LogWarning("Payment {PaymentId} arrived for cancelled order {OrderNumber} — refund required", request.RazorpayPaymentId, payment.Order.OrderNumber);
            if (!alreadyPaid) _emails.PaidAfterCancel(payment.OrderId);
            return new VerifyPaymentResponse(false, "This order was cancelled because the payment wasn't completed in time. Your payment will be refunded.",
                payment.OrderId, payment.Order.OrderNumber, PaymentStatus.Paid);
        }

        // Mark as paid
        var wasPaid = payment.Status == PaymentStatus.Paid && payment.Order.PaymentStatus == PaymentStatus.Paid;
        payment.RazorpayPaymentId = request.RazorpayPaymentId;
        payment.RazorpaySignature = request.RazorpaySignature;
        payment.Status = PaymentStatus.Paid;
        payment.UpdatedAt = DateTime.UtcNow;

        var order = payment.Order;
        order.RazorpayPaymentId = request.RazorpayPaymentId;
        order.PaymentStatus = PaymentStatus.Paid;
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = DateTime.UtcNow;

        // Add status history
        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.Confirmed,
            Note = "Payment received via Razorpay",
            UpdatedBy = userId.ToString()
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Payment verified for Order {OrderNumber}, PaymentId {PaymentId}",
            order.OrderNumber, request.RazorpayPaymentId);

        // Auto-deduct inventory and generate invoice after successful payment
        // Stock was already reserved when the order was placed (OrderService → StoreRules.DeductForOrderAsync).
        // The idempotent call below is a no-op then; it only covers orders created before that change.
        try { await StoreRules.DeductForOrderAsync(_db, order.OrderNumber, order.Items.Select(i => (i.ProductId, i.Quantity)), userId.ToString()); await _db.SaveChangesAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Inventory deduction failed for Order {Id}", order.Id); }

        await _invoice.TryAutoGenerateAsync(order.Id);   // creates (or marks paid) the invoice; never throws

        if (!wasPaid) _emails.PaymentReceived(order.Id);   // one "payment received / order confirmed" email

        return new VerifyPaymentResponse(true, "Payment successful", order.Id, order.OrderNumber, PaymentStatus.Paid);
    }

    // ── Get payment by order id (customer) ───────────────────────────────
    public async Task<PaymentDetailsDto?> GetPaymentByOrderIdAsync(int orderId, int userId)
    {
        var payment = await _db.Payments
            .Include(p => p.Refunds)
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Order.UserId == userId && !p.IsDeleted);

        return payment == null ? null : MapToDetails(payment);
    }

    // ── Admin: all payments ───────────────────────────────────────────────
    public async Task<PagedResult<PaymentListItemDto>> GetAllPaymentsAsync(PaymentFilterParams filter)
    {
        var query = _db.Payments
            .Include(p => p.Order).ThenInclude(o => o.User)
            .Include(p => p.Refunds)
            .Where(p => !p.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<PaymentStatus>(filter.Status, true, out var statusEnum))
            query = query.Where(p => p.Status == statusEnum);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            query = query.Where(p =>
                p.RazorpayOrderId.ToLower().Contains(s) ||
                (p.RazorpayPaymentId != null && p.RazorpayPaymentId.ToLower().Contains(s)) ||
                p.Order.OrderNumber.ToLower().Contains(s));
        }

        if (filter.FromDate.HasValue) query = query.Where(p => p.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(p => p.CreatedAt <= filter.ToDate.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((filter.Page - 1) * filter.Limit)
            .Take(filter.Limit)
            .Select(p => new PaymentListItemDto(
                p.Id,
                p.OrderId,
                p.Order.OrderNumber,
                p.Order.User.Name,
                p.RazorpayOrderId,
                p.RazorpayPaymentId,
                p.Amount,
                p.Status,
                p.Method,
                p.CreatedAt,
                p.Refunds.Count(r => !r.IsDeleted),
                p.Refunds.Where(r => !r.IsDeleted).Sum(r => r.Amount)
            ))
            .ToListAsync();

        return new PagedResult<PaymentListItemDto>
        {
            Items = items,
            Total = total,
            Page = filter.Page,
            Limit = filter.Limit
        };
    }

    // ── Admin: payment by id ──────────────────────────────────────────────
    public async Task<PaymentDetailsDto?> GetPaymentByIdAsync(int paymentId)
    {
        var payment = await _db.Payments
            .Include(p => p.Refunds)
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId && !p.IsDeleted);

        return payment == null ? null : MapToDetails(payment);
    }

    // ── Admin: initiate refund ────────────────────────────────────────────
    public async Task<RefundDetailsDto> InitiateRefundAsync(int paymentId, InitiateRefundRequest request, string adminName)
    {
        var payment = await _db.Payments
            .Include(p => p.Refunds)
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId && !p.IsDeleted)
            ?? throw AppException.NotFound("Payment");

        if (payment.Status != PaymentStatus.Paid && payment.Status != PaymentStatus.PartiallyRefunded)
            throw new AppException("Cannot refund: payment is not in Paid status", 400, "INVALID_STATUS");

        if (string.IsNullOrEmpty(payment.RazorpayPaymentId))
            throw new AppException("Razorpay payment ID not found", 400, "NO_PAYMENT_ID");

        var alreadyRefunded = payment.Refunds.Where(r => !r.IsDeleted).Sum(r => r.Amount);
        var maxRefundable = payment.Amount - alreadyRefunded;

        if (request.Amount <= 0 || request.Amount > maxRefundable)
            throw new AppException($"Refund amount must be between ₹1 and ₹{maxRefundable}", 400, "INVALID_AMOUNT");

        // Call Razorpay refund API
        var amountInPaise = (long)(request.Amount * 100);
        var payload = new
        {
            amount = amountInPaise,
            speed = "normal",
            notes = new { reason = request.Reason ?? "Admin initiated refund", order_id = payment.OrderId.ToString() }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"payments/{payment.RazorpayPaymentId}/refund", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        string? razorpayRefundId = null;
        var refundStatus = RefundStatus.Initiated;

        if (response.IsSuccessStatusCode)
        {
            var rzpRefund = JsonDocument.Parse(responseBody).RootElement;
            razorpayRefundId = rzpRefund.GetProperty("id").GetString();

            var rzpStatus = rzpRefund.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            refundStatus = rzpStatus == "processed" ? RefundStatus.Processed : RefundStatus.Initiated;
        }
        else
        {
            _logger.LogError("Razorpay refund failed: {Body}", responseBody);
            refundStatus = RefundStatus.Failed;
        }

        // Save refund record
        var refund = new Refund
        {
            PaymentId = paymentId,
            OrderId = payment.OrderId,
            ReturnOrderId = request.ReturnOrderId,
            RazorpayRefundId = razorpayRefundId,
            Amount = request.Amount,
            Status = refundStatus,
            Method = request.Method,
            Reason = request.Reason,
            Notes = request.Notes,
            InitiatedBy = adminName,
            ProcessedAt = refundStatus == RefundStatus.Processed ? DateTime.UtcNow : null,
            CreatedBy = adminName
        };

        _db.Refunds.Add(refund);

        // Update payment and order status
        var totalRefunded = alreadyRefunded + request.Amount;
        payment.Status = totalRefunded >= payment.Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.UpdatedBy = adminName;

        if (payment.Status == PaymentStatus.Refunded)
        {
            payment.Order.PaymentStatus = PaymentStatus.Refunded;
            payment.Order.Status = OrderStatus.Refunded;
            payment.Order.UpdatedAt = DateTime.UtcNow;
            payment.Order.UpdatedBy = adminName;

            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = payment.OrderId,
                Status = OrderStatus.Refunded,
                Note = $"Refund of ₹{request.Amount} initiated by {adminName}",
                UpdatedBy = adminName
            });
        }

        await _db.SaveChangesAsync();

        if (refundStatus == RefundStatus.Failed)
            throw new AppException("Razorpay rejected the refund request. Please check payment status.", 502, "REFUND_FAILED");

        _emails.Refunded(payment.OrderId, request.Amount, partial: payment.Status != PaymentStatus.Refunded, request.Reason);

        return MapRefundToDto(refund);
    }

    // ── Admin: sync refund status from Razorpay ───────────────────────────
    public async Task<RefundDetailsDto?> SyncRefundStatusAsync(int refundId)
    {
        var refund = await _db.Refunds.FindAsync(refundId);
        if (refund == null || string.IsNullOrEmpty(refund.RazorpayRefundId)) return null;

        var response = await _http.GetAsync($"refunds/{refund.RazorpayRefundId}");
        if (!response.IsSuccessStatusCode) return MapRefundToDto(refund);

        var body = await response.Content.ReadAsStringAsync();
        var rzp = JsonDocument.Parse(body).RootElement;
        var status = rzp.TryGetProperty("status", out var s) ? s.GetString() : null;

        if (status == "processed")
        {
            refund.Status = RefundStatus.Processed;
            refund.ProcessedAt = DateTime.UtcNow;
            refund.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return MapRefundToDto(refund);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static string ComputeHmac(string message, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var msg = Encoding.UTF8.GetBytes(message);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(msg)).ToLower();
    }

    private CreatePaymentOrderResponse BuildCreateResponse(Payment payment, Order order) =>
        new(
            payment.RazorpayOrderId,
            order.GrandTotal,
            (long)(order.GrandTotal * 100),
            "INR",
            KeyId,
            order.OrderNumber,
            order.User?.Name ?? string.Empty,
            order.User?.Email ?? string.Empty,
            order.User?.Phone ?? string.Empty
        );

    private static PaymentDetailsDto MapToDetails(Payment p) =>
        new(
            p.Id, p.OrderId,
            p.Order?.OrderNumber ?? string.Empty,
            p.RazorpayOrderId, p.RazorpayPaymentId,
            p.Amount, p.Status, p.Method,
            p.ErrorCode, p.ErrorDescription,
            p.CreatedAt,
            p.Refunds.Where(r => !r.IsDeleted).Select(MapRefundToDto)
        );

    private static RefundDetailsDto MapRefundToDto(Refund r) =>
        new(r.Id, r.PaymentId, r.OrderId, r.ReturnOrderId,
            r.RazorpayRefundId, r.Amount, r.Status, r.Method,
            r.Reason, r.InitiatedBy, r.ProcessedAt, r.CreatedAt);
}
