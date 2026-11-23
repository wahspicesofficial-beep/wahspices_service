using WahSpices.API.Authorization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Admin → Payments: one row per website order (COD included — the Payments table only holds gateway attempts),
/// with method, payment status, amount, gateway ids and refunds (gateway refunds + refunds recorded on returns).
/// </summary>
[Route("api/admin/payments/ledger")]
[RequireModule("Payments")]
public class AdminPaymentLedgerController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminPaymentLedgerController(AppDbContext db) => _db = db;

    private IQueryable<Order> Filtered(string? status, string? method, DateTime? fromDate, DateTime? toDate, string? search, string? orderStatus)
    {
        var q = _db.Orders.AsNoTracking().Where(o => !o.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            if (!Enum.TryParse<PaymentStatus>(status, true, out var ps) || int.TryParse(status, out _))
                throw new ArgumentException($"Invalid payment status '{status}'");
            q = q.Where(o => o.PaymentStatus == ps);
        }
        if (!string.IsNullOrWhiteSpace(method) && method != "All")
        {
            if (!Enum.TryParse<PaymentMethod>(method, true, out var pm) || int.TryParse(method, out _))
                throw new ArgumentException($"Invalid payment method '{method}'");
            q = q.Where(o => o.PaymentMethod == pm);
        }
        if (!string.IsNullOrWhiteSpace(orderStatus) && orderStatus != "All" && Enum.TryParse<OrderStatus>(orderStatus, true, out var os))
            q = q.Where(o => o.Status == os);
        if (fromDate.HasValue) q = q.Where(o => o.CreatedAt >= fromDate.Value.Date);
        if (toDate.HasValue) q = q.Where(o => o.CreatedAt < toDate.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => o.OrderNumber.Contains(s) || o.User.Name.Contains(s) || o.User.Email.Contains(s) || o.User.Phone.Contains(s)
                || (o.RazorpayOrderId != null && o.RazorpayOrderId.Contains(s)) || (o.RazorpayPaymentId != null && o.RazorpayPaymentId.Contains(s))
                || _db.Payments.Any(p => p.OrderId == o.Id && (p.RazorpayOrderId.Contains(s) || (p.RazorpayPaymentId != null && p.RazorpayPaymentId.Contains(s)))));
        }
        return q;
    }

    /// Refunded money per order: gateway refunds (not failed/cancelled) + refunded returns without a linked gateway refund.
    private async Task<Dictionary<int, (decimal Amount, int Count)>> RefundsFor(IQueryable<int> orderIds)
    {
        var gateway = await _db.Refunds.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && !r.IsDeleted && r.Status != RefundStatus.Failed && r.Status != RefundStatus.Cancelled)
            .Select(r => new { r.OrderId, r.Amount, r.ReturnOrderId }).ToListAsync();
        var linked = gateway.Where(g => g.ReturnOrderId != null).Select(g => g.ReturnOrderId!.Value).ToHashSet();
        var viaReturns = await _db.ReturnOrders.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && !r.IsDeleted && r.Status == ReturnStatus.Refunded)
            .Select(r => new { r.Id, r.OrderId, r.GrandRefundAmount }).ToListAsync();
        var map = new Dictionary<int, (decimal, int)>();
        foreach (var g in gateway) { var c = map.GetValueOrDefault(g.OrderId); map[g.OrderId] = (c.Item1 + g.Amount, c.Item2 + 1); }
        foreach (var r in viaReturns.Where(r => !linked.Contains(r.Id))) { var c = map.GetValueOrDefault(r.OrderId); map[r.OrderId] = (c.Item1 + r.GrandRefundAmount, c.Item2 + 1); }
        return map;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status, [FromQuery] string? method, [FromQuery] string? orderStatus,
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);
        IQueryable<Order> q;
        try { q = Filtered(status, method, fromDate, toDate, search, orderStatus); }
        catch (ArgumentException ex) { return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", ex.Message)); }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * limit).Take(limit)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.CreatedAt, o.GrandTotal, o.PaymentMethod, o.PaymentStatus, o.Status,
                o.RazorpayOrderId, o.RazorpayPaymentId, o.DeliveredAt,
                Customer = new { o.User.Id, o.User.Name, o.User.Email, o.User.Phone },
                Pay = _db.Payments.Where(p => p.OrderId == o.Id && !p.IsDeleted).OrderByDescending(p => p.CreatedAt)
                    .Select(p => new { p.Id, p.RazorpayOrderId, p.RazorpayPaymentId, p.Status, p.UpdatedAt, p.CreatedAt, p.ErrorDescription }).FirstOrDefault(),
                ReturnNumber = _db.ReturnOrders.Where(r => r.OrderId == o.Id && !r.IsDeleted && r.Status != ReturnStatus.Cancelled)
                    .OrderByDescending(r => r.CreatedAt).Select(r => r.ReturnNumber).FirstOrDefault(),
            }).ToListAsync();

        var pageIds = rows.Select(r => r.Id).ToList();
        var refunds = await RefundsFor(_db.Orders.Where(o => pageIds.Contains(o.Id)).Select(o => o.Id));
        var items = rows.Select(o =>
        {
            var rf = refunds.GetValueOrDefault(o.Id);
            return new
            {
                OrderId = o.Id, o.OrderNumber, o.CreatedAt, Amount = o.GrandTotal,
                Method = o.PaymentMethod.ToString(), PaymentStatus = o.PaymentStatus.ToString(), OrderStatus = o.Status.ToString(),
                PaymentId = o.Pay?.Id,
                GatewayOrderId = o.Pay?.RazorpayOrderId ?? o.RazorpayOrderId,
                GatewayPaymentId = o.Pay?.RazorpayPaymentId ?? o.RazorpayPaymentId,
                GatewayError = o.Pay?.ErrorDescription,
                PaidAt = o.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
                    ? (o.Pay != null ? o.Pay.UpdatedAt ?? o.Pay.CreatedAt : o.DeliveredAt) : (DateTime?)null,
                RefundedAmount = rf.Amount, RefundCount = rf.Count,
                o.ReturnNumber, o.Customer,
            };
        });

        // Totals over the whole filtered set (not just this page)
        var agg = await q.GroupBy(o => new { o.PaymentStatus, o.PaymentMethod, Cancelled = o.Status == OrderStatus.Cancelled })
            .Select(g => new { g.Key.PaymentStatus, g.Key.PaymentMethod, g.Key.Cancelled, Count = g.Count(), Amount = g.Sum(o => o.GrandTotal) })
            .ToListAsync();
        var allRefunds = await RefundsFor(q.Select(o => o.Id));
        var collectedStatuses = new[] { PaymentStatus.Paid, PaymentStatus.Refunded, PaymentStatus.PartiallyRefunded };
        var collected = agg.Where(a => collectedStatuses.Contains(a.PaymentStatus)).Sum(a => a.Amount);
        var refunded = allRefunds.Values.Sum(v => v.Amount);
        var totals = new
        {
            Orders = agg.Sum(a => a.Count),
            Amount = agg.Sum(a => a.Amount),
            Collected = collected,
            Refunded = refunded,
            Net = collected - refunded,
            Pending = agg.Where(a => (a.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Initiated) && !a.Cancelled).Sum(a => a.Amount),
            PendingCount = agg.Where(a => (a.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Initiated) && !a.Cancelled).Sum(a => a.Count),
            CodPending = agg.Where(a => a.PaymentMethod == PaymentMethod.CashOnDelivery && a.PaymentStatus == PaymentStatus.Pending && !a.Cancelled).Sum(a => a.Amount),
            Failed = agg.Where(a => a.PaymentStatus == PaymentStatus.Failed).Sum(a => a.Amount),
            FailedCount = agg.Where(a => a.PaymentStatus == PaymentStatus.Failed).Sum(a => a.Count),
            ByMethod = agg.GroupBy(a => a.PaymentMethod).Select(g => new { Method = g.Key.ToString(), Count = g.Sum(x => x.Count), Amount = g.Sum(x => x.Amount) }),
            ByStatus = agg.GroupBy(a => a.PaymentStatus).ToDictionary(g => g.Key.ToString(), g => g.Sum(x => x.Count)),
        };

        return Ok(ApiResponse<object>.Ok(new { Items = items, Totals = totals, Pagination = new { total, page, limit } }));
    }

    /// <summary>Everything about one order's money: gateway attempts, refunds, returns, payment-related history.</summary>
    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetOne(int orderId)
    {
        var o = await _db.Orders.AsNoTracking().Include(x => x.User).Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.IsDeleted);
        if (o == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found"));
        var payments = await _db.Payments.AsNoTracking().Include(p => p.Refunds)
            .Where(p => p.OrderId == orderId && !p.IsDeleted).OrderByDescending(p => p.CreatedAt).ToListAsync();
        var returns = await _db.ReturnOrders.AsNoTracking().Where(r => r.OrderId == orderId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        var rf = (await RefundsFor(_db.Orders.Where(x => x.Id == orderId).Select(x => x.Id))).GetValueOrDefault(orderId);

        return Ok(ApiResponse<object>.Ok(new
        {
            OrderId = o.Id, o.OrderNumber, o.CreatedAt, Amount = o.GrandTotal, o.Subtotal, o.CouponDiscount, o.DeliveryCharge,
            Method = o.PaymentMethod.ToString(), PaymentStatus = o.PaymentStatus.ToString(), OrderStatus = o.Status.ToString(),
            GatewayOrderId = o.RazorpayOrderId, GatewayPaymentId = o.RazorpayPaymentId,
            RefundedAmount = rf.Amount,
            Customer = new { o.User.Id, o.User.Name, o.User.Email, o.User.Phone },
            Payments = payments.Select(p => new
            {
                p.Id, GatewayOrderId = p.RazorpayOrderId, GatewayPaymentId = p.RazorpayPaymentId, p.Amount, p.Currency,
                Status = p.Status.ToString(), Method = p.Method.ToString(), p.ErrorCode, p.ErrorDescription, p.CreatedAt, p.UpdatedAt,
                Refunds = p.Refunds.Where(r => !r.IsDeleted).OrderByDescending(r => r.CreatedAt).Select(r => new
                {
                    r.Id, GatewayRefundId = r.RazorpayRefundId, r.Amount, Status = r.Status.ToString(), Method = r.Method.ToString(),
                    r.Reason, r.Notes, r.InitiatedBy, r.ProcessedAt, r.CreatedAt, r.ReturnOrderId,
                }),
            }),
            Returns = returns.Select(r => new
            {
                r.Id, r.ReturnNumber, Status = r.Status.ToString(), RefundAmount = r.GrandRefundAmount, RefundMethod = r.RefundMethod.ToString(),
                r.RefundReferenceId, r.RefundCompletedAt, r.CreditNoteNumber, r.CreatedAt,
            }),
            History = o.StatusHistory.Where(h => !h.IsDeleted).OrderByDescending(h => h.CreatedAt)
                .Select(h => new { Status = h.Status.ToString(), h.Note, h.UpdatedBy, h.CreatedAt }),
        }));
    }

    /// <summary>CSV of the filtered ledger (same filters as the list).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? status, [FromQuery] string? method, [FromQuery] string? orderStatus,
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] string? search)
    {
        IQueryable<Order> q;
        try { q = Filtered(status, method, fromDate, toDate, search, orderStatus); }
        catch (ArgumentException ex) { return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", ex.Message)); }
        var rows = await q.OrderByDescending(o => o.CreatedAt).Take(20000)
            .Select(o => new { o.Id, o.OrderNumber, o.CreatedAt, o.User.Name, o.User.Email, o.User.Phone, o.PaymentMethod, o.PaymentStatus, o.Status, o.GrandTotal, o.RazorpayOrderId, o.RazorpayPaymentId })
            .ToListAsync();
        var refunds = await RefundsFor(q.Select(o => o.Id));
        static string Esc(object? v) { var s = v?.ToString() ?? ""; return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s; }
        var sb = new StringBuilder("Order,Date,Customer,Email,Phone,Method,Payment status,Order status,Amount,Refunded,Gateway order id,Gateway payment id\n");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new object?[] { r.OrderNumber, r.CreatedAt.ToString("yyyy-MM-dd HH:mm"), r.Name, r.Email, r.Phone,
                r.PaymentMethod, r.PaymentStatus, r.Status, r.GrandTotal, refunds.GetValueOrDefault(r.Id).Amount, r.RazorpayOrderId, r.RazorpayPaymentId }.Select(Esc)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"payments-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
