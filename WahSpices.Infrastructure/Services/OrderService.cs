using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Order;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderEmails _emails;
    private readonly IInvoiceService _invoices;

    public OrderService(AppDbContext db, ILogger<OrderService> logger, IOrderEmails emails, IInvoiceService invoices)
    {
        _db = db;
        _logger = logger;
        _emails = emails;
        _invoices = invoices;
    }

    private static readonly OrderStatus[] Cancellable =
        { OrderStatus.Pending, OrderStatus.Processing, OrderStatus.Confirmed, OrderStatus.Packed };

    // ── Pricing (shared by quote + place order: the client always shows what the server charges) ──
    private sealed class Priced
    {
        public List<QuoteLineDto> Lines { get; } = new();
        public List<OrderItem> Items { get; } = new();
        public decimal SubtotalMrp, Subtotal, CouponDiscount, Delivery, GrandTotal;
        public Coupon? Coupon;
        public string? CouponError;
        public SiteSettings Settings = new();
        public string? FirstError => Lines.FirstOrDefault(l => !l.Available)?.Message;
    }

    private async Task<Priced> PriceAsync(List<OrderItemRequest> items, string? couponCode)
    {
        var r = new Priced { Settings = await StoreRules.GetSettingsAsync(_db) };
        var ids = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Include(p => p.Variants)
            .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id);
        var stock = await StoreRules.StockByProductAsync(_db, ids);
        var wanted = items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0, x.Quantity)));
        // Pack sizes with their own stock row (created by packing runs) are limited to that row's packets
        var variantIds = items.Where(i => i.VariantId.HasValue).Select(i => i.VariantId!.Value).Distinct().ToList();
        var variantStock = variantIds.Count == 0 ? new Dictionary<int, int>() : await _db.InventoryItems.AsNoTracking()
            .Where(x => !x.IsDeleted && x.VariantId != null && variantIds.Contains(x.VariantId.Value))
            .GroupBy(x => x.VariantId!.Value)
            .Select(g => new { g.Key, Qty = g.Sum(x => x.CurrentStock) })
            .ToDictionaryAsync(x => x.Key, x => (int)Math.Floor((decimal)x.Qty));
        var wantedVariant = items.Where(i => i.VariantId.HasValue).GroupBy(i => i.VariantId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0, x.Quantity)));

        foreach (var i in items)
        {
            if (!products.TryGetValue(i.ProductId, out var p))
            {
                r.Lines.Add(new QuoteLineDto(i.ProductId, i.VariantId, $"Product #{i.ProductId}", "", 0, 0, i.Quantity, 0, false,
                    "A product in your cart is no longer available"));
                continue;
            }
            var variants = p.Variants.Where(v => !v.IsDeleted).ToList();
            ProductVariant? variant = null;
            string? error = null;
            if (i.VariantId.HasValue)
            {
                variant = variants.FirstOrDefault(v => v.Id == i.VariantId.Value);
                if (variant == null) error = $"The selected pack size of {p.Name} is no longer available";
                else if (!variant.InStock) error = $"{p.Name} ({variant.Weight}) is out of stock";
            }
            else if (variants.Count > 0)
            {
                variant = variants.FirstOrDefault(v => v.IsDefault) ?? variants.OrderBy(v => v.WeightGrams).First();
            }

            if (error == null && !p.InStock) error = $"{p.Name} is out of stock";
            if (error == null && (i.Quantity < 1 || i.Quantity > 100)) error = $"Quantity for {p.Name} must be between 1 and 100";
            if (error == null && variant != null && i.VariantId.HasValue && variantStock.TryGetValue(variant.Id, out var vHave) && vHave < wantedVariant[variant.Id])
                error = vHave <= 0 ? $"{p.Name} ({variant.Weight}) is out of stock" : $"Only {vHave} pack(s) of {p.Name} ({variant.Weight}) left in stock";
            if (error == null && stock.TryGetValue(p.Id, out var have) && have < wanted[p.Id])
                error = have <= 0 ? $"{p.Name} is out of stock" : $"Only {have} unit(s) of {p.Name} left in stock";

            var price = variant?.Price ?? p.Price;
            var mrp = Math.Max(variant?.Mrp ?? p.Mrp, price);
            var weight = variant?.Weight ?? p.Weight;
            var qty = Math.Max(0, i.Quantity);
            var available = error == null;

            r.Lines.Add(new QuoteLineDto(p.Id, variant?.Id, p.Name, weight, price, mrp, qty, price * qty, available, error));
            if (!available) continue;

            r.SubtotalMrp += mrp * qty;
            r.Subtotal += price * qty;
            r.Items.Add(new OrderItem
            {
                ProductId = p.Id, VariantId = variant?.Id, ProductName = p.Name, Weight = weight,
                Price = price, Mrp = mrp, Quantity = qty, LineTotal = price * qty,
            });
        }

        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var code = couponCode.Trim().ToUpper();
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c =>
                c.Code == code && c.IsActive && !c.IsDeleted &&
                (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow) &&
                (c.MaxUses == null || c.UsedCount < c.MaxUses));
            if (coupon == null) r.CouponError = "Coupon is invalid or expired";
            else if (coupon.MinCartValue.HasValue && r.Subtotal < coupon.MinCartValue.Value)
                r.CouponError = $"Add items worth ₹{coupon.MinCartValue.Value - r.Subtotal:0.##} more to use {coupon.Code} (minimum ₹{coupon.MinCartValue.Value:0.##})";
            else
            {
                var d = coupon.DiscountType == "percentage" ? r.Subtotal * coupon.DiscountValue / 100 : coupon.DiscountValue;
                if (coupon.MaxDiscount.HasValue) d = Math.Min(d, coupon.MaxDiscount.Value);
                r.CouponDiscount = Math.Round(Math.Min(d, r.Subtotal), 2);
                r.Coupon = coupon;
            }
        }

        var orderValue = r.Subtotal - r.CouponDiscount;
        r.Delivery = r.Items.Count == 0 ? 0 : StoreRules.DeliveryFor(r.Settings, orderValue);
        r.GrandTotal = orderValue + r.Delivery;
        return r;
    }

    public async Task<ApiResponse<QuoteDto>> QuoteAsync(QuoteRequest request)
    {
        var items = request.Items ?? new();
        if (items.Count > 50) return ApiResponse<QuoteDto>.Fail("VALIDATION_ERROR", "Too many items in one cart");
        var r = await PriceAsync(items, request.CouponCode);
        var s = r.Settings;
        string? block = s.MaintenanceMode ? "The store is under maintenance — orders are paused for now. Please try again later."
            : r.FirstError;
        var orderValue = r.Subtotal - r.CouponDiscount;
        return ApiResponse<QuoteDto>.Ok(new QuoteDto(
            r.Lines, r.Items.Sum(i => i.Quantity), r.SubtotalMrp, r.Subtotal, r.SubtotalMrp - r.Subtotal,
            r.CouponDiscount, r.Coupon?.Code, r.CouponError, r.Delivery, s.FreeDeliveryAbove,
            s.FreeDeliveryAbove > 0 && r.Items.Count > 0 ? Math.Max(0, s.FreeDeliveryAbove - orderValue) : 0,
            r.GrandTotal, s.CodAvailable, s.MaintenanceMode,
            block == null && r.Items.Count > 0, block));
    }

    // ── Place order ───────────────────────────────────────────────────────
    public async Task<ApiResponse<PlaceOrderResponse>> PlaceOrderAsync(int userId, PlaceOrderRequest request)
    {
        var settings = await StoreRules.GetSettingsAsync(_db);
        if (settings.MaintenanceMode)
            return ApiResponse<PlaceOrderResponse>.Fail("MAINTENANCE",
                "The store is under maintenance — orders are paused for now. Please try again later.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
        if (user == null || !user.IsActive)
            return ApiResponse<PlaceOrderResponse>.Fail("ACCOUNT_DISABLED", "Your account is disabled. Please contact support.");

        var address = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId && !a.IsDeleted);
        if (address == null)
            return ApiResponse<PlaceOrderResponse>.Fail("INVALID_ADDRESS", "Delivery address not found");

        if (string.IsNullOrWhiteSpace(request.PaymentMethod) || int.TryParse(request.PaymentMethod, out _) ||
            !Enum.TryParse<PaymentMethod>(request.PaymentMethod, true, out var paymentMethod) || !Enum.IsDefined(paymentMethod))
            return ApiResponse<PlaceOrderResponse>.Fail("INVALID_PAYMENT",
                $"Invalid payment method. Allowed: {string.Join(", ", Enum.GetNames<PaymentMethod>())}");

        if (paymentMethod == PaymentMethod.CashOnDelivery && !settings.CodAvailable)
            return ApiResponse<PlaceOrderResponse>.Fail("COD_UNAVAILABLE",
                "Cash on Delivery is currently not available. Please choose an online payment method.");

        if (request.Items == null || request.Items.Count == 0)
            return ApiResponse<PlaceOrderResponse>.Fail("EMPTY_CART", "Your cart is empty");

        var priced = await PriceAsync(request.Items, request.CouponCode);
        if (priced.FirstError != null)
            return ApiResponse<PlaceOrderResponse>.Fail("ITEM_UNAVAILABLE", priced.FirstError);
        if (!string.IsNullOrWhiteSpace(request.CouponCode) && priced.CouponError != null)
            return ApiResponse<PlaceOrderResponse>.Fail("INVALID_COUPON", priced.CouponError);
        if (request.ExpectedTotal.HasValue && Math.Abs(request.ExpectedTotal.Value - priced.GrandTotal) > 0.5m)
            return ApiResponse<PlaceOrderResponse>.Fail("PRICE_CHANGED",
                $"Prices or delivery charges have changed. Your new total is ₹{priced.GrandTotal:0.##} — please review and place the order again.");

        var orderNumber = await NewOrderNumberAsync();
        var days = MaxDeliveryDays(settings.AvgDeliveryDays);
        var by = userId.ToString();

        // One SaveChanges = one DB transaction (order, items, history, stock ledger, cart clear)
        var order = new Order
        {
            OrderNumber    = orderNumber,
            UserId         = userId,
            AddressId      = request.AddressId,
            PaymentMethod  = paymentMethod,
            Status         = OrderStatus.Pending,
            PaymentStatus  = PaymentStatus.Pending,
            CouponCode     = priced.Coupon?.Code,
            SubtotalMrp    = priced.SubtotalMrp,
            Subtotal       = priced.Subtotal,
            Discount       = priced.SubtotalMrp - priced.Subtotal,
            CouponDiscount = priced.CouponDiscount,
            DeliveryCharge = priced.Delivery,
            GrandTotal     = priced.GrandTotal,
            Notes          = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()[..Math.Min(500, request.Notes.Trim().Length)],
            EstimatedDelivery = DateTime.UtcNow.AddDays(days),
            CreatedBy      = by
        };
        foreach (var item in priced.Items) { item.CreatedBy = by; order.Items.Add(item); }
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Pending,
            Note = paymentMethod == PaymentMethod.CashOnDelivery ? "Order placed (Cash on Delivery)" : "Order placed — awaiting online payment",
            UpdatedBy = by
        });
        _db.Orders.Add(order);
        if (priced.Coupon != null) priced.Coupon.UsedCount++;

        // Stock is reserved at placement (restored on cancel / return received)
        await StoreRules.DeductForOrderAsync(_db, orderNumber, priced.Items.Select(i => (i.ProductId, i.Quantity)), by);

        var cartItems = await _db.CartItems.Where(c => c.UserId == userId && !c.IsDeleted).ToListAsync();
        cartItems.ForEach(c => c.IsDeleted = true);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Order {Number} placed by user {UserId}, total ₹{Total}", orderNumber, userId, priced.GrandTotal);
        // Cash on Delivery: the tax invoice is issued now (online orders get theirs when the payment succeeds),
        // so the order email can carry the PDF. Never fails the order.
        if (paymentMethod == PaymentMethod.CashOnDelivery) await _invoices.TryAutoGenerateAsync(order.Id);
        _emails.OrderPlaced(order.Id);   // queued after the commit — never delays or fails the order

        return ApiResponse<PlaceOrderResponse>.Ok(new PlaceOrderResponse(
            orderNumber,
            OrderStatus.Pending.ToString(),
            priced.GrandTotal,
            PaymentStatus.Pending.ToString(),
            null,
            order.EstimatedDelivery?.ToString("dd MMM yyyy") ?? $"{settings.AvgDeliveryDays} business days",
            user.Email,
            order.Id,
            orderNumber,
            paymentMethod.ToString(),
            priced.Subtotal,
            priced.CouponDiscount,
            priced.Delivery
        ), "Order placed successfully");
    }

    private async Task<string> NewOrderNumberAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var n = $"WAH{DateTime.UtcNow:yyMMdd}{Random.Shared.Next(1000, 9999)}";
            if (!await _db.Orders.AnyAsync(o => o.OrderNumber == n)) return n;
        }
        return $"WAH{DateTime.UtcNow:yyMMddHHmmss}";
    }

    private static int MaxDeliveryDays(string? avg)
    {
        var nums = Regex.Matches(avg ?? "", @"\d+").Select(m => int.Parse(m.Value)).ToList();
        return nums.Count == 0 ? 5 : Math.Clamp(nums.Max(), 1, 30);
    }

    // ── User orders ───────────────────────────────────────────────────────
    public async Task<ApiResponse<PagedResult<OrderDto>>> GetUserOrdersAsync(int userId, string? status, int page, int limit)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 50);
        var q = _db.Orders.AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Address)
            .Where(o => o.UserId == userId && !o.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
            q = q.Where(o => o.Status == s);

        var total = await q.CountAsync();
        var orders = await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var returns = await LatestReturnsAsync(ids);
        var refunds = await RefundTotalsAsync(ids);

        return ApiResponse<PagedResult<OrderDto>>.Ok(new PagedResult<OrderDto>
        {
            Items = orders.Select(o => MapOrderDto(o, returns.GetValueOrDefault(o.Id), refunds.GetValueOrDefault(o.Id), null, null)).ToList(),
            Total = total, Page = page, Limit = limit
        });
    }

    // ── Order detail ──────────────────────────────────────────────────────
    public async Task<ApiResponse<OrderDto>> GetOrderDetailAsync(int userId, int orderId)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Address)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId && !o.IsDeleted);
        if (order == null) return ApiResponse<OrderDto>.Fail("NOT_FOUND", "Order not found");

        var ret = (await LatestReturnsAsync(new() { order.Id })).GetValueOrDefault(order.Id);
        var refunded = (await RefundTotalsAsync(new() { order.Id })).GetValueOrDefault(order.Id);
        var payment = await _db.Payments.AsNoTracking().Include(p => p.Refunds)
            .Where(p => p.OrderId == order.Id && !p.IsDeleted).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
        var payInfo = new OrderPaymentInfoDto(
            order.PaymentMethod.ToString(), order.PaymentStatus.ToString(), order.GrandTotal,
            payment?.RazorpayOrderId ?? order.RazorpayOrderId, payment?.RazorpayPaymentId ?? order.RazorpayPaymentId,
            refunded, payment?.Status == PaymentStatus.Paid ? payment.UpdatedAt ?? payment.CreatedAt : (order.PaymentStatus == PaymentStatus.Paid ? order.DeliveredAt : null));
        var history = order.StatusHistory.Where(h => !h.IsDeleted).OrderBy(h => h.CreatedAt)
            .Select(h => new OrderHistoryDto(h.Status.ToString(), h.Note, h.CreatedAt)).ToList();
        return ApiResponse<OrderDto>.Ok(MapOrderDto(order, ret, refunded, history, payInfo));
    }

    // ── Track order ───────────────────────────────────────────────────────
    private static string Digits(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());
    private static string Last10(string? s) { var d = Digits(s); return d.Length > 10 ? d[^10..] : d; }

    public async Task<ApiResponse<TrackOrderResponse>> TrackPublicAsync(string orderNumber, string contact)
    {
        const string notFound = "We couldn't find an order with that order number and phone / email. Please check both and try again.";
        var number = (orderNumber ?? "").Trim().TrimStart('#').ToUpper();
        var c = (contact ?? "").Trim();
        if (number.Length < 4 || c.Length < 4)
            return ApiResponse<TrackOrderResponse>.Fail("VALIDATION_ERROR", "Enter your order number and the phone number or email used for the order.");

        var order = await LoadForTracking(o => o.OrderNumber == number);
        if (order == null) return ApiResponse<TrackOrderResponse>.Fail("NOT_FOUND", notFound);

        bool match;
        if (c.Contains('@'))
            match = string.Equals(order.User?.Email?.Trim(), c, StringComparison.OrdinalIgnoreCase);
        else
        {
            var p = Last10(c);
            match = p.Length == 10 && (Last10(order.User?.Phone) == p || Last10(order.Address?.Phone) == p);
        }
        if (!match) return ApiResponse<TrackOrderResponse>.Fail("NOT_FOUND", notFound);
        return ApiResponse<TrackOrderResponse>.Ok(await BuildTrackingAsync(order));
    }

    public async Task<ApiResponse<TrackOrderResponse>> TrackForUserAsync(int userId, string orderNumber)
    {
        var number = (orderNumber ?? "").Trim().TrimStart('#').ToUpper();
        var order = await LoadForTracking(o => o.OrderNumber == number && o.UserId == userId);
        if (order == null) return ApiResponse<TrackOrderResponse>.Fail("NOT_FOUND", "Order not found in your account");
        return ApiResponse<TrackOrderResponse>.Ok(await BuildTrackingAsync(order));
    }

    private Task<Order?> LoadForTracking(System.Linq.Expressions.Expression<Func<Order, bool>> where) =>
        _db.Orders.AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.StatusHistory)
            .Where(o => !o.IsDeleted)
            .FirstOrDefaultAsync(where);

    private static readonly (OrderStatus Status, string Label)[] FlowSteps =
    {
        (OrderStatus.Pending, "Order Placed"), (OrderStatus.Confirmed, "Confirmed"), (OrderStatus.Packed, "Packed"),
        (OrderStatus.Dispatched, "Dispatched"), (OrderStatus.InTransit, "In Transit"),
        (OrderStatus.OutForDelivery, "Out for Delivery"), (OrderStatus.Delivered, "Delivered"),
    };

    private static string StatusLabel(OrderStatus s) => s switch
    {
        OrderStatus.Pending => "Order Placed",
        OrderStatus.InTransit => "In Transit",
        OrderStatus.OutForDelivery => "Out for Delivery",
        OrderStatus.ReturnRequested => "Return Requested",
        _ => s.ToString()
    };

    private static readonly TimeZoneInfo Ist = FindIst();
    private static TimeZoneInfo FindIst()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }
    private static DateTime ToIst(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ist);

    private async Task<TrackOrderResponse> BuildTrackingAsync(Order order)
    {
        var hist = order.StatusHistory.Where(h => !h.IsDeleted).OrderBy(h => h.CreatedAt).ToList();
        DateTime? when(OrderStatus s) => hist.LastOrDefault(h => h.Status == s)?.CreatedAt ?? (s == OrderStatus.Pending ? order.CreatedAt : null);
        string? fmt(DateTime? d) => d.HasValue ? ToIst(d.Value).ToString("dd MMM yyyy, hh:mm tt") : null;

        // Processing sits between Confirmed and Packed in the fulfilment flow
        int flowIndex(OrderStatus s) => s == OrderStatus.Processing ? 1 : Array.FindIndex(FlowSteps, f => f.Status == s);
        var reachedIdx = hist.Select(h => flowIndex(h.Status)).DefaultIfEmpty(0).Max();
        var current = flowIndex(order.Status);
        var postDelivery = order.Status is OrderStatus.ReturnRequested or OrderStatus.Returned or OrderStatus.Refunded;
        if (postDelivery) current = FlowSteps.Length - 1;

        var timeline = new List<TimelineStep>();
        if (order.Status == OrderStatus.Cancelled)
        {
            // show what actually happened, then the cancellation
            for (var i = 0; i <= Math.Max(0, reachedIdx); i++)
            {
                var (st, label) = FlowSteps[i];
                timeline.Add(new TimelineStep(st.ToString(), fmt(when(st)), true, label, hist.LastOrDefault(h => h.Status == st)?.Note, false, when(st)));
            }
            var c = hist.LastOrDefault(h => h.Status == OrderStatus.Cancelled);
            timeline.Add(new TimelineStep("Cancelled", fmt(c?.CreatedAt ?? order.UpdatedAt), true, "Cancelled", c?.Note ?? order.CancellationReason, true, c?.CreatedAt ?? order.UpdatedAt));
        }
        else
        {
            for (var i = 0; i < FlowSteps.Length; i++)
            {
                var (st, label) = FlowSteps[i];
                var done = i <= current;
                timeline.Add(new TimelineStep(st.ToString(), done ? fmt(when(st)) : null, done, label,
                    hist.LastOrDefault(h => h.Status == st)?.Note, !postDelivery && i == current, done ? when(st) : null));
            }
            if (postDelivery)
            {
                foreach (var st in new[] { OrderStatus.ReturnRequested, OrderStatus.Returned, OrderStatus.Refunded })
                {
                    var h = hist.LastOrDefault(x => x.Status == st);
                    var reached = h != null || st == order.Status;
                    if (!reached && (int)st > (int)order.Status) continue;
                    timeline.Add(new TimelineStep(st.ToString(), fmt(h?.CreatedAt), true, StatusLabel(st), h?.Note, st == order.Status, h?.CreatedAt));
                }
            }
        }

        var ret = (await LatestReturnsAsync(new() { order.Id })).GetValueOrDefault(order.Id);
        var a = order.Address;
        return new TrackOrderResponse(
            order.OrderNumber,
            order.Status.ToString(),
            order.Courier,
            order.AwbNumber,
            order.CourierTrackingUrl,
            order.Status is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Refunded or OrderStatus.ReturnRequested
                ? null : order.EstimatedDelivery.HasValue ? ToIst(order.EstimatedDelivery.Value).ToString("dd MMM yyyy") : null,
            order.DeliveredAt,
            timeline,
            new AddressSummaryDto(a?.Name ?? "", a?.Phone ?? "", a?.AddressLine ?? "", a?.City ?? "", a?.State ?? "", a?.Pincode ?? ""),
            order.OrderNumber,
            order.CreatedAt,
            order.DispatchedAt,
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            order.Subtotal,
            order.CouponDiscount,
            order.DeliveryCharge,
            order.GrandTotal,
            order.CancellationReason,
            order.Items.Where(i => !i.IsDeleted).Select(MapItem).ToList(),
            hist.Select(h => new OrderHistoryDto(h.Status.ToString(), h.Note, h.CreatedAt)).ToList(),
            ret
        );
    }

    // ── Cancel order ──────────────────────────────────────────────────────
    public async Task<ApiResponse<bool>> CancelOrderAsync(int userId, int orderId, string reason)
    {
        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId && !o.IsDeleted);
        if (order == null) return ApiResponse<bool>.Fail("NOT_FOUND", "Order not found");

        if (!Cancellable.Contains(order.Status))
            return ApiResponse<bool>.Fail("CANNOT_CANCEL", order.Status == OrderStatus.Cancelled
                ? "This order is already cancelled."
                : "This order has already been dispatched and can't be cancelled. You can request a return after delivery.");

        reason = string.IsNullOrWhiteSpace(reason) ? "No reason given" : reason.Trim()[..Math.Min(300, reason.Trim().Length)];
        var by = userId.ToString();
        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = reason;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = by;

        var paidOnline = order.PaymentStatus == PaymentStatus.Paid && order.PaymentMethod != PaymentMethod.CashOnDelivery;
        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.Cancelled,
            Note = $"Cancelled by customer: {reason}" + (paidOnline ? " — refund will be issued to the original payment method" : ""),
            UpdatedBy = by
        });

        await StoreRules.RestoreAsync(_db, order.OrderNumber, order.OrderNumber,
            order.Items.Where(i => !i.IsDeleted).Select(i => (i.ProductId, i.Quantity)), $"Order #{order.OrderNumber} cancelled", by);

        await _db.SaveChangesAsync();
        await _invoices.SyncWithOrderAsync(order.Id, by);
        _emails.StatusChanged(order.Id, OrderStatus.Cancelled, reason, CancelSource.Customer);
        return ApiResponse<bool>.Ok(true, paidOnline
            ? "Order cancelled. Your refund will be processed to the original payment method."
            : "Order cancelled successfully");
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private async Task<Dictionary<int, OrderReturnInfoDto>> LatestReturnsAsync(List<int> orderIds)
    {
        var rets = await _db.ReturnOrders.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && !r.IsDeleted && r.Status != ReturnStatus.Cancelled)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        return rets.GroupBy(r => r.OrderId).ToDictionary(g => g.Key, g =>
        {
            var r = g.First();
            return new OrderReturnInfoDto(r.Id, r.ReturnNumber, r.Status.ToString(), r.Reason, r.GrandRefundAmount,
                r.RefundMethod.ToString(), r.RefundReferenceId, r.RefundCompletedAt, r.RejectionReason,
                r.CourierName, r.AwbNumber, r.CreatedAt);
        });
    }

    /// Money actually returned: gateway refunds + refunds recorded on completed returns (COD/manual).
    private async Task<Dictionary<int, decimal>> RefundTotalsAsync(List<int> orderIds)
    {
        var gateway = await _db.Refunds.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && !r.IsDeleted && r.Status != RefundStatus.Failed && r.Status != RefundStatus.Cancelled)
            .Select(r => new { r.OrderId, r.Amount, r.ReturnOrderId }).ToListAsync();
        var linked = gateway.Where(g => g.ReturnOrderId != null).Select(g => g.ReturnOrderId!.Value).ToHashSet();
        var viaReturns = await _db.ReturnOrders.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && !r.IsDeleted && r.Status == ReturnStatus.Refunded)
            .Select(r => new { r.Id, r.OrderId, r.GrandRefundAmount }).ToListAsync();
        var result = gateway.GroupBy(g => g.OrderId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        foreach (var r in viaReturns.Where(r => !linked.Contains(r.Id)))
            result[r.OrderId] = result.GetValueOrDefault(r.OrderId) + r.GrandRefundAmount;
        return result;
    }

    private static OrderItemDto MapItem(OrderItem i) => new(
        i.ProductId, i.ProductName, i.Weight, i.Price, i.Mrp, i.Quantity, i.LineTotal,
        i.Id, i.VariantId, i.Product?.ImageUrl, i.Product?.Slug);

    private static OrderDto MapOrderDto(Order o, OrderReturnInfoDto? ret, decimal refunded, List<OrderHistoryDto>? history, OrderPaymentInfoDto? pay) =>
        new(o.Id, o.OrderNumber, o.Status, o.PaymentStatus,
            o.PaymentMethod.ToString(),
            o.SubtotalMrp, o.Subtotal, o.Discount, o.CouponDiscount,
            o.DeliveryCharge, o.GrandTotal,
            o.CouponCode, o.Notes, o.Courier, o.AwbNumber,
            o.EstimatedDelivery, o.DeliveredAt, o.CreatedAt,
            o.Items.Where(i => !i.IsDeleted).Select(MapItem).ToList(),
            new AddressSummaryDto(o.Address?.Name ?? "", o.Address?.Phone ?? "", o.Address?.AddressLine ?? "",
                o.Address?.City ?? "", o.Address?.State ?? "", o.Address?.Pincode ?? ""),
            o.CourierTrackingUrl,
            o.DispatchedAt,
            o.CancellationReason,
            Cancellable.Contains(o.Status),
            o.Status == OrderStatus.Delivered && (ret == null || ret.Status is "Rejected"),
            refunded,
            history,
            ret,
            pay
        );
}
