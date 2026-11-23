using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WahSpices.API.Controllers.Admin;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;
using WahSpices.Infrastructure.Services;

/// <summary>Server-side paging for the field-sales lists. No page → whole list (pickers); page → one page.</summary>
internal static class SalesPaging
{
    public static (int Page, int Limit) Norm(int? page, int? limit) => (Math.Max(1, page ?? 1), Math.Clamp(limit ?? 20, 1, 200));
}

// ── Retail Clients ────────────────────────────────────────────────────────────
[Route("api/admin/sales/clients")]
[ApiController]
[Authorize(Policy = "AdminOnly")]
[RequireModule("FieldSales")]
public class AdminRetailClientsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminRetailClientsController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] string? type, [FromQuery] bool? active,
        [FromQuery] int? page = null, [FromQuery] int? limit = null)
    {
        var q = _db.RetailClients.Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x => x.Name.Contains(search) || (x.Phone != null && x.Phone.Contains(search))
                || (x.ContactPerson != null && x.ContactPerson.Contains(search)) || (x.GstNumber != null && x.GstNumber.Contains(search))
                || (x.City != null && x.City.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(type))
            q = q.Where(x => x.ClientType == type);
        if (active.HasValue)
            q = q.Where(x => x.IsActive == active.Value);
        var total = await q.CountAsync();
        var ordered = q.OrderBy(x => x.Name).ThenBy(x => x.Id).AsQueryable();
        var (pg, lim) = SalesPaging.Norm(page, limit);
        if (page.HasValue) ordered = ordered.Skip((pg - 1) * lim).Take(lim);
        var list = await ordered.Select(x => new
        {
            x.Id, x.Name, x.ContactPerson, x.Phone, x.Email, x.Address, x.City, x.State,
            x.GstNumber, x.ClientType, x.CreditLimit, x.CurrentOutstanding, x.IsActive, x.Notes
        }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items = list, total, page = page.HasValue ? pg : 1, limit = page.HasValue ? lim : total }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var c = await _db.RetailClients.Where(x => x.Id == id && !x.IsDeleted).FirstOrDefaultAsync();
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Client not found"));
        return Ok(ApiResponse<object>.Ok(c));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRetailClientRequest req)
    {
        var err = await ValidateClient(req);
        if (err != null) return err;
        var client = new RetailClient
        {
            Name = req.Name!.Trim(), ContactPerson = req.ContactPerson, Phone = req.Phone,
            Email = req.Email, Address = req.Address, City = req.City, State = req.State,
            GstNumber = req.GstNumber, ClientType = req.ClientType ?? "Retailer",
            CreditLimit = req.CreditLimit ?? 0, IsActive = true,
            Notes = req.Notes, CreatedBy = AdminName
        };
        _db.RetailClients.Add(client);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapClient(client), "Client created"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateRetailClientRequest req)
    {
        var c = await _db.RetailClients.FindAsync(id);
        if (c == null || c.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Client not found"));
        var err = await ValidateClient(req);
        if (err != null) return err;
        c.Name = req.Name!.Trim(); c.ContactPerson = req.ContactPerson; c.Phone = req.Phone;
        c.Email = req.Email; c.Address = req.Address; c.City = req.City; c.State = req.State;
        c.GstNumber = req.GstNumber; c.ClientType = req.ClientType ?? c.ClientType;
        c.CreditLimit = req.CreditLimit ?? c.CreditLimit;
        c.Notes = req.Notes; c.UpdatedBy = AdminName; c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapClient(c), "Client updated"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.RetailClients.FindAsync(id);
        if (c == null || c.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Client not found"));
        var openOrders = await _db.SalesOrders.CountAsync(o => o.ClientId == id && !o.IsDeleted && !o.IsBilled
            && o.Status != nameof(SalesOrderStatus.Cancelled));
        if (openOrders > 0)
            return BadRequest(ApiResponse<object>.Fail("HAS_OPEN_ORDERS", $"{c.Name} has {openOrders} open order(s). Cancel or bill them before deleting the client."));
        if (c.CurrentOutstanding > 0)
            return BadRequest(ApiResponse<object>.Fail("HAS_OUTSTANDING", $"{c.Name} still owes ₹{c.CurrentOutstanding:N2}. Settle the bills before deleting the client."));
        c.IsDeleted = true; c.UpdatedBy = AdminName; c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { deleted = true }, "Client deleted"));
    }

    private async Task<IActionResult?> ValidateClient(CreateRetailClientRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Business name is required.", "name"));
        if (req.CreditLimit is < 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Credit limit cannot be negative.", "creditLimit"));
        if (!string.IsNullOrWhiteSpace(req.ClientType))
        {
            var types = await _db.MasterDataValues.Where(m => m.Type == "SalesClientType").Select(m => m.Value).ToListAsync();
            if (types.Count > 0 && !types.Contains(req.ClientType))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Unknown client type '{req.ClientType}'.", "clientType"));
        }
        return null;
    }

    private static object MapClient(RetailClient x) => new
    {
        x.Id, x.Name, x.ContactPerson, x.Phone, x.Email, x.Address, x.City, x.State,
        x.GstNumber, x.ClientType, x.CreditLimit, x.CurrentOutstanding, x.IsActive, x.Notes
    };
}

// ── Sales Orders ──────────────────────────────────────────────────────────────
[Route("api/admin/sales/orders")]
[ApiController]
[Authorize(Policy = "AdminOnly")]
[RequireModule("FieldSales")]
public class AdminSalesOrdersController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminSalesOrdersController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? clientId, [FromQuery] string? status,
        [FromQuery] string? salesperson, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? search = null, [FromQuery] int? page = null, [FromQuery] int? limit = null)
    {
        var q = _db.SalesOrders.Where(x => !x.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x => x.OrderNumber.Contains(search) || x.Client.Name.Contains(search) || x.SalespersonName.Contains(search));
        }
        if (clientId.HasValue) q = q.Where(x => x.ClientId == clientId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(salesperson)) q = q.Where(x => x.SalespersonName.Contains(salesperson));
        if (from.HasValue) q = q.Where(x => x.OrderDate >= from.Value);
        if (to.HasValue) q = q.Where(x => x.OrderDate <= to.Value);
        var total = await q.CountAsync();
        var ordered = q.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id).AsQueryable();
        var (pg, lim) = SalesPaging.Norm(page, limit);
        if (page.HasValue) ordered = ordered.Skip((pg - 1) * lim).Take(lim);
        var list = await ordered.Select(x => new
        {
            x.Id, x.OrderNumber, x.ClientId, ClientName = x.Client.Name, x.SalespersonName,
            x.OrderDate, x.Status, x.SubTotal, x.GstAmount, x.TotalAmount, x.IsBilled, x.BillId,
            ItemCount = x.Items.Count(i => !i.IsDeleted)
        }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items = list, total, page = page.HasValue ? pg : 1, limit = page.HasValue ? lim : total }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await _db.SalesOrders
            .Include(x => x.Client)
            .Include(x => x.Items.Where(i => !i.IsDeleted))
            .Where(x => x.Id == id && !x.IsDeleted).FirstOrDefaultAsync();
        if (order == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found"));
        return Ok(ApiResponse<object>.Ok(new
        {
            order.Id, order.OrderNumber, order.ClientId, ClientName = order.Client.Name,
            order.SalespersonName, order.OrderDate, order.Status, order.SubTotal,
            order.GstAmount, order.TotalAmount, order.Notes, order.IsBilled, order.BillId,
            Items = order.Items.Select(i => new
            {
                i.Id, i.ProductName, i.HsnCode, i.Unit, i.Quantity, i.UnitPrice, i.GstPercent, i.TotalAmount
            })
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSalesOrderRequest req)
    {
        if (req.ClientId <= 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Select a client.", "clientId"));
        if (string.IsNullOrWhiteSpace(req.SalespersonName)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Salesperson name is required.", "salespersonName"));
        if (req.Items == null || req.Items.Count == 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Add at least one item.", "items"));
        if (req.Items.Any(i => string.IsNullOrWhiteSpace(i.ProductName))) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Every item needs a product name.", "items"));
        if (req.Items.Any(i => i.Quantity <= 0)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Item quantity must be greater than zero.", "items"));
        if (req.Items.Any(i => i.UnitPrice < 0 || i.GstPercent < 0 || i.GstPercent > 100)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Item rate and GST % must be valid.", "items"));
        var client = await _db.RetailClients.FirstOrDefaultAsync(c => c.Id == req.ClientId && !c.IsDeleted);
        if (client == null) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Client not found.", "clientId"));
        if (!client.IsActive) return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"{client.Name} is inactive.", "clientId"));
        var orderNum = $"SO-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";
        decimal subTotal = req.Items!.Sum(i => i.Quantity * i.UnitPrice);
        decimal gstAmt = req.Items!.Sum(i => i.Quantity * i.UnitPrice * i.GstPercent / 100m);
        var order = new SalesOrder
        {
            OrderNumber = orderNum, ClientId = req.ClientId,
            SalespersonName = req.SalespersonName!.Trim(), OrderDate = req.OrderDate ?? DateTime.UtcNow,
            Status = nameof(SalesOrderStatus.Draft), SubTotal = subTotal, GstAmount = gstAmt,
            TotalAmount = subTotal + gstAmt, Notes = req.Notes, CreatedBy = AdminName
        };
        _db.SalesOrders.Add(order);
        await CrmLastOrder.TouchAsync(_db, client.Name, client.Phone, client.GstNumber, client.Email, order.OrderDate);
        await _db.SaveChangesAsync();
        foreach (var item in req.Items!)
        {
            var lineTotal = item.Quantity * item.UnitPrice * (1 + item.GstPercent / 100m);
            _db.SalesOrderItems.Add(new SalesOrderItem
            {
                SalesOrderId = order.Id, ProductName = item.ProductName!.Trim(), HsnCode = item.HsnCode,
                Unit = item.Unit ?? "kg", Quantity = item.Quantity, UnitPrice = item.UnitPrice,
                GstPercent = item.GstPercent, TotalAmount = lineTotal, CreatedBy = AdminName
            });
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            order.Id, order.OrderNumber, order.ClientId, ClientName = client.Name, order.SalespersonName,
            order.OrderDate, order.Status, order.SubTotal, order.GstAmount, order.TotalAmount, order.IsBilled, order.BillId,
            ItemCount = req.Items!.Count
        }, "Order created"));
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateSalesOrderStatusRequest req)
    {
        var order = await _db.SalesOrders.FindAsync(id);
        if (order == null || order.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found"));
        if (!Enum.TryParse<SalesOrderStatus>(req.Status, false, out var newStatus) || !Enum.IsDefined(newStatus))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Unknown status '{req.Status}'.", "status"));
        if (order.IsBilled || order.Status == nameof(SalesOrderStatus.Billed))
            return BadRequest(ApiResponse<object>.Fail("ALREADY_BILLED", "This order is already billed and can't change status."));
        if (order.Status == nameof(SalesOrderStatus.Cancelled))
            return BadRequest(ApiResponse<object>.Fail("CANCELLED", "This order is cancelled and can't change status."));
        if (newStatus == SalesOrderStatus.Billed)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Orders become Billed only by generating a bill.", "status"));
        if (newStatus == SalesOrderStatus.Cancelled)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Use Cancel order to cancel.", "status"));
        order.Status = newStatus.ToString(); order.UpdatedBy = AdminName; order.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(req.Notes)) order.Notes = req.Notes;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { order.Id, order.Status }));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _db.SalesOrders.FindAsync(id);
        if (order == null || order.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found"));
        if (order.IsBilled) return BadRequest(ApiResponse<object>.Fail("ALREADY_BILLED", "Cannot cancel a billed order"));
        if (order.Status == nameof(SalesOrderStatus.Cancelled)) return BadRequest(ApiResponse<object>.Fail("CANCELLED", "Order is already cancelled"));
        order.Status = nameof(SalesOrderStatus.Cancelled); order.UpdatedBy = AdminName; order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { cancelled = true, order.Id, order.Status }, "Order cancelled"));
    }
}

// ── Sales Bills ───────────────────────────────────────────────────────────────
[Route("api/admin/sales/bills")]
[ApiController]
[Authorize(Policy = "AdminOnly")]
[RequireModule("FieldSales")]
public class AdminSalesBillsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminSalesBillsController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? clientId, [FromQuery] string? paymentStatus, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? search = null, [FromQuery] int? page = null, [FromQuery] int? limit = null)
    {
        var q = _db.SalesOrderBills.Where(x => !x.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x => x.BillNumber.Contains(search) || x.Client.Name.Contains(search));
        }
        if (clientId.HasValue) q = q.Where(x => x.ClientId == clientId.Value);
        if (!string.IsNullOrWhiteSpace(paymentStatus)) q = q.Where(x => x.PaymentStatus == paymentStatus);
        if (from.HasValue) q = q.Where(x => x.BillDate >= from.Value);
        if (to.HasValue) q = q.Where(x => x.BillDate <= to.Value);
        var total = await q.CountAsync();
        var ordered = q.OrderByDescending(x => x.BillDate).ThenByDescending(x => x.Id).AsQueryable();
        var (pg, lim) = SalesPaging.Norm(page, limit);
        if (page.HasValue) ordered = ordered.Skip((pg - 1) * lim).Take(lim);
        var list = await ordered.Select(x => new
        {
            x.Id, x.BillNumber, x.ClientId, ClientName = x.Client.Name, x.BillDate,
            x.SubTotal, x.GstAmount, x.TotalAmount, x.PaidAmount, x.BalanceAmount, x.PaymentStatus
        }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items = list, total, page = page.HasValue ? pg : 1, limit = page.HasValue ? lim : total }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var bill = await _db.SalesOrderBills
            .Include(x => x.Client)
            .Include(x => x.Orders.Where(o => !o.IsDeleted))
                .ThenInclude(o => o.Items.Where(i => !i.IsDeleted))
            .Where(x => x.Id == id && !x.IsDeleted).FirstOrDefaultAsync();
        if (bill == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Bill not found"));
        return Ok(ApiResponse<object>.Ok(new
        {
            bill.Id, bill.BillNumber, bill.ClientId, ClientName = bill.Client.Name, bill.BillDate,
            bill.SubTotal, bill.GstAmount, bill.TotalAmount, bill.PaidAmount, bill.BalanceAmount,
            bill.PaymentStatus, bill.Notes,
            Orders = bill.Orders.Select(o => new
            {
                o.Id, o.OrderNumber, o.OrderDate, o.TotalAmount,
                Items = o.Items.Select(i => new { i.ProductName, i.Quantity, i.Unit, i.UnitPrice, i.GstPercent, i.TotalAmount })
            })
        }));
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateBill([FromBody] GenerateSalesBillRequest req)
    {
        if (req.OrderIds == null || req.OrderIds.Count == 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Select at least one delivered order.", "orderIds"));
        var orders = await _db.SalesOrders
            .Include(x => x.Items.Where(i => !i.IsDeleted))
            .Where(x => req.OrderIds!.Contains(x.Id) && !x.IsDeleted && !x.IsBilled && x.Status == nameof(SalesOrderStatus.Delivered))
            .ToListAsync();
        if (!orders.Any()) return BadRequest(ApiResponse<object>.Fail("NO_ORDERS", "No eligible delivered orders found"));
        if (orders.Count != req.OrderIds!.Distinct().Count())
            return BadRequest(ApiResponse<object>.Fail("NOT_ELIGIBLE", "Some selected orders are not delivered or are already billed."));
        var clientIds = orders.Select(x => x.ClientId).Distinct().ToList();
        if (clientIds.Count > 1) return BadRequest(ApiResponse<object>.Fail("MULTI_CLIENT", "All orders must belong to the same client"));

        decimal subTotal = orders.Sum(o => o.SubTotal);
        decimal gstAmt = orders.Sum(o => o.GstAmount);
        var billNum = $"BILL-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";
        var bill = new SalesOrderBill
        {
            BillNumber = billNum, ClientId = clientIds[0],
            BillDate = req.BillDate ?? DateTime.UtcNow,
            SubTotal = subTotal, GstAmount = gstAmt,
            TotalAmount = subTotal + gstAmt, BalanceAmount = subTotal + gstAmt,
            PaymentStatus = nameof(SalesBillStatus.Unpaid), Notes = req.Notes, CreatedBy = AdminName
        };
        _db.SalesOrderBills.Add(bill);
        await _db.SaveChangesAsync();

        foreach (var order in orders)
        {
            order.BillId = bill.Id; order.IsBilled = true; order.Status = nameof(SalesOrderStatus.Billed);
            order.UpdatedBy = AdminName; order.UpdatedAt = DateTime.UtcNow;
        }
        var client = await _db.RetailClients.FindAsync(clientIds[0]);
        if (client != null)
        {
            client.CurrentOutstanding += bill.TotalAmount; client.UpdatedAt = DateTime.UtcNow;
            await CrmLastOrder.TouchAsync(_db, client.Name, client.Phone, client.GstNumber, client.Email, bill.BillDate);
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            bill.Id, bill.BillNumber, bill.ClientId, ClientName = client?.Name, bill.BillDate,
            bill.SubTotal, bill.GstAmount, bill.TotalAmount, bill.PaidAmount, bill.BalanceAmount, bill.PaymentStatus
        }, $"Bill {bill.BillNumber} generated"));
    }

    [HttpPut("{id}/payment")]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] RecordPaymentRequest req)
    {
        var bill = await _db.SalesOrderBills.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (bill == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Bill not found"));
        if (req.Amount <= 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Payment amount must be greater than zero.", "amount"));
        if (bill.BalanceAmount <= 0) return BadRequest(ApiResponse<object>.Fail("ALREADY_PAID", "This bill is already fully paid."));
        if (req.Amount > bill.BalanceAmount)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Amount exceeds the balance of ₹{bill.BalanceAmount:N2}.", "amount"));
        var mode = string.IsNullOrWhiteSpace(req.Mode) ? "Cash" : req.Mode.Trim();
        var modes = await _db.MasterDataValues.Where(m => m.Type == "PaymentMode").Select(m => m.Value).ToListAsync();
        if (modes.Count > 0 && !modes.Contains(mode))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Unknown payment mode '{mode}'.", "mode"));
        bill.PaidAmount += req.Amount;
        bill.BalanceAmount = bill.TotalAmount - bill.PaidAmount;
        bill.PaymentStatus = (bill.BalanceAmount <= 0 ? SalesBillStatus.Paid : bill.PaidAmount > 0 ? SalesBillStatus.PartialPaid : SalesBillStatus.Unpaid).ToString();
        // Keep a payment trail on the bill (no separate payments table for field-sales bills)
        var line = $"{DateTime.UtcNow:yyyy-MM-dd}: received ₹{req.Amount:N2} via {mode}" + (string.IsNullOrWhiteSpace(req.Reference) ? "" : $" (ref {req.Reference.Trim()})") + $" by {AdminName}";
        bill.Notes = string.IsNullOrWhiteSpace(bill.Notes) ? line : $"{bill.Notes}\n{line}";
        if (bill.Client != null)
        {
            bill.Client.CurrentOutstanding = Math.Max(0, bill.Client.CurrentOutstanding - req.Amount);
            bill.Client.UpdatedAt = DateTime.UtcNow;
        }
        bill.UpdatedBy = AdminName; bill.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { bill.Id, bill.PaymentStatus, bill.PaidAmount, bill.BalanceAmount }, "Payment recorded"));
    }
}

// ── Request Records ───────────────────────────────────────────────────────────
public record CreateRetailClientRequest(
    string? Name, string? ContactPerson, string? Phone, string? Email,
    string? Address, string? City, string? State, string? GstNumber,
    string? ClientType, decimal? CreditLimit, string? Notes
);
public record CreateSalesOrderItemRequest(
    string? ProductName, string? HsnCode, string? Unit,
    decimal Quantity, decimal UnitPrice, decimal GstPercent
);
public record CreateSalesOrderRequest(
    int ClientId, string? SalespersonName, DateTime? OrderDate,
    string? Notes, List<CreateSalesOrderItemRequest>? Items
);
public record UpdateSalesOrderStatusRequest(string? Status, string? Notes);
public record GenerateSalesBillRequest(List<int>? OrderIds, DateTime? BillDate, string? Notes);
public record RecordPaymentRequest(decimal Amount, string? Mode, string? Reference);
