using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/orders")]
[RequireModule("Orders")]
public class AdminOrdersController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly IOrderEmails _orderEmails;
    private readonly IInvoiceService _invoices;
    private readonly ILogger<AdminOrdersController> _logger;

    public AdminOrdersController(AppDbContext db, IOrderEmails orderEmails, IInvoiceService invoices, ILogger<AdminOrdersController> logger)
    {
        _db = db;
        _orderEmails = orderEmails;
        _invoices = invoices;
        _logger = logger;
    }

    private static readonly OrderStatus[] ActiveStatuses =
    {
        OrderStatus.Pending, OrderStatus.Processing, OrderStatus.Confirmed, OrderStatus.Packed,
        OrderStatus.Dispatched, OrderStatus.InTransit, OrderStatus.OutForDelivery,
    };

    /// <summary>Get all orders with filters</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 500);
        var query = _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var statusEnum))
            query = query.Where(o => o.Status == statusEnum);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o =>
                o.OrderNumber.Contains(search) ||
                o.User.Name.Contains(search) ||
                o.User.Phone.Contains(search));

        if (dateFrom.HasValue) query = query.Where(o => o.CreatedAt >= dateFrom);
        if (dateTo.HasValue)   query = query.Where(o => o.CreatedAt <= dateTo);

        var total = await query.CountAsync();
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                Customer = new { o.User.Id, o.User.Name, o.User.Phone, o.User.Email },
                City = o.Address.City,
                ItemCount = o.Items.Count,
                o.GrandTotal,
                Status = o.Status.ToString(),
                PaymentStatus = o.PaymentStatus.ToString(),
                PaymentMethod = o.PaymentMethod.ToString(),
                o.Courier,
                o.AwbNumber,
                o.CreatedAt,
                Date = o.CreatedAt.ToString("dd MMM yyyy")
            })
            .ToListAsync();

        // Counts across ALL orders (not just the current filter/page) for the KPI tiles + status chips
        var byStatusRaw = await _db.Orders.GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var byStatus = byStatusRaw.ToDictionary(x => x.Status.ToString(), x => x.Count);

        var summary = new
        {
            Total       = total,
            All         = byStatusRaw.Sum(x => x.Count),
            Active      = byStatusRaw.Where(x => ActiveStatuses.Contains(x.Status)).Sum(x => x.Count),
            Processing  = byStatusRaw.Where(x => x.Status == OrderStatus.Processing).Sum(x => x.Count),
            Delivered   = byStatusRaw.Where(x => x.Status == OrderStatus.Delivered).Sum(x => x.Count),
            Cancelled   = byStatusRaw.Where(x => x.Status == OrderStatus.Cancelled).Sum(x => x.Count),
            ByStatus    = byStatus,
        };

        return Ok(ApiResponse<object>.Ok(new { Orders = orders, Summary = summary, Pagination = new { total, page, limit } }));
    }

    /// <summary>Get full order detail</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id)
    {
        var order = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found."));

        return Ok(ApiResponse<object>.Ok(new
        {
            order.Id,
            order.OrderNumber,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            PaymentMethod = order.PaymentMethod.ToString(),
            order.CreatedAt,
            order.GrandTotal,
            order.SubtotalMrp,
            order.Subtotal,
            order.Discount,
            order.CouponDiscount,
            order.DeliveryCharge,
            order.CouponCode,
            order.Courier,
            order.AwbNumber,
            order.Notes,
            Customer = new { order.User.Name, order.User.Email, order.User.Phone },
            Address  = order.Address == null ? null : new { order.Address.Name, order.Address.AddressLine, order.Address.City, order.Address.State, order.Address.Pincode, order.Address.Phone },
            Items    = order.Items.Where(i => !i.IsDeleted).Select(i => new { i.Id, i.ProductId, i.ProductName, i.Weight, i.Price, i.Mrp, i.Quantity, i.LineTotal }),
            History  = order.StatusHistory.OrderByDescending(h => h.CreatedAt).Select(h => new { Status = h.Status.ToString(), h.Note, h.UpdatedBy, h.CreatedAt })
        }));
    }

    /// <summary>Update order status</summary>
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await _db.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found."));

        var raw = (request.Status ?? "").Replace(" ", "");
        if (!Enum.TryParse<OrderStatus>(raw, true, out var newStatus) || !Enum.IsDefined(newStatus) || int.TryParse(raw, out _))
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS",
                $"Invalid order status '{request.Status}'. Allowed: {string.Join(", ", Enum.GetNames<OrderStatus>())}."));
        if (newStatus == order.Status)
            return BadRequest(ApiResponse<object>.Fail("NO_CHANGE", $"Order is already {newStatus}."));

        var prev = order.Status;
        order.Status = newStatus;
        if (!string.IsNullOrWhiteSpace(request.Courier)) order.Courier = request.Courier;
        if (!string.IsNullOrWhiteSpace(request.AwbNumber)) order.AwbNumber = request.AwbNumber;
        if (newStatus == OrderStatus.Delivered) order.DeliveredAt = DateTime.UtcNow;
        if (newStatus == OrderStatus.Dispatched) order.DispatchedAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = AdminName;

        _db.OrderStatusHistories.Add(new Domain.Entities.OrderStatusHistory
        {
            OrderId = id,
            Status = newStatus,
            Note = request.Note,
            UpdatedBy = AdminName
        });

        // Cash on Delivery is collected on delivery
        if (newStatus == OrderStatus.Delivered && order.PaymentMethod == PaymentMethod.CashOnDelivery && order.PaymentStatus == PaymentStatus.Pending)
            order.PaymentStatus = PaymentStatus.Paid;

        // Cancelling puts the reserved stock back (no-op if it was never deducted / already restored)
        if (newStatus == OrderStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(order.CancellationReason)) order.CancellationReason = request.Note ?? "Cancelled by store";
            var lines = await _db.OrderItems.Where(i => i.OrderId == id && !i.IsDeleted).Select(i => new { i.ProductId, i.Quantity }).ToListAsync();
            await WahSpices.Infrastructure.Services.StoreRules.RestoreAsync(_db, order.OrderNumber, order.OrderNumber,
                lines.Select(l => (l.ProductId, l.Quantity)), $"Order #{order.OrderNumber} cancelled by {AdminName}", AdminName);
        }

        await _db.SaveChangesAsync();
        await _invoices.SyncWithOrderAsync(order.Id, AdminName);   // invoice → Paid on COD delivery, Cancelled with the order

        // Email the customer (queued — branded status template with tracker; never blocks or fails this request).
        // SMS/WhatsApp gateways aren't configured.
        var notificationSent = EmailAddressRules.IsDeliverable(order.User?.Email);
        if (notificationSent)
            _orderEmails.StatusChanged(order.Id, newStatus, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                newStatus == OrderStatus.Cancelled ? CancelSource.Store : null);

        return Ok(ApiResponse<object>.Ok(new
        {
            Id = order.Id,
            OrderId = order.OrderNumber,
            PreviousStatus = prev.ToString(),
            Status = newStatus.ToString(),
            order.Courier,
            order.AwbNumber,
            NotificationSent = notificationSent
        }, $"Order status updated to {newStatus}"));
    }

    /// <summary>Generate and send invoice for order</summary>
    [HttpPost("{id:int}/invoice")]
    public async Task<IActionResult> GenerateInvoice(int id)
    {
        // Same as POST /api/admin/invoices/generate/{orderId}: returns the existing invoice or creates one
        try
        {
            var invoice = await _invoices.GenerateFromOrderAsync(id);
            return Ok(ApiResponse<object>.Ok(invoice, "Invoice ready"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
    }
}
