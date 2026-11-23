using WahSpices.API.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

/// <summary>Per-product website sales: who ordered it, pack sizes, quantities, order + payment status, revenue.</summary>
[Route("api/admin/product-sales")]
[RequireModule("Products", "Orders")]
public class AdminProductSalesController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminProductSalesController(AppDbContext db) => _db = db;

    [HttpGet("{productId:int}")]
    public async Task<IActionResult> Get(int productId,
        [FromQuery] string? status, [FromQuery] string? paymentStatus, [FromQuery] string? search,
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);
        var product = await _db.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Name, p.Slug, p.ImageUrl }).FirstOrDefaultAsync();
        if (product == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Product not found"));

        var lines = _db.OrderItems.AsNoTracking()
            .Where(i => i.ProductId == productId && !i.IsDeleted && !i.Order.IsDeleted);
        if (fromDate.HasValue) lines = lines.Where(i => i.Order.CreatedAt >= fromDate.Value.Date);
        if (toDate.HasValue) lines = lines.Where(i => i.Order.CreatedAt < toDate.Value.Date.AddDays(1));

        // Summary over all (date-filtered) lines; cancelled orders don't count as sales
        var sold = lines.Where(i => i.Order.Status != OrderStatus.Cancelled);
        var byVariant = await sold.GroupBy(i => i.Weight)
            .Select(g => new { Weight = g.Key, Orders = g.Select(x => x.OrderId).Distinct().Count(), Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Units).ToListAsync();
        var paidStatuses = new[] { PaymentStatus.Paid, PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded };
        var summary = new
        {
            Orders = await sold.Select(i => i.OrderId).Distinct().CountAsync(),
            Customers = await sold.Select(i => i.Order.UserId).Distinct().CountAsync(),
            Units = await sold.SumAsync(i => (int?)i.Quantity) ?? 0,
            Revenue = await sold.SumAsync(i => (decimal?)i.LineTotal) ?? 0,
            PaidRevenue = await sold.Where(i => paidStatuses.Contains(i.Order.PaymentStatus)).SumAsync(i => (decimal?)i.LineTotal) ?? 0,
            PendingRevenue = await sold.Where(i => i.Order.PaymentStatus == PaymentStatus.Pending || i.Order.PaymentStatus == PaymentStatus.Initiated).SumAsync(i => (decimal?)i.LineTotal) ?? 0,
            CancelledUnits = await lines.Where(i => i.Order.Status == OrderStatus.Cancelled).SumAsync(i => (int?)i.Quantity) ?? 0,
            ReturnedUnits = await _db.ReturnItems.Where(r => r.ProductId == productId && !r.IsDeleted
                && r.ReturnOrder.Status != ReturnStatus.Cancelled && r.ReturnOrder.Status != ReturnStatus.Rejected).SumAsync(r => (int?)r.Quantity) ?? 0,
            ByVariant = byVariant,
        };

        var rows = lines;
        if (!string.IsNullOrWhiteSpace(status) && status != "All" && Enum.TryParse<OrderStatus>(status, true, out var os))
            rows = rows.Where(i => i.Order.Status == os);
        if (!string.IsNullOrWhiteSpace(paymentStatus) && paymentStatus != "All" && Enum.TryParse<PaymentStatus>(paymentStatus, true, out var ps))
            rows = rows.Where(i => i.Order.PaymentStatus == ps);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            rows = rows.Where(i => i.Order.OrderNumber.Contains(s) || i.Order.User.Name.Contains(s) || i.Order.User.Email.Contains(s) || i.Order.User.Phone.Contains(s));
        }
        var total = await rows.CountAsync();
        var items = await rows.OrderByDescending(i => i.Order.CreatedAt).Skip((page - 1) * limit).Take(limit)
            .Select(i => new
            {
                i.Id, i.OrderId, i.Order.OrderNumber, OrderDate = i.Order.CreatedAt,
                Customer = new { i.Order.User.Id, i.Order.User.Name, i.Order.User.Email, i.Order.User.Phone },
                City = i.Order.Address.City,
                i.Weight, i.VariantId, i.Quantity, i.Price, i.Mrp, i.LineTotal,
                OrderStatus = i.Order.Status.ToString(), PaymentStatus = i.Order.PaymentStatus.ToString(), PaymentMethod = i.Order.PaymentMethod.ToString(),
                ReturnedQty = _db.ReturnItems.Where(r => r.OrderItemId == i.Id && !r.IsDeleted
                    && r.ReturnOrder.Status != ReturnStatus.Cancelled && r.ReturnOrder.Status != ReturnStatus.Rejected).Sum(r => (int?)r.Quantity) ?? 0,
            }).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Product = product, Summary = summary, Items = items, Pagination = new { total, page, limit } }));
    }
}
