using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/analytics")]
[RequireModule("Analytics", "Dashboard")]
public class AdminAnalyticsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminAnalyticsController(AppDbContext db) => _db = db;

    // ── period → date range ──────────────────────────────────────────────────
    private static (DateTime from, DateTime fromPrev, DateTime to) PeriodRange(string period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            "week"   => (now.AddDays(-7),   now.AddDays(-14),  now),
            "3month" => (now.AddDays(-90),  now.AddDays(-180), now),
            "year"   => (now.AddDays(-365), now.AddDays(-730), now),
            _        => (now.AddDays(-30),  now.AddDays(-60),  now)   // "month"
        };
    }

    // ── helpers: combined revenue from all sources ───────────────────────────
    // Revenue sources:
    //   1. Invoices       (sales invoices, not cancelled)
    //   2. Orders         (e-commerce, PaymentStatus = Paid)
    //   3. SalesOrderBills (B2B retail bills, not deleted)

    private async Task<decimal> RevenueInRange(DateTime from, DateTime to)
    {
        var invoices = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled
                     && i.CreatedAt >= from && i.CreatedAt <= to)
            .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;

        var orders = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid
                     && o.CreatedAt >= from && o.CreatedAt <= to)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;

        var bills = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt >= from && b.CreatedAt <= to)
            .SumAsync(b => (decimal?)b.TotalAmount) ?? 0;

        return invoices + orders + bills;
    }

    private async Task<int> OrderCountInRange(DateTime from, DateTime to)
    {
        var invoices = await _db.Invoices
            .CountAsync(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled
                          && i.CreatedAt >= from && i.CreatedAt <= to);
        var orders = await _db.Orders
            .CountAsync(o => o.CreatedAt >= from && o.CreatedAt <= to);
        var bills = await _db.SalesOrderBills
            .CountAsync(b => !b.IsDeleted && b.CreatedAt >= from && b.CreatedAt <= to);
        return invoices + orders + bills;
    }

    // ── 1. Performance overview cards ────────────────────────────────────────
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] string period = "month")
    {
        var (from, fromPrev, to) = PeriodRange(period);

        var revCurr = await RevenueInRange(from, to);
        var revPrev = await RevenueInRange(fromPrev, from);

        var ordCurr = await OrderCountInRange(from, to);
        var ordPrev = await OrderCountInRange(fromPrev, from);

        var custCurr  = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= from && u.CreatedAt <= to);
        var custPrev  = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= fromPrev && u.CreatedAt < from);
        var custTotal = await _db.Users.CountAsync(u => !u.IsDeleted);

        var aov     = ordCurr > 0 ? Math.Round(revCurr / ordCurr, 0) : 0;
        var aovPrev = ordPrev > 0 ? Math.Round(revPrev / ordPrev, 0) : 0;

        var pendingOrders  = await _db.Orders.CountAsync(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Processing);
        var unpaidInvoices = await _db.Invoices.CountAsync(i => !i.IsDeleted && i.Status == InvoiceStatus.Unpaid);
        var lowStock       = await _db.InventoryItems.CountAsync(i => i.CurrentStock <= i.ReorderPoint);

        static string Pct(decimal curr, decimal prev) =>
            prev == 0 ? "N/A" : $"{((curr - prev) / prev * 100):+0.0;-0.0}%";

        return Ok(ApiResponse<object>.Ok(new
        {
            Revenue        = new { Value = revCurr,  Prev = revPrev,  Change = Pct(revCurr,  revPrev)  },
            Orders         = new { Value = ordCurr,  Prev = ordPrev,  Change = Pct(ordCurr,  ordPrev)  },
            NewCustomers   = new { Value = custCurr, Prev = custPrev, Change = Pct(custCurr, custPrev), Total = custTotal },
            AvgOrderValue  = new { Value = aov,      Prev = aovPrev,  Change = Pct(aov,      aovPrev)  },
            PendingOrders  = pendingOrders,
            UnpaidInvoices = unpaidInvoices,
            LowStock       = lowStock,
            Period         = period,
            From           = from,
            To             = to
        }));
    }

    // ── 2. Monthly revenue chart ─────────────────────────────────────────────
    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] int year = 0)
    {
        if (year == 0) year = DateTime.UtcNow.Year;

        // Invoices by month
        var invData = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt.Year == year)
            .GroupBy(i => i.CreatedAt.Month)
            .Select(g => new { Month = g.Key, Revenue = g.Sum(i => i.TotalAmount), Count = g.Count() })
            .ToListAsync();

        // Orders by month
        var ordData = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt.Year == year)
            .GroupBy(o => o.CreatedAt.Month)
            .Select(g => new { Month = g.Key, Revenue = g.Sum(o => o.GrandTotal), Count = g.Count() })
            .ToListAsync();

        // SalesOrderBills by month
        var billData = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt.Year == year)
            .GroupBy(b => b.CreatedAt.Month)
            .Select(g => new { Month = g.Key, Revenue = g.Sum(b => b.TotalAmount), Count = g.Count() })
            .ToListAsync();

        var abbr = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;

        var result = Enumerable.Range(1, 12).Select(m =>
        {
            var inv  = invData.FirstOrDefault(d => d.Month == m);
            var ord  = ordData.FirstOrDefault(d => d.Month == m);
            var bill = billData.FirstOrDefault(d => d.Month == m);

            var revenue = (inv?.Revenue ?? 0) + (ord?.Revenue ?? 0) + (bill?.Revenue ?? 0);
            var count   = (inv?.Count   ?? 0) + (ord?.Count   ?? 0) + (bill?.Count   ?? 0);

            return new { Month = abbr[m - 1], Revenue = revenue, Orders = count };
        });

        return Ok(ApiResponse<object>.Ok(new { Year = year, Months = result }));
    }

    // ── 3. Revenue breakdown by source ───────────────────────────────────────
    [HttpGet("revenue-sources")]
    public async Task<IActionResult> GetRevenueSources([FromQuery] string period = "month")
    {
        var (from, _, to) = PeriodRange(period);

        var invoiceRev = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= from && i.CreatedAt <= to)
            .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;
        var invoiceCount = await _db.Invoices
            .CountAsync(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= from && i.CreatedAt <= to);

        var orderRev = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= from && o.CreatedAt <= to)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;
        var orderCount = await _db.Orders
            .CountAsync(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= from && o.CreatedAt <= to);

        var billRev = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt >= from && b.CreatedAt <= to)
            .SumAsync(b => (decimal?)b.TotalAmount) ?? 0;
        var billCount = await _db.SalesOrderBills
            .CountAsync(b => !b.IsDeleted && b.CreatedAt >= from && b.CreatedAt <= to);

        var total = invoiceRev + orderRev + billRev;

        return Ok(ApiResponse<object>.Ok(new
        {
            Total   = total,
            Sources = new[]
            {
                new { Source = "Invoices",   Revenue = invoiceRev, Count = invoiceCount, Pct = total > 0 ? Math.Round(invoiceRev / total * 100, 1) : 0m },
                new { Source = "Orders",     Revenue = orderRev,   Count = orderCount,   Pct = total > 0 ? Math.Round(orderRev   / total * 100, 1) : 0m },
                new { Source = "Sales Bills",Revenue = billRev,    Count = billCount,    Pct = total > 0 ? Math.Round(billRev    / total * 100, 1) : 0m },
            }
        }));
    }

    // ── 4. Sales by category ─────────────────────────────────────────────────
    [HttpGet("sales-by-category")]
    public async Task<IActionResult> GetSalesByCategory([FromQuery] string period = "month")
    {
        var (from, _, to) = PeriodRange(period);

        var items = await _db.OrderItems
            .Where(i => i.Order.CreatedAt >= from && i.Order.CreatedAt <= to
                     && i.Order.PaymentStatus == PaymentStatus.Paid)
            .Select(i => new { i.ProductId, i.Quantity, i.LineTotal })
            .ToListAsync();

        if (!items.Any())
            return Ok(ApiResponse<object>.Ok(new { Categories = Array.Empty<object>(), Period = period }));

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products   = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CategorySlug })
            .ToListAsync();

        var slugs      = products.Select(p => p.CategorySlug).Distinct().ToList();
        var categories = await _db.Categories
            .Where(c => slugs.Contains(c.Slug))
            .Select(c => new { c.Slug, c.Name })
            .ToListAsync();

        var productMap  = products.ToDictionary(p => p.Id, p => p.CategorySlug);
        var categoryMap = categories.ToDictionary(c => c.Slug, c => c.Name);

        var grouped = items
            .GroupBy(i => productMap.TryGetValue(i.ProductId, out var slug) ? slug : "other")
            .Select(g => new
            {
                Slug    = g.Key,
                Name    = categoryMap.TryGetValue(g.Key, out var name) ? name : g.Key,
                Revenue = Math.Round(g.Sum(i => i.LineTotal), 0),
                Units   = g.Sum(i => i.Quantity)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var totalRevenue = grouped.Sum(g => g.Revenue);
        var withPct = grouped.Select(g => new
        {
            g.Slug, g.Name, g.Revenue, g.Units,
            Pct = totalRevenue > 0 ? Math.Round(g.Revenue / totalRevenue * 100, 1) : 0m
        });

        return Ok(ApiResponse<object>.Ok(new { Categories = withPct, TotalRevenue = totalRevenue, Period = period }));
    }

    // ── 5. Top selling products ──────────────────────────────────────────────
    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProducts([FromQuery] string period = "month", [FromQuery] int limit = 8)
    {
        var (from, _, to) = PeriodRange(period);

        var top = await _db.OrderItems
            .Where(i => i.Order.CreatedAt >= from && i.Order.CreatedAt <= to
                     && i.Order.PaymentStatus == PaymentStatus.Paid)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new
            {
                Name    = g.Key.ProductName,
                Units   = g.Sum(x => x.Quantity),
                Revenue = Math.Round(g.Sum(x => x.LineTotal), 0)
            })
            .OrderByDescending(x => x.Units)
            .Take(limit)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Products = top, Period = period }));
    }

    // ── 6. Performance KPIs ──────────────────────────────────────────────────
    // period (optional): week | month | 3month | year — when given, the "new customers", "reviews" and
    // "expenses" figures cover that rolling window; when omitted they cover the current calendar month.
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis([FromQuery] string? period = null)
    {
        var now       = DateTime.UtcNow;
        var thisMonth = string.IsNullOrWhiteSpace(period)
            ? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : PeriodRange(period).from;

        var avgRating = await _db.Products
            .Where(p => !p.IsDeleted && p.Rating > 0)
            .AverageAsync(p => (double?)p.Rating) ?? 0;

        // Return rate across all invoice/order count
        var totalDocs    = await _db.Invoices.CountAsync(i => !i.IsDeleted)
                         + await _db.Orders.CountAsync();
        var totalReturns = await _db.ReturnOrders.CountAsync();
        var returnRate   = totalDocs > 0 ? Math.Round((double)totalReturns / totalDocs * 100, 1) : 0;

        var buyerCounts = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid)
            .GroupBy(o => o.UserId)
            .Select(g => new { Count = g.Count() })
            .ToListAsync();
        var totalBuyers  = buyerCounts.Count;
        var repeatBuyers = buyerCounts.Count(b => b.Count > 1);
        var repeatBuyerPct = totalBuyers > 0 ? Math.Round((double)repeatBuyers / totalBuyers * 100, 1) : 0;

        // use the real delivery timestamp (fall back to last update for legacy rows)
        var deliveredOrders = await _db.Orders
            .Where(o => o.Status == OrderStatus.Delivered && (o.DeliveredAt.HasValue || o.UpdatedAt.HasValue))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new { o.CreatedAt, DeliveredAt = o.DeliveredAt ?? o.UpdatedAt!.Value })
            .Take(200).ToListAsync();
        var avgDeliveryDays = deliveredOrders.Count > 0
            ? Math.Round(deliveredOrders.Average(o => (o.DeliveredAt - o.CreatedAt).TotalDays), 1)
            : 0;

        var newCustomers    = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= thisMonth);
        var activeProducts  = await _db.Products.CountAsync(p => !p.IsDeleted && !p.IsDraft);
        var reviewsThisMonth = await _db.Reviews.CountAsync(r => !r.IsDeleted && r.CreatedAt >= thisMonth);

        // Unpaid invoices outstanding
        var unpaidInvoices = await _db.Invoices.CountAsync(i => !i.IsDeleted && i.Status == InvoiceStatus.Unpaid);
        var unpaidAmount   = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status == InvoiceStatus.Unpaid)
            .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;

        // Total expenses this month
        var expensesThisMonth = await _db.ExpenseEntries
            .Where(e => e.Date >= thisMonth)
            .SumAsync(e => (decimal?)e.Amount) ?? 0;

        return Ok(ApiResponse<object>.Ok(new
        {
            AvgRating         = Math.Round(avgRating, 1),
            ReturnRate        = returnRate,
            RepeatBuyerPct    = repeatBuyerPct,
            AvgDeliveryDays   = avgDeliveryDays,
            NewCustomers      = newCustomers,
            ActiveProducts    = activeProducts,
            ReviewsThisMonth  = reviewsThisMonth,
            TotalBuyers       = totalBuyers,
            RepeatBuyers      = repeatBuyers,
            UnpaidInvoices    = unpaidInvoices,
            UnpaidAmount      = unpaidAmount,
            ExpensesThisMonth = expensesThisMonth,
            Period            = period ?? "calendarMonth",
            From              = thisMonth
        }));
    }

    // ── 7. Orders by status ──────────────────────────────────────────────────
    [HttpGet("order-status")]
    public async Task<IActionResult> GetOrderStatus([FromQuery] string period = "month")
    {
        var (from, _, to) = PeriodRange(period);

        var orderBreakdown = await _db.Orders
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count(), Revenue = g.Sum(o => o.GrandTotal) })
            .ToListAsync();

        var invoiceBreakdown = await _db.Invoices
            .Where(i => !i.IsDeleted && i.CreatedAt >= from && i.CreatedAt <= to)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count(), Revenue = g.Sum(i => i.TotalAmount) })
            .ToListAsync();

        var billBreakdown = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt >= from && b.CreatedAt <= to)
            .GroupBy(b => b.PaymentStatus)
            .Select(g => new { Status = g.Key ?? "Unknown", Count = g.Count(), Revenue = g.Sum(b => b.TotalAmount) })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Orders   = orderBreakdown,
            Invoices = invoiceBreakdown,
            Bills    = billBreakdown,
            Period   = period
        }));
    }

    // ── 8. Recent transactions ───────────────────────────────────────────────
    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int limit = 8)
    {
        // Combine invoices + orders + bills sorted by date
        var invoices = await _db.Invoices
            .Where(i => !i.IsDeleted)
            .OrderByDescending(i => i.CreatedAt)
            .Take(limit)
            .Select(i => new
            {
                Ref        = i.InvoiceNumber,
                Customer   = i.CustomerName,
                City       = (string?)null,
                Amount     = i.TotalAmount,
                Status     = i.Status.ToString(),
                Source     = "Invoice",
                Date       = i.CreatedAt
            })
            .ToListAsync();

        var orders = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new
            {
                Ref        = o.OrderNumber,
                Customer   = o.User.Name,
                City       = o.Address != null ? o.Address.City : (string?)null,
                Amount     = o.GrandTotal,
                Status     = o.Status.ToString(),
                Source     = "Order",
                Date       = o.CreatedAt
            })
            .ToListAsync();

        var bills = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted)
            .Include(b => b.Client)
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .Select(b => new
            {
                Ref        = b.BillNumber,
                Customer   = b.Client != null ? b.Client.Name : "B2B Client",
                City       = (string?)null,
                Amount     = b.TotalAmount,
                Status     = b.PaymentStatus ?? "Unpaid",
                Source     = "Bill",
                Date       = b.CreatedAt
            })
            .ToListAsync();

        var combined = invoices
            .Concat(orders)
            .Concat(bills)
            .OrderByDescending(x => x.Date)
            .Take(limit)
            .ToList();

        return Ok(ApiResponse<object>.Ok(combined));
    }
}
