using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Enums;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/dashboard")]
[RequireModule("Dashboard")]
public class AdminDashboardController : AdminBaseController
{
    private readonly AppDbContext _db;

    public AdminDashboardController(AppDbContext db) => _db = db;

    /// <summary>Get KPI statistics</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] string period = "month")
    {
        var now = DateTime.UtcNow;
        var (fromCurrent, fromPrev) = period switch
        {
            "today" => (now.Date, now.Date.AddDays(-1)),
            "week"  => (now.AddDays(-7), now.AddDays(-14)),
            "year"  => (now.AddDays(-365), now.AddDays(-730)),
            _       => (now.AddDays(-30), now.AddDays(-60))
        };

        var currentRevenue =
            (await _db.Invoices.Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= fromCurrent).SumAsync(i => (decimal?)i.TotalAmount) ?? 0) +
            (await _db.Orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= fromCurrent).SumAsync(o => (decimal?)o.GrandTotal) ?? 0) +
            (await _db.SalesOrderBills.Where(b => !b.IsDeleted && b.CreatedAt >= fromCurrent).SumAsync(b => (decimal?)b.TotalAmount) ?? 0);

        var prevRevenue =
            (await _db.Invoices.Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= fromPrev && i.CreatedAt < fromCurrent).SumAsync(i => (decimal?)i.TotalAmount) ?? 0) +
            (await _db.Orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= fromPrev && o.CreatedAt < fromCurrent).SumAsync(o => (decimal?)o.GrandTotal) ?? 0) +
            (await _db.SalesOrderBills.Where(b => !b.IsDeleted && b.CreatedAt >= fromPrev && b.CreatedAt < fromCurrent).SumAsync(b => (decimal?)b.TotalAmount) ?? 0);

        var currentOrders  = await _db.Orders.CountAsync(o => o.CreatedAt >= fromCurrent);
        var prevOrders     = await _db.Orders.CountAsync(o => o.CreatedAt >= fromPrev && o.CreatedAt < fromCurrent);
        var totalCustomers = await _db.Users.CountAsync(u => !u.IsDeleted);
        var newCustCurrent = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= fromCurrent);
        var newCustPrev    = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= fromPrev && u.CreatedAt < fromCurrent);
        var prevAov        = prevOrders > 0 ? Math.Round(prevRevenue / prevOrders, 0) : 0;
        var pendingReturns = await _db.ReturnOrders.CountAsync(r => r.Status == ReturnStatus.Requested);
        var lowStock       = await _db.InventoryItems.CountAsync(i => i.CurrentStock <= i.ReorderPoint);
        var pendingOrders  = await _db.Orders.CountAsync(o => o.Status == OrderStatus.Processing || o.Status == OrderStatus.Pending);

        static string CalcChange(decimal current, decimal prev) =>
            prev == 0 ? "N/A" : $"{((current - prev) / prev * 100):+0.0;-0.0}%";

        var stats = new DashboardStats(
            Revenue: new KpiStat(currentRevenue, prevRevenue, CalcChange(currentRevenue, prevRevenue)),
            Orders: new KpiStat(currentOrders, prevOrders, CalcChange(currentOrders, prevOrders)),
            // customers: total now vs total at the start of the period; change = growth of new sign-ups vs previous period
            Customers: new KpiStat(totalCustomers, totalCustomers - newCustCurrent, CalcChange(newCustCurrent, newCustPrev)),
            AvgOrderValue: currentOrders > 0
                ? new KpiStat(Math.Round(currentRevenue / currentOrders, 0), prevAov, CalcChange(Math.Round(currentRevenue / currentOrders, 0), prevAov))
                : new KpiStat(0, prevAov, "N/A"),
            LowStockCount: lowStock,
            PendingOrders: pendingOrders,
            PendingApprovals: pendingReturns
        );

        return Ok(ApiResponse<DashboardStats>.Ok(stats));
    }

    /// <summary>Monthly revenue chart data</summary>
    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] int year = 0)
    {
        if (year == 0) year = DateTime.UtcNow.Year;

        var data = await _db.Orders
            .Where(o => o.CreatedAt.Year == year && o.PaymentStatus == PaymentStatus.Paid)
            .GroupBy(o => o.CreatedAt.Month)
            .Select(g => new
            {
                Month   = g.Key,
                Revenue = g.Sum(o => o.GrandTotal),
                Orders  = g.Count()
            })
            .ToListAsync();

        var months = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        var result = Enumerable.Range(1, 12).Select(m =>
        {
            var row = data.FirstOrDefault(d => d.Month == m);
            return new MonthlyRevenue(months[m - 1], row?.Revenue ?? 0, row?.Orders ?? 0);
        }).ToList();

        return Ok(ApiResponse<List<MonthlyRevenue>>.Ok(result));
    }

    /// <summary>Recent orders for dashboard</summary>
    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int limit = 6)
    {
        var orders = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new
            {
                o.OrderNumber,
                Customer = o.User.Name,
                City = o.Address.City,
                Items = o.Items.Count(),
                o.GrandTotal,
                Status = o.Status.ToString(),
                Date = o.CreatedAt.ToString("dd MMM yyyy")
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(orders));
    }

    /// <summary>Top selling products</summary>
    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProducts([FromQuery] int limit = 6)
    {
        var top = await _db.OrderItems
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new
            {
                g.Key.ProductName,
                Sales   = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.Sales)
            .Take(limit)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(top));
    }
}
