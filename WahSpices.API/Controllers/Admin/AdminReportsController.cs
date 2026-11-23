using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/reports")]
[RequireModule("Reports")]
public class AdminReportsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminReportsController(AppDbContext db) => _db = db;

    // ── helpers ──────────────────────────────────────────────────────────────

    // A date-only "to" (e.g. ?to=2026-11-15 from a date picker) means "through the end of that day".
    private static DateTime? EndOfDay(DateTime? to) =>
        to.HasValue && to.Value.TimeOfDay == TimeSpan.Zero ? to.Value.Date.AddDays(1).AddTicks(-1) : to;

    private static string MonthLabel(int m) =>
        System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames[m - 1];

    // combined revenue for a period from all 3 sources
    private async Task<(decimal invoiceRev, decimal orderRev, decimal billRev)> RevenueParts(DateTime from, DateTime to)
    {
        var inv  = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= from && i.CreatedAt <= to)
            .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;
        var ord  = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= from && o.CreatedAt <= to)
            .SumAsync(o => (decimal?)o.GrandTotal) ?? 0;
        var bill = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt >= from && b.CreatedAt <= to)
            .SumAsync(b => (decimal?)b.TotalAmount) ?? 0;
        return (inv, ord, bill);
    }

    // ── 1. Sales Report ──────────────────────────────────────────────────────
    [HttpGet("sales")]
    public async Task<IActionResult> GetSales(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string groupBy = "day")
    {
        var f = from ?? DateTime.UtcNow.AddDays(-30);
        var t = EndOfDay(to) ?? DateTime.UtcNow;

        // --- invoices ---
        var invoices = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= f && i.CreatedAt <= t)
            .Select(i => new { i.CreatedAt, i.TotalAmount, i.CustomerName, i.InvoiceNumber, i.Status, Source = "Invoice" })
            .ToListAsync();

        // --- orders ---
        var orders = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt >= f && o.CreatedAt <= t)
            .Include(o => o.User)
            .Select(o => new { o.CreatedAt, TotalAmount = o.GrandTotal, CustomerName = o.User.Name, InvoiceNumber = o.OrderNumber, Status = o.Status.ToString(), Source = "Order" })
            .ToListAsync();

        // --- bills ---
        var bills = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt >= f && b.CreatedAt <= t)
            .Include(b => b.Client)
            .Select(b => new { b.CreatedAt, b.TotalAmount, CustomerName = b.Client != null ? b.Client.Name : "B2B", InvoiceNumber = b.BillNumber, b.PaymentStatus, Source = "Bill" })
            .ToListAsync();

        var allTxns = invoices
            .Select(x => new { x.CreatedAt, x.TotalAmount, x.CustomerName, x.InvoiceNumber, x.Source })
            .Concat(orders.Select(x => new { x.CreatedAt, x.TotalAmount, x.CustomerName, x.InvoiceNumber, x.Source }))
            .Concat(bills.Select(x  => new { x.CreatedAt, x.TotalAmount, x.CustomerName, InvoiceNumber = x.InvoiceNumber, Source = x.Source }))
            .OrderBy(x => x.CreatedAt)
            .ToList();

        // group into periods
        Func<DateTime, string> periodKey = groupBy switch
        {
            "month" => d => $"{MonthLabel(d.Month)} {d.Year}",
            "week"  => d => $"W{System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday)} {d.Year}",
            _       => d => d.ToString("dd MMM")
        };

        var grouped = allTxns
            .GroupBy(x => periodKey(x.CreatedAt))
            .Select(g => new
            {
                Period       = g.Key,
                Revenue      = g.Sum(x => x.TotalAmount),
                Transactions = g.Count(),
                InvoiceRev   = g.Where(x => x.Source == "Invoice").Sum(x => x.TotalAmount),
                OrderRev     = g.Where(x => x.Source == "Order").Sum(x => x.TotalAmount),
                BillRev      = g.Where(x => x.Source == "Bill").Sum(x => x.TotalAmount),
            })
            .ToList();

        // flat transaction list (for preview table)
        var txnList = allTxns.Select(x => new
        {
            Date     = x.CreatedAt.ToString("dd MMM yyyy"),
            Ref      = x.InvoiceNumber,
            Customer = x.CustomerName,
            Source   = x.Source,
            Amount   = x.TotalAmount,
        }).ToList();

        var totalRev = allTxns.Sum(x => x.TotalAmount);

        return Ok(ApiResponse<object>.Ok(new
        {
            DateRange  = new { From = f, To = t },
            GroupBy    = groupBy,
            Summary    = new
            {
                TotalRevenue       = totalRev,
                TotalTransactions  = allTxns.Count,
                AvgTransactionValue= allTxns.Count > 0 ? Math.Round(totalRev / allTxns.Count, 0) : 0,
                InvoiceRevenue     = invoices.Sum(i => i.TotalAmount),
                OrderRevenue       = orders.Sum(o => o.TotalAmount),
                BillRevenue        = bills.Sum(b => b.TotalAmount),
            },
            Grouped      = grouped,
            Transactions = txnList,
        }));
    }

    // ── 2. P&L Report ────────────────────────────────────────────────────────
    [HttpGet("pl")]
    public async Task<IActionResult> GetPL([FromQuery] int year = 0, [FromQuery] int? month = null)
    {
        if (year == 0) year = DateTime.UtcNow.Year;

        var from = month.HasValue ? new DateTime(year, month.Value, 1) : new DateTime(year, 1, 1);
        var to   = month.HasValue ? from.AddMonths(1).AddTicks(-1) : new DateTime(year, 12, 31, 23, 59, 59);

        var (invRev, ordRev, billRev) = await RevenueParts(from, to);
        var totalRevenue = invRev + ordRev + billRev;

        // COGS: actual purchase order amounts (received/completed)
        var cogs = await _db.PurchaseOrders
            .Where(p => !p.IsDeleted && p.Status == PurchaseOrderStatus.Received
                     && p.CreatedAt >= from && p.CreatedAt <= to)
            .SumAsync(p => (decimal?)p.TotalAmount) ?? 0;

        var grossProfit    = totalRevenue - cogs;
        var grossMarginPct = totalRevenue > 0 ? Math.Round(grossProfit / totalRevenue * 100, 1) : 0;

        // expenses
        var expQuery = _db.ExpenseEntries.Where(e => e.Date >= from && e.Date <= to);
        var expenses = await expQuery
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.Total)
            .ToListAsync();
        var totalExpenses = expenses.Sum(e => e.Total);

        var netProfit    = grossProfit - totalExpenses;
        var netMarginPct = totalRevenue > 0 ? Math.Round(netProfit / totalRevenue * 100, 1) : 0;

        // monthly breakdown (always 12 months for the year)
        var monthlyRevData = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt.Year == year)
            .GroupBy(i => i.CreatedAt.Month).Select(g => new { Month = g.Key, Rev = g.Sum(x => x.TotalAmount) }).ToListAsync();
        var monthlyOrdData = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.CreatedAt.Year == year)
            .GroupBy(o => o.CreatedAt.Month).Select(g => new { Month = g.Key, Rev = g.Sum(x => x.GrandTotal) }).ToListAsync();
        var monthlyBillData = await _db.SalesOrderBills
            .Where(b => !b.IsDeleted && b.CreatedAt.Year == year)
            .GroupBy(b => b.CreatedAt.Month).Select(g => new { Month = g.Key, Rev = g.Sum(x => x.TotalAmount) }).ToListAsync();
        var monthlyExpData = await _db.ExpenseEntries
            .Where(e => e.Date.Year == year)
            .GroupBy(e => e.Date.Month).Select(g => new { Month = g.Key, Exp = g.Sum(x => x.Amount) }).ToListAsync();

        var monthlyBreakdown = Enumerable.Range(1, 12).Select(m =>
        {
            var mRev = (monthlyRevData.FirstOrDefault(x => x.Month == m)?.Rev ?? 0)
                     + (monthlyOrdData.FirstOrDefault(x  => x.Month == m)?.Rev ?? 0)
                     + (monthlyBillData.FirstOrDefault(x => x.Month == m)?.Rev ?? 0);
            var mExp = monthlyExpData.FirstOrDefault(x => x.Month == m)?.Exp ?? 0;
            return new { Month = MonthLabel(m), Revenue = mRev, Expenses = mExp, NetProfit = mRev - mExp };
        });

        var period = month.HasValue
            ? $"{System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month.Value)} {year}"
            : year.ToString();

        return Ok(ApiResponse<object>.Ok(new
        {
            Period          = period,
            Revenue         = totalRevenue,
            InvoiceRevenue  = invRev,
            OrderRevenue    = ordRev,
            BillRevenue     = billRev,
            Cogs            = cogs,
            GrossProfit     = grossProfit,
            GrossMarginPct  = grossMarginPct,
            Expenses        = expenses,
            TotalExpenses   = totalExpenses,
            NetProfit       = netProfit,
            NetMarginPct    = netMarginPct,
            MonthlyBreakdown= monthlyBreakdown,
        }));
    }

    // ── 3. GST Report ────────────────────────────────────────────────────────
    [HttpGet("gst")]
    public async Task<IActionResult> GetGst([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var f = from ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var t = EndOfDay(to) ?? DateTime.UtcNow;

        var lineItems = await _db.InvoiceLineItems
            .Where(li => !li.IsDeleted && li.Invoice.CreatedAt >= f && li.Invoice.CreatedAt <= t
                      && li.Invoice.Status != InvoiceStatus.Cancelled)
            .Include(li => li.Invoice)
            .ToListAsync();

        var b2cItems = lineItems.Where(li => li.Invoice.Type == InvoiceType.B2C).ToList();
        var b2bItems = lineItems.Where(li => li.Invoice.Type == InvoiceType.B2B).ToList();

        decimal SumField(IEnumerable<WahSpices.Domain.Entities.InvoiceLineItem> items, Func<WahSpices.Domain.Entities.InvoiceLineItem, decimal> selector)
            => items.Sum(selector);

        var hsnWise = lineItems
            .GroupBy(li => li.HsnCode ?? "N/A")
            .Select(g => new
            {
                HsnCode       = g.Key,
                TaxableAmount = g.Sum(x => x.TaxableAmount),
                GstRate       = g.FirstOrDefault()?.GstRate ?? 0,
                Cgst          = g.Sum(x => x.CgstAmount),
                Sgst          = g.Sum(x => x.SgstAmount),
                Igst          = g.Sum(x => x.IgstAmount),
                TotalTax      = g.Sum(x => x.TaxAmount),
                LineTotal     = g.Sum(x => x.LineTotal),
            })
            .OrderByDescending(x => x.TaxableAmount)
            .ToList();

        // invoice-level rows for preview
        var invoiceRows = await _db.Invoices
            .Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt >= f && i.CreatedAt <= t)
            .Select(i => new
            {
                Date          = i.CreatedAt,
                Ref           = i.InvoiceNumber,
                Customer      = i.CustomerName,
                Type          = i.Type.ToString(),
                Taxable       = i.Subtotal - i.DiscountAmount,
                Cgst          = i.CgstAmount,
                Sgst          = i.SgstAmount,
                Igst          = i.IgstAmount,
                TotalTax      = i.CgstAmount + i.SgstAmount + i.IgstAmount,
                GrandTotal    = i.TotalAmount,
            })
            .OrderByDescending(i => i.Date)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Period  = new { From = f, To = t },
            Summary = new
            {
                TotalRevenue  = lineItems.Sum(x => x.LineTotal),
                TaxableAmount = lineItems.Sum(x => x.TaxableAmount),
                TotalCgst     = lineItems.Sum(x => x.CgstAmount),
                TotalSgst     = lineItems.Sum(x => x.SgstAmount),
                TotalIgst     = lineItems.Sum(x => x.IgstAmount),
                TotalTax      = lineItems.Sum(x => x.TaxAmount),
                B2cRevenue    = b2cItems.Sum(x => x.LineTotal),
                B2bRevenue    = b2bItems.Sum(x => x.LineTotal),
            },
            HsnWise    = hsnWise,
            Invoices   = invoiceRows,
        }));
    }

    // ── 4. Stock Report ──────────────────────────────────────────────────────
    [HttpGet("stock")]
    public async Task<IActionResult> GetStock([FromQuery] string type = "current")
    {
        if (type == "movement")
        {
            var ledger = await _db.InventoryLedgers
                .Include(l => l.InventoryItem).ThenInclude(i => i.Product)
                .OrderByDescending(l => l.CreatedAt)
                .Take(500)
                .Select(l => new
                {
                    Date        = l.CreatedAt,
                    Product     = l.InventoryItem.Product.Name,
                    Sku         = l.InventoryItem.Sku,
                    Type        = l.Type.ToString(),
                    Quantity    = l.Quantity,
                    Balance     = l.BalanceAfter,
                    Reason      = l.Reason,
                    Reference   = l.ReferenceId,
                    PerformedBy = l.PerformedBy,
                })
                .ToListAsync();

            return Ok(ApiResponse<object>.Ok(new
            {
                Type = "movement",
                Data = ledger,
                Total = ledger.Count,
            }));
        }

        if (type == "valuation")
        {
            var items = await _db.InventoryItems
                .Where(i => !i.IsDeleted)
                .Include(i => i.Product)
                .Select(i => new
                {
                    Product     = i.Product.Name,
                    Sku         = i.Sku,
                    Stock       = i.CurrentStock,
                    Unit        = i.Unit,
                    CostPrice   = i.CostPrice,
                    MrpPrice    = i.Product.Mrp,
                    SellingPrice= i.Product.Price,
                    TotalCost   = i.CurrentStock * i.CostPrice,
                    TotalMrp    = i.CurrentStock * i.Product.Mrp,
                    Location    = i.Location,
                    Batch       = i.BatchNumber,
                    Expiry      = i.ExpiryDate,
                })
                .OrderByDescending(i => i.TotalCost)
                .ToListAsync();

            return Ok(ApiResponse<object>.Ok(new
            {
                Type         = "valuation",
                Data         = items,
                Summary = new
                {
                    TotalItems     = items.Count,
                    TotalStockQty  = items.Sum(i => i.Stock),
                    TotalCostValue = items.Sum(i => i.TotalCost),
                    TotalMrpValue  = items.Sum(i => i.TotalMrp),
                },
            }));
        }

        // default: current stock
        {
            var items = await _db.InventoryItems
                .Where(i => !i.IsDeleted)
                .Include(i => i.Product)
                .Select(i => new
                {
                    Product      = i.Product.Name,
                    Sku          = i.Sku,
                    Stock        = i.CurrentStock,
                    Unit         = i.Unit,
                    ReorderPoint = i.ReorderPoint,
                    Status       = i.CurrentStock == 0 ? "Out of Stock" : i.CurrentStock <= i.ReorderPoint ? "Low Stock" : "In Stock",
                    Location     = i.Location,
                    Batch        = i.BatchNumber,
                    Expiry       = i.ExpiryDate,
                    CostPrice    = i.CostPrice,
                })
                .OrderBy(i => i.Status)
                .ThenBy(i => i.Product)
                .ToListAsync();

            return Ok(ApiResponse<object>.Ok(new
            {
                Type    = "current",
                Data    = items,
                Summary = new
                {
                    TotalItems  = items.Count,
                    LowStock    = items.Count(i => i.Status == "Low Stock"),
                    OutOfStock  = items.Count(i => i.Status == "Out of Stock"),
                    InStock     = items.Count(i => i.Status == "In Stock"),
                },
            }));
        }
    }

    // ── 5. Margin Report ─────────────────────────────────────────────────────
    [HttpGet("margin")]
    public async Task<IActionResult> GetMargin([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var f = from ?? DateTime.UtcNow.AddDays(-30);
        var t = EndOfDay(to) ?? DateTime.UtcNow;

        // products with cost from inventory
        var products = await _db.Products
            .Where(p => !p.IsDeleted)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug,
                SellingPrice = p.Price,
                MrpPrice     = p.Mrp,
                CostPrice    = p.InventoryItems.Where(i => !i.IsDeleted).OrderBy(i => i.VariantId).Select(i => (decimal?)i.CostPrice).FirstOrDefault() ?? 0m,
                p.HsnCode,
            })
            .ToListAsync();

        // units sold from invoice line items
        var invoiceSales = await _db.InvoiceLineItems
            .Where(li => !li.IsDeleted && li.ProductId.HasValue
                      && li.Invoice.CreatedAt >= f && li.Invoice.CreatedAt <= t
                      && li.Invoice.Status != InvoiceStatus.Cancelled)
            .GroupBy(li => li.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .ToListAsync();

        // units sold from orders
        var orderSales = await _db.OrderItems
            .Where(oi => oi.Order.PaymentStatus == PaymentStatus.Paid
                      && oi.Order.CreatedAt >= f && oi.Order.CreatedAt <= t)
            .GroupBy(oi => oi.ProductId)
            .Select(g => new { ProductId = g.Key, Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .ToListAsync();

        var invoiceSalesMap = invoiceSales.ToDictionary(x => x.ProductId);
        var orderSalesMap   = orderSales.ToDictionary(x => x.ProductId);

        var rows = products.Select(p =>
        {
            invoiceSalesMap.TryGetValue(p.Id, out var iSales);
            orderSalesMap.TryGetValue(p.Id, out var oSales);
            var units   = (iSales?.Units ?? 0) + (oSales?.Units ?? 0);
            var revenue = (iSales?.Revenue ?? 0) + (oSales?.Revenue ?? 0);
            var cost    = p.CostPrice;
            var margin  = p.SellingPrice > 0 ? p.SellingPrice - cost : 0;
            var marginPct = p.SellingPrice > 0 ? Math.Round(margin / p.SellingPrice * 100, 1) : 0;
            var profit  = units * margin;
            return new
            {
                p.Name,
                SellingPrice = p.SellingPrice,
                MrpPrice     = p.MrpPrice,
                CostPrice    = cost,
                MarginAmt    = margin,
                MarginPct    = marginPct,
                UnitsSold    = units,
                Revenue      = revenue,
                Profit       = profit,
            };
        })
        .OrderByDescending(x => x.Revenue)
        .ToList();

        var totalRevenue = rows.Sum(x => x.Revenue);
        var totalProfit  = rows.Sum(x => x.Profit);
        var avgMarginPct = rows.Count > 0 ? Math.Round(rows.Where(r => r.SellingPrice > 0).Average(r => r.MarginPct), 1) : 0;

        return Ok(ApiResponse<object>.Ok(new
        {
            Period  = new { From = f, To = t },
            Summary = new
            {
                TotalProducts = rows.Count,
                TotalRevenue  = totalRevenue,
                TotalProfit   = totalProfit,
                AvgMarginPct  = avgMarginPct,
            },
            Products = rows,
        }));
    }

    // ── 6. YoY Report ────────────────────────────────────────────────────────
    [HttpGet("yoy")]
    public async Task<IActionResult> GetYoY([FromQuery] int baseYear = 0, [FromQuery] int compareYears = 2)
    {
        if (baseYear == 0) baseYear = DateTime.UtcNow.Year;
        var years = Enumerable.Range(baseYear - compareYears + 1, compareYears).ToList();

        var result = new List<object>();
        foreach (var yr in years)
        {
            var yFrom = new DateTime(yr, 1, 1);
            var yTo   = new DateTime(yr, 12, 31, 23, 59, 59);

            var (iRev, oRev, bRev) = await RevenueParts(yFrom, yTo);
            var revenue  = iRev + oRev + bRev;
            var txnCount = await _db.Invoices.CountAsync(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled && i.CreatedAt.Year == yr)
                         + await _db.Orders.CountAsync(o => o.CreatedAt.Year == yr)
                         + await _db.SalesOrderBills.CountAsync(b => !b.IsDeleted && b.CreatedAt.Year == yr);
            var customers = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt.Year == yr);
            var expenses  = await _db.ExpenseEntries.Where(e => e.Date.Year == yr).SumAsync(e => (decimal?)e.Amount) ?? 0;
            var cogs      = await _db.PurchaseOrders.Where(p => !p.IsDeleted && p.CreatedAt.Year == yr
                              && p.Status == PurchaseOrderStatus.Received)
                              .SumAsync(p => (decimal?)p.TotalAmount) ?? 0;
            var netProfit = revenue - cogs - expenses;

            // monthly breakdown for chart
            var months = Enumerable.Range(1, 12).Select(m =>
            {
                return new { Month = MonthLabel(m), Year = yr };
            });

            result.Add(new
            {
                Year        = yr,
                Revenue     = revenue,
                Transactions= txnCount,
                NewCustomers= customers,
                Expenses    = expenses,
                Cogs        = cogs,
                NetProfit   = netProfit,
                NetMarginPct= revenue > 0 ? Math.Round(netProfit / revenue * 100, 1) : 0m,
            });
        }

        // growth vs previous year
        var sorted = result.Cast<dynamic>().OrderBy(x => (int)x.Year).ToList();
        var growthRows = new List<object>();
        if (sorted.Count >= 2)
        {
            var prev = sorted[^2];
            var curr = sorted[^1];
            decimal SafePct(decimal c, decimal p) => p == 0 ? 0 : Math.Round((c - p) / p * 100, 1);
            growthRows.Add(new { Metric = "Revenue",      BaseYear = (decimal)prev.Revenue,     CurrentYear = (decimal)curr.Revenue,     GrowthPct = SafePct((decimal)curr.Revenue, (decimal)prev.Revenue) });
            growthRows.Add(new { Metric = "Transactions", BaseYear = (decimal)prev.Transactions, CurrentYear = (decimal)curr.Transactions, GrowthPct = SafePct((decimal)curr.Transactions, (decimal)prev.Transactions) });
            growthRows.Add(new { Metric = "New Customers",BaseYear = (decimal)prev.NewCustomers, CurrentYear = (decimal)curr.NewCustomers, GrowthPct = SafePct((decimal)curr.NewCustomers, (decimal)prev.NewCustomers) });
            growthRows.Add(new { Metric = "Expenses",     BaseYear = (decimal)prev.Expenses,     CurrentYear = (decimal)curr.Expenses,     GrowthPct = SafePct((decimal)curr.Expenses, (decimal)prev.Expenses) });
            growthRows.Add(new { Metric = "Net Profit",   BaseYear = (decimal)prev.NetProfit,    CurrentYear = (decimal)curr.NetProfit,    GrowthPct = SafePct((decimal)curr.NetProfit, (decimal)prev.NetProfit) });
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            Years  = result,
            Growth = growthRows,
        }));
    }

    // ── Excel Export Endpoints ───────────────────────────────────────────────
    [HttpGet("export/sales")]
    public async Task<IActionResult> ExportSales([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string groupBy = "day")
    {
        var result = await GetSalesData(from, to, groupBy);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sales Report");
        WriteHeader(ws, new[] { "Date", "Reference", "Customer", "Source", "Amount (₹)" });

        int row = 2;
        foreach (var txn in result.Transactions)
        {
            ws.Cell(row, 1).Value = txn.Date;
            ws.Cell(row, 2).Value = txn.Ref;
            ws.Cell(row, 3).Value = txn.Customer;
            ws.Cell(row, 4).Value = txn.Source;
            ws.Cell(row, 5).Value = txn.Amount;
            row++;
        }
        // summary below
        row++;
        ws.Cell(row, 1).Value = "Total Revenue"; ws.Cell(row, 5).Value = result.Summary.TotalRevenue;
        ws.Cell(row + 1, 1).Value = "Total Transactions"; ws.Cell(row + 1, 5).Value = result.Summary.TotalTransactions;
        AutoFit(ws);
        return ExcelFile(wb, $"SalesReport_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("export/pl")]
    public async Task<IActionResult> ExportPL([FromQuery] int year = 0, [FromQuery] int? month = null)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        var result = await GetPLData(year, month);
        using var wb = new XLWorkbook();

        // Sheet 1: P&L Summary
        var ws1 = wb.Worksheets.Add("P&L Summary");
        WriteHeader(ws1, new[] { "Item", "Amount (₹)" });
        var plRows = new (string, decimal)[]
        {
            ("Total Revenue",        result.Revenue),
            ("Invoice Revenue",      result.InvoiceRevenue),
            ("Order Revenue",        result.OrderRevenue),
            ("Bill Revenue",         result.BillRevenue),
            ("Cost of Goods (COGS)", result.Cogs),
            ("Gross Profit",         result.GrossProfit),
            ("Total Expenses",       result.TotalExpenses),
            ("Net Profit",           result.NetProfit),
        };
        int row = 2;
        foreach (var (label, amt) in plRows)
        {
            ws1.Cell(row, 1).Value = label;
            ws1.Cell(row, 2).Value = amt;
            row++;
        }
        AutoFit(ws1);

        // Sheet 2: Expenses breakdown
        var ws2 = wb.Worksheets.Add("Expenses");
        WriteHeader(ws2, new[] { "Category", "Amount (₹)" });
        row = 2;
        foreach (var exp in result.Expenses)
        {
            ws2.Cell(row, 1).Value = exp.Category;
            ws2.Cell(row, 2).Value = exp.Total;
            row++;
        }
        AutoFit(ws2);

        // Sheet 3: Monthly breakdown
        var ws3 = wb.Worksheets.Add("Monthly Breakdown");
        WriteHeader(ws3, new[] { "Month", "Revenue (₹)", "Expenses (₹)", "Net Profit (₹)" });
        row = 2;
        foreach (var mb in result.MonthlyBreakdown)
        {
            ws3.Cell(row, 1).Value = mb.Month;
            ws3.Cell(row, 2).Value = mb.Revenue;
            ws3.Cell(row, 3).Value = mb.Expenses;
            ws3.Cell(row, 4).Value = mb.NetProfit;
            row++;
        }
        AutoFit(ws3);

        return ExcelFile(wb, $"PL_Report_{result.Period.Replace(" ", "_")}.xlsx");
    }

    [HttpGet("export/gst")]
    public async Task<IActionResult> ExportGst([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await GetGstData(from, to);
        using var wb = new XLWorkbook();

        // Sheet 1: Invoice List
        var ws1 = wb.Worksheets.Add("Invoices");
        WriteHeader(ws1, new[] { "Date", "Invoice No", "Customer", "Type", "Taxable Amt", "CGST", "SGST", "IGST", "Total Tax", "Grand Total" });
        int row = 2;
        foreach (var inv in result.Invoices)
        {
            ws1.Cell(row, 1).Value = inv.Date.ToString("dd MMM yyyy");
            ws1.Cell(row, 2).Value = inv.Ref;
            ws1.Cell(row, 3).Value = inv.Customer;
            ws1.Cell(row, 4).Value = inv.Type;
            ws1.Cell(row, 5).Value = inv.Taxable;
            ws1.Cell(row, 6).Value = inv.Cgst;
            ws1.Cell(row, 7).Value = inv.Sgst;
            ws1.Cell(row, 8).Value = inv.Igst;
            ws1.Cell(row, 9).Value = inv.TotalTax;
            ws1.Cell(row, 10).Value = inv.GrandTotal;
            row++;
        }
        AutoFit(ws1);

        // Sheet 2: HSN-wise summary
        var ws2 = wb.Worksheets.Add("HSN Summary");
        WriteHeader(ws2, new[] { "HSN Code", "Taxable Amount", "GST Rate %", "CGST", "SGST", "IGST", "Total Tax", "Line Total" });
        row = 2;
        foreach (var hsn in result.HsnWise)
        {
            ws2.Cell(row, 1).Value = hsn.HsnCode;
            ws2.Cell(row, 2).Value = hsn.TaxableAmount;
            ws2.Cell(row, 3).Value = hsn.GstRate;
            ws2.Cell(row, 4).Value = hsn.Cgst;
            ws2.Cell(row, 5).Value = hsn.Sgst;
            ws2.Cell(row, 6).Value = hsn.Igst;
            ws2.Cell(row, 7).Value = hsn.TotalTax;
            ws2.Cell(row, 8).Value = hsn.LineTotal;
            row++;
        }
        AutoFit(ws2);

        return ExcelFile(wb, $"GST_Report_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("export/stock")]
    public async Task<IActionResult> ExportStock([FromQuery] string type = "current")
    {
        var result = await GetStockData(type);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Stock Report");

        if (type == "movement")
        {
            WriteHeader(ws, new[] { "Date", "Product", "SKU", "Type", "Quantity", "Balance After", "Reason", "Reference", "Performed By" });
            int row = 2;
            foreach (var item in result.Movement ?? new())
            {
                ws.Cell(row, 1).Value = item.Date.ToString("dd MMM yyyy HH:mm");
                ws.Cell(row, 2).Value = item.Product;
                ws.Cell(row, 3).Value = item.Sku;
                ws.Cell(row, 4).Value = item.Type;
                ws.Cell(row, 5).Value = item.Quantity;
                ws.Cell(row, 6).Value = item.Balance;
                ws.Cell(row, 7).Value = item.Reason ?? "";
                ws.Cell(row, 8).Value = item.Reference ?? "";
                ws.Cell(row, 9).Value = item.PerformedBy;
                row++;
            }
        }
        else if (type == "valuation")
        {
            WriteHeader(ws, new[] { "Product", "SKU", "Stock", "Unit", "Cost Price", "MRP", "Selling Price", "Total Cost Value", "Total MRP Value", "Location" });
            int row = 2;
            foreach (var item in result.Valuation ?? new())
            {
                ws.Cell(row, 1).Value = item.Product;
                ws.Cell(row, 2).Value = item.Sku;
                ws.Cell(row, 3).Value = item.Stock;
                ws.Cell(row, 4).Value = item.Unit;
                ws.Cell(row, 5).Value = item.CostPrice;
                ws.Cell(row, 6).Value = item.MrpPrice;
                ws.Cell(row, 7).Value = item.SellingPrice;
                ws.Cell(row, 8).Value = item.TotalCost;
                ws.Cell(row, 9).Value = item.TotalMrp;
                ws.Cell(row, 10).Value = item.Location;
                row++;
            }
        }
        else
        {
            WriteHeader(ws, new[] { "Product", "SKU", "Stock", "Unit", "Reorder Point", "Status", "Location", "Batch", "Expiry", "Cost Price" });
            int row = 2;
            foreach (var item in result.Current ?? new())
            {
                ws.Cell(row, 1).Value = item.Product;
                ws.Cell(row, 2).Value = item.Sku;
                ws.Cell(row, 3).Value = item.Stock;
                ws.Cell(row, 4).Value = item.Unit;
                ws.Cell(row, 5).Value = item.ReorderPoint;
                ws.Cell(row, 6).Value = item.Status;
                ws.Cell(row, 7).Value = item.Location;
                ws.Cell(row, 8).Value = item.Batch ?? "";
                ws.Cell(row, 9).Value = item.Expiry?.ToString("dd MMM yyyy") ?? "";
                ws.Cell(row, 10).Value = item.CostPrice;
                row++;
            }
        }
        AutoFit(ws);
        return ExcelFile(wb, $"Stock_{type}_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("export/margin")]
    public async Task<IActionResult> ExportMargin([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await GetMarginData(from, to);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Margin Report");
        WriteHeader(ws, new[] { "Product", "Selling Price", "Cost Price", "Margin (₹)", "Margin %", "Units Sold", "Revenue (₹)", "Profit (₹)" });
        int row = 2;
        foreach (var p in result.Products)
        {
            ws.Cell(row, 1).Value = p.Name;
            ws.Cell(row, 2).Value = p.SellingPrice;
            ws.Cell(row, 3).Value = p.CostPrice;
            ws.Cell(row, 4).Value = p.MarginAmt;
            ws.Cell(row, 5).Value = $"{p.MarginPct}%";
            ws.Cell(row, 6).Value = p.UnitsSold;
            ws.Cell(row, 7).Value = p.Revenue;
            ws.Cell(row, 8).Value = p.Profit;
            row++;
        }
        AutoFit(ws);
        return ExcelFile(wb, $"MarginReport_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("export/yoy")]
    public async Task<IActionResult> ExportYoY([FromQuery] int baseYear = 0, [FromQuery] int compareYears = 2)
    {
        var result = await GetYoYData(baseYear, compareYears);
        using var wb = new XLWorkbook();

        var ws1 = wb.Worksheets.Add("YoY Comparison");
        WriteHeader(ws1, new[] { "Year", "Revenue (₹)", "Transactions", "New Customers", "Expenses (₹)", "COGS (₹)", "Net Profit (₹)", "Net Margin %" });
        int row = 2;
        foreach (var y in result.Years)
        {
            ws1.Cell(row, 1).Value = y.Year;
            ws1.Cell(row, 2).Value = y.Revenue;
            ws1.Cell(row, 3).Value = y.Transactions;
            ws1.Cell(row, 4).Value = y.NewCustomers;
            ws1.Cell(row, 5).Value = y.Expenses;
            ws1.Cell(row, 6).Value = y.Cogs;
            ws1.Cell(row, 7).Value = y.NetProfit;
            ws1.Cell(row, 8).Value = y.NetMarginPct;
            row++;
        }
        AutoFit(ws1);

        var ws2 = wb.Worksheets.Add("Growth Metrics");
        WriteHeader(ws2, new[] { "Metric", "Previous Year", "Current Year", "Growth %" });
        row = 2;
        foreach (var g in result.Growth)
        {
            ws2.Cell(row, 1).Value = g.Metric;
            ws2.Cell(row, 2).Value = g.BaseYear;
            ws2.Cell(row, 3).Value = g.CurrentYear;
            ws2.Cell(row, 4).Value = $"{g.GrowthPct}%";
            row++;
        }
        AutoFit(ws2);

        return ExcelFile(wb, $"YoY_Report_{DateTime.UtcNow:yyyy}.xlsx");
    }

    // ── private data-fetch helpers (reused by export endpoints) ─────────────
    private async Task<SalesData> GetSalesData(DateTime? from, DateTime? to, string groupBy)
    {
        // call the logic inline – simpler than refactoring
        var res = await GetSales(from, to, groupBy) as OkObjectResult;
        var api = (ApiResponse<object>)res!.Value!;
        return System.Text.Json.JsonSerializer.Deserialize<SalesData>(
            System.Text.Json.JsonSerializer.Serialize(api.Data))!;
    }
    private async Task<PLData> GetPLData(int year, int? month)
    {
        var res = await GetPL(year, month) as OkObjectResult;
        var api = (ApiResponse<object>)res!.Value!;
        return System.Text.Json.JsonSerializer.Deserialize<PLData>(
            System.Text.Json.JsonSerializer.Serialize(api.Data))!;
    }
    private async Task<GstData> GetGstData(DateTime? from, DateTime? to)
    {
        var res = await GetGst(from, to) as OkObjectResult;
        var api = (ApiResponse<object>)res!.Value!;
        return System.Text.Json.JsonSerializer.Deserialize<GstData>(
            System.Text.Json.JsonSerializer.Serialize(api.Data))!;
    }
    private async Task<StockData> GetStockData(string type)
    {
        // GetStock returns { Type, Data: [...] } — map Data onto the list matching the report type
        var res = await GetStock(type) as OkObjectResult;
        var api = (ApiResponse<object>)res!.Value!;
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(api.Data));
        var data = doc.RootElement.GetProperty("Data").GetRawText();
        return type switch
        {
            "movement"  => new StockData(type, Movement:  System.Text.Json.JsonSerializer.Deserialize<List<StockMovementItem>>(data) ?? new()),
            "valuation" => new StockData(type, Valuation: System.Text.Json.JsonSerializer.Deserialize<List<StockValItem>>(data) ?? new()),
            _           => new StockData(type, Current:   System.Text.Json.JsonSerializer.Deserialize<List<StockCurrentItem>>(data) ?? new()),
        };
    }
    private async Task<MarginData> GetMarginData(DateTime? from, DateTime? to)
    {
        var res = await GetMargin(from, to) as OkObjectResult;
        var api = (ApiResponse<object>)res!.Value!;
        return System.Text.Json.JsonSerializer.Deserialize<MarginData>(
            System.Text.Json.JsonSerializer.Serialize(api.Data))!;
    }
    private async Task<YoYData> GetYoYData(int baseYear, int compareYears)
    {
        var res = await GetYoY(baseYear, compareYears) as OkObjectResult;
        var api = (ApiResponse<object>)res!.Value!;
        return System.Text.Json.JsonSerializer.Deserialize<YoYData>(
            System.Text.Json.JsonSerializer.Serialize(api.Data))!;
    }

    // ── Excel helpers ────────────────────────────────────────────────────────
    private static void WriteHeader(IXLWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a5c2e");
            cell.Style.Font.FontColor = XLColor.White;
        }
    }

    private static void AutoFit(IXLWorksheet ws)
    {
        ws.Columns().AdjustToContents();
    }

    private FileContentResult ExcelFile(XLWorkbook wb, string filename)
    {
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    // ── DTOs for deserialization ─────────────────────────────────────────────
    private record SalesTxn(string Date, string Ref, string Customer, string Source, decimal Amount);
    private record SalesSummary(decimal TotalRevenue, int TotalTransactions, decimal AvgTransactionValue, decimal InvoiceRevenue, decimal OrderRevenue, decimal BillRevenue);
    private record SalesData(SalesSummary Summary, List<SalesTxn> Transactions);

    private record ExpenseRow(string Category, decimal Total);
    private record MonthlyRow(string Month, decimal Revenue, decimal Expenses, decimal NetProfit);
    private record PLData(string Period, decimal Revenue, decimal InvoiceRevenue, decimal OrderRevenue, decimal BillRevenue,
        decimal Cogs, decimal GrossProfit, decimal GrossMarginPct, List<ExpenseRow> Expenses, decimal TotalExpenses,
        decimal NetProfit, decimal NetMarginPct, List<MonthlyRow> MonthlyBreakdown);

    private record GstInvoiceRow(DateTime Date, string Ref, string Customer, string Type, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal TotalTax, decimal GrandTotal);
    private record HsnRow(string HsnCode, decimal TaxableAmount, decimal GstRate, decimal Cgst, decimal Sgst, decimal Igst, decimal TotalTax, decimal LineTotal);
    private record GstData(List<GstInvoiceRow> Invoices, List<HsnRow> HsnWise);

    private record StockCurrentItem(string Product, string Sku, int Stock, string Unit, int ReorderPoint, string Status, string Location, string? Batch, DateTime? Expiry, decimal CostPrice);
    private record StockMovementItem(DateTime Date, string Product, string Sku, string Type, int Quantity, int Balance, string? Reason, string? Reference, string PerformedBy);
    private record StockValItem(string Product, string Sku, int Stock, string Unit, decimal CostPrice, decimal MrpPrice, decimal SellingPrice, decimal TotalCost, decimal TotalMrp, string Location);
    private record StockData(string Type, List<StockCurrentItem>? Current = null, List<StockMovementItem>? Movement = null, List<StockValItem>? Valuation = null);

    private record MarginRow(string Name, decimal SellingPrice, decimal MrpPrice, decimal CostPrice, decimal MarginAmt, decimal MarginPct, int UnitsSold, decimal Revenue, decimal Profit);
    private record MarginSummary(int TotalProducts, decimal TotalRevenue, decimal TotalProfit, decimal AvgMarginPct);
    private record MarginData(MarginSummary Summary, List<MarginRow> Products);

    private record YoYYearRow(int Year, decimal Revenue, int Transactions, int NewCustomers, decimal Expenses, decimal Cogs, decimal NetProfit, decimal NetMarginPct);
    private record YoYGrowthRow(string Metric, decimal BaseYear, decimal CurrentYear, decimal GrowthPct);
    private record YoYData(List<YoYYearRow> Years, List<YoYGrowthRow> Growth);
}
