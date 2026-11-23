using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/purchase")]
[RequireModule("Purchase")]
public class AdminPurchaseController : AdminBaseController
{
    private readonly AppDbContext _db;
    private new string AdminName => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
    public AdminPurchaseController(AppDbContext db) => _db = db;

    // ── Vendors ──────────────────────────────────────────────────────────────
    [HttpGet("vendors")]
    public async Task<IActionResult> GetVendors([FromQuery] string? search)
    {
        var q = _db.Vendors.Where(v => v.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(v => v.Name.Contains(search) || v.Phone.Contains(search));
        var list = await q.OrderBy(v => v.Name).ToListAsync();
        return Ok(ApiResponse<object>.Ok(list.Select(MapVendor)));
    }

    [HttpPost("vendors")]
    public async Task<IActionResult> CreateVendor([FromBody] VendorRequest req)
    {
        var err = ValidateVendor(req);
        if (err != null) return BadRequest(err);
        var v = new Vendor
        {
            Name = req.Name.Trim(), Gstin = req.Gstin, City = req.City ?? string.Empty, State = req.State,
            Phone = req.Phone, Email = req.Email, Address = req.Address,
            BankAccountNo = req.BankAccountNo, BankIfsc = req.BankIfsc, BankName = req.BankName,
            IsActive = true, CreatedBy = AdminName
        };
        _db.Vendors.Add(v);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(MapVendor(v), "Vendor added."));
    }

    [HttpPut("vendors/{id:int}")]
    public async Task<IActionResult> UpdateVendor(int id, [FromBody] VendorRequest req)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found."));
        var err = ValidateVendor(req);
        if (err != null) return BadRequest(err);
        v.Name = req.Name.Trim(); v.Gstin = req.Gstin; v.City = req.City ?? string.Empty; v.State = req.State;
        v.Phone = req.Phone; v.Email = req.Email; v.Address = req.Address;
        // The form shows the account number masked; an unchanged masked value must not overwrite the real one.
        if (!SensitiveMask.IsEchoOfMask(req.BankAccountNo, v.BankAccountNo, x => SensitiveMask.Tail(x))) v.BankAccountNo = req.BankAccountNo;
        v.BankIfsc = req.BankIfsc; v.BankName = req.BankName;
        v.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapVendor(v)));
    }

    [HttpDelete("vendors/{id:int}")]
    public async Task<IActionResult> DeleteVendor(int id)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found."));
        v.IsActive = false; v.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("vendors/{id:int}/scorecard")]
    public async Task<IActionResult> GetVendorScorecard(int id)
    {
        var v = await _db.Vendors.FindAsync(id);
        if (v == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found."));

        var orders = await _db.PurchaseOrders.Where(p => p.VendorId == id).ToListAsync();
        var totalOrders = orders.Count;
        var receivedOrders = orders.Count(o => o.Status == PurchaseOrderStatus.Received);
        var totalSpend = orders.Where(o => o.Status == PurchaseOrderStatus.Received).Sum(o => o.TotalAmount);
        var payments = await _db.VendorPayments.Where(p => p.VendorId == id).ToListAsync();
        var totalPaid = payments.Sum(p => p.Amount);
        var returns = await _db.PurchaseReturns.Where(r => r.PurchaseOrder.VendorId == id).CountAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            vendor       = MapVendor(v),
            totalOrders,
            receivedOrders,
            fulfillmentRate = totalOrders > 0 ? Math.Round((double)receivedOrders / totalOrders * 100, 1) : 0,
            totalSpend,
            totalPaid,
            outstanding  = totalSpend - totalPaid,
            totalReturns = returns,
        }));
    }

    // ── Purchase Orders ──────────────────────────────────────────────────────
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.PurchaseOrders.Include(p => p.Vendor).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PurchaseOrderStatus>(status, true, out var s)) q = q.Where(p => p.Status == s);
        var total = await q.CountAsync();
        var list  = await q.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            orders = list.Select(p => new
            {
                id           = p.Id,
                poNumber     = p.PoNumber,
                vendorId     = p.VendorId,
                vendorName   = p.Vendor.Name,
                totalAmount  = p.TotalAmount,
                status       = p.Status.ToString(),
                expectedDate = p.ExpectedDate,
                isGrnDone    = p.IsGrnDone,
                notes        = p.Notes,
                items        = p.Items,
                createdAt    = p.CreatedAt,
            }),
            pagination = new { total, page, limit }
        }));
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] PurchaseOrderRequest req)
    {
        var vendor = await _db.Vendors.FindAsync(req.VendorId);
        if (vendor == null || !vendor.IsActive) return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found.", "vendorId"));
        if (req.TotalAmount < 0) return BadRequest(ApiResponse<object>.Fail("INVALID_AMOUNT", "Total amount cannot be negative.", "totalAmount"));
        var po = new PurchaseOrder
        {
            PoNumber     = await NextNumberAsync("PO", _db.PurchaseOrders.Select(x => x.PoNumber)),
            VendorId     = req.VendorId,
            TotalAmount  = req.TotalAmount,
            Status       = PurchaseOrderStatus.Draft,
            Items        = req.Items,
            Notes        = req.Notes,
            ExpectedDate = req.ExpectedDate,
            CreatedBy    = AdminName,
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new
        {
            id           = po.Id,
            poNumber     = po.PoNumber,
            vendorId     = po.VendorId,
            vendorName   = vendor.Name,
            totalAmount  = po.TotalAmount,
            status       = po.Status.ToString(),
            items        = po.Items,
            notes        = po.Notes,
            expectedDate = po.ExpectedDate,
            isGrnDone    = po.IsGrnDone,
            createdAt    = po.CreatedAt,
        }, "PO created."));
    }

    [HttpPut("orders/{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] PoStatusRequest req)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "PO not found."));
        if (string.IsNullOrWhiteSpace(req.Status) || !Enum.TryParse<PurchaseOrderStatus>(req.Status, true, out var s) || !Enum.IsDefined(s))
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Unknown PO status '{req.Status}'.", "status"));
        if ((po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled) && s != po.Status)
            return BadRequest(ApiResponse<object>.Fail("INVALID_TRANSITION", $"A {po.Status} PO can't be changed to {s}."));
        po.Status = s;
        if (req.IsGrnDone.HasValue) po.IsGrnDone = req.IsGrnDone.Value;
        if (s == PurchaseOrderStatus.Approved) { po.ApprovedBy = AdminName; po.ApprovedAt = DateTime.UtcNow; }
        po.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id = po.Id, status = po.Status.ToString(), isGrnDone = po.IsGrnDone }, $"PO marked {po.Status}."));
    }

    [HttpDelete("orders/{id:int}")]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "PO not found."));
        if (await _db.GoodsReceiptNotes.AnyAsync(g => g.PurchaseOrderId == id))
            return BadRequest(ApiResponse<object>.Fail("HAS_GRN", "Goods were already received against this PO, so it can't be deleted. Cancel it instead."));
        if (await _db.PurchaseReturns.AnyAsync(r => r.PurchaseOrderId == id))
            return BadRequest(ApiResponse<object>.Fail("HAS_RETURNS", "This PO has purchase returns, so it can't be deleted."));
        _db.PurchaseOrders.Remove(po);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── GRN ──────────────────────────────────────────────────────────────────
    [HttpGet("grn")]
    public async Task<IActionResult> GetGrn([FromQuery] int? poId, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.GoodsReceiptNotes
            .Include(g => g.PurchaseOrder).ThenInclude(p => p.Vendor)
            .AsQueryable();
        if (poId.HasValue) q = q.Where(g => g.PurchaseOrderId == poId.Value);
        var total = await q.CountAsync();
        var list  = await q.OrderByDescending(g => g.ReceivedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            grn = list.Select(g => new
            {
                id              = g.Id,
                grnNumber       = g.GrnNumber,
                purchaseOrderId = g.PurchaseOrderId,
                poNumber        = g.PurchaseOrder.PoNumber,
                vendorName      = g.PurchaseOrder.Vendor.Name,
                vendorInvoiceNo = g.VendorInvoiceNo,
                invoiceAmount   = g.InvoiceAmount,
                notes           = g.Notes,
                receivedAt      = g.ReceivedAt,
            }),
            pagination = new { total, page, limit }
        }));
    }

    [HttpGet("grn/{id:int}")]
    public async Task<IActionResult> GetGrnById(int id)
    {
        var g = await _db.GoodsReceiptNotes
            .Include(x => x.Items)
            .Include(x => x.PurchaseOrder).ThenInclude(p => p.Vendor)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "GRN not found."));
        return Ok(ApiResponse<object>.Ok(new
        {
            id              = g.Id,
            grnNumber       = g.GrnNumber,
            purchaseOrderId = g.PurchaseOrderId,
            poNumber        = g.PurchaseOrder.PoNumber,
            vendorName      = g.PurchaseOrder.Vendor.Name,
            vendorInvoiceNo = g.VendorInvoiceNo,
            invoiceAmount   = g.InvoiceAmount,
            notes           = g.Notes,
            receivedAt      = g.ReceivedAt,
            items           = g.Items.Select(i => new
            {
                i.Id, i.ProductName, i.Unit, i.OrderedQty, i.ReceivedQty, i.UnitPrice, i.InventoryItemId, i.RawMaterialId,
                lineTotal = i.ReceivedQty * i.UnitPrice,
            }),
        }));
    }

    [HttpPost("grn")]
    public async Task<IActionResult> CreateGrn([FromBody] GrnRequest req)
    {
        try
        {
            var po = await _db.PurchaseOrders.Include(p => p.Vendor).FirstOrDefaultAsync(p => p.Id == req.PurchaseOrderId);
            if (po == null) return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "PO not found.", "purchaseOrderId"));
            if (po.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Draft)
                return BadRequest(ApiResponse<object>.Fail("INVALID_PO", $"Goods can't be received against a {po.Status} PO."));
            if (req.Items == null || !req.Items.Any(i => (i.ReceivedQty > 0 ? i.ReceivedQty : i.Quantity) > 0))
                return BadRequest(ApiResponse<object>.Fail("NO_ITEMS", "Add at least one item with a received quantity.", "items"));
            if (req.Items.Any(i => i.UnitPrice < 0 || i.GstRate < 0 || i.Quantity < 0 || i.ReceivedQty < 0))
                return BadRequest(ApiResponse<object>.Fail("INVALID_ITEM", "Quantities, prices and GST rates cannot be negative.", "items"));
            if (req.Items.Any(i => i.RawMaterialId.HasValue && i.InventoryItemId.HasValue))
                return BadRequest(ApiResponse<object>.Fail("INVALID_ITEM", "A GRN line goes either to a raw material or to a finished inventory item, not both.", "items"));
            // Raw-material lines: stock is kept in kg, so the unit must be a weight
            var rawIds = req.Items.Where(i => i.RawMaterialId.HasValue).Select(i => i.RawMaterialId!.Value).Distinct().ToList();
            var raws = await _db.RawMaterials.Where(r => rawIds.Contains(r.Id) && !r.IsDeleted).ToDictionaryAsync(r => r.Id);
            if (raws.Count != rawIds.Count)
                return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "A raw material on this GRN was not found.", "items"));
            try { foreach (var i in req.Items.Where(i => i.RawMaterialId.HasValue)) PackingRules.ToKg(1, i.Unit ?? "kg", "items"); }
            catch (PackingException ex) { return BadRequest(ApiResponse<object>.Fail(ex.Code, ex.Message, "items")); }

            var gstType = string.IsNullOrWhiteSpace(req.GstType) ? "IntraState" : req.GstType;

            decimal taxableTotal = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;
            var grnItems = new List<GrnItem>();

            if (req.Items?.Any() == true)
            {
                foreach (var i in req.Items)
                {
                    var receivedQty = i.ReceivedQty > 0 ? i.ReceivedQty : i.Quantity;
                    var taxable     = i.UnitPrice * receivedQty;
                    decimal cgst = 0, sgst = 0, igst = 0;
                    if (gstType == "IntraState")
                    {
                        cgst = Math.Round(taxable * i.GstRate / 100 / 2, 2);
                        sgst = cgst;
                    }
                    else
                    {
                        igst = Math.Round(taxable * i.GstRate / 100, 2);
                    }
                    taxableTotal += taxable;
                    cgstTotal    += cgst;
                    sgstTotal    += sgst;
                    igstTotal    += igst;

                    grnItems.Add(new GrnItem
                    {
                        ProductName     = string.IsNullOrWhiteSpace(i.ProductName) && i.RawMaterialId.HasValue ? raws[i.RawMaterialId.Value].Name : i.ProductName,
                        Unit            = i.Unit ?? (i.RawMaterialId.HasValue ? "kg" : "pcs"),
                        OrderedQty      = i.OrderedQty > 0 ? i.OrderedQty : receivedQty,
                        ReceivedQty     = receivedQty,
                        UnitPrice       = i.UnitPrice,
                        GstRate         = i.GstRate,
                        InventoryItemId = i.InventoryItemId,
                        RawMaterialId   = i.RawMaterialId,
                        CreatedBy       = AdminName,
                    });
                }
            }

            var gstTotal = cgstTotal + sgstTotal + igstTotal;

            var grn = new GoodsReceiptNote
            {
                GrnNumber       = await NextNumberAsync("GRN", _db.GoodsReceiptNotes.Select(x => x.GrnNumber)),
                PurchaseOrderId = req.PurchaseOrderId,
                VendorInvoiceNo = req.VendorInvoiceNo,
                InvoiceAmount   = req.InvoiceAmount > 0 ? req.InvoiceAmount : taxableTotal + gstTotal,
                TaxableAmount   = taxableTotal,
                CgstAmount      = cgstTotal,
                SgstAmount      = sgstTotal,
                IgstAmount      = igstTotal,
                GstType         = gstType,
                Notes           = req.Notes,
                ReceivedAt      = DateTime.UtcNow,
                CreatedBy       = AdminName,
                Items           = grnItems,
            };

            _db.GoodsReceiptNotes.Add(grn);
            po.IsGrnDone = true;
            if (po.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.Pending) po.Status = PurchaseOrderStatus.Received;
            // The vendor's invoice becomes payable
            po.Vendor.OutstandingDues += grn.InvoiceAmount;
            po.Vendor.UpdatedAt = DateTime.UtcNow;
            po.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(); // save first to get grn.Id

            // ── Auto-update inventory ──
            int inventoryUpdated = 0;
            foreach (var gi in grn.Items)
            {
                if (gi.InventoryItemId.HasValue && gi.ReceivedQty > 0)
                {
                    var inv = await _db.InventoryItems.FindAsync(gi.InventoryItemId.Value);
                    if (inv != null && !inv.IsDeleted)
                    {
                        inv.CurrentStock += gi.ReceivedQty;
                        inv.UpdatedAt     = DateTime.UtcNow;
                        _db.InventoryLedgers.Add(new InventoryLedger
                        {
                            InventoryItemId = inv.Id,
                            Type            = StockAdjustmentType.GRNReceipt,
                            Quantity        = gi.ReceivedQty,
                            BalanceAfter    = inv.CurrentStock,
                            Reason          = $"GRN: {grn.GrnNumber}",
                            ReferenceId     = grn.GrnNumber,
                            PerformedBy     = AdminName,
                        });
                        inventoryUpdated++;
                    }
                }
                else if (gi.RawMaterialId.HasValue && gi.ReceivedQty > 0 && raws.TryGetValue(gi.RawMaterialId.Value, out var rm))
                {
                    // Raw material bought in kg / g / quintal → raw stock (kg) at weighted-average cost
                    var kg = PackingRules.ToKg(gi.ReceivedQty, gi.Unit);
                    var costPerKg = kg > 0 ? Math.Round(gi.UnitPrice * gi.ReceivedQty / kg, 2) : 0;
                    PackingRules.Receive(_db, rm, kg, costPerKg, "Purchase", AdminName, "GRN", grn.GrnNumber,
                        reason: $"GRN {grn.GrnNumber} · PO {po.PoNumber} · {po.Vendor.Name}");
                    inventoryUpdated++;
                }
            }

            // ── Auto-create ITC entry ──
            if (gstTotal > 0)
            {
                var now    = DateTime.UtcNow;
                var months = new[] { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
                var period = $"{months[now.Month - 1]} {now.Year}";

                _db.ItcEntries.Add(new ItcEntry
                {
                    Period        = period,
                    SupplierName  = po.Vendor.Name,
                    SupplierGstin = po.Vendor.Gstin ?? string.Empty,
                    InvoiceNumber = req.VendorInvoiceNo ?? grn.GrnNumber,
                    TaxableAmount = taxableTotal,
                    CgstAmount    = cgstTotal,
                    SgstAmount    = sgstTotal,
                    IgstAmount    = igstTotal,
                    TotalItc      = gstTotal,
                    IsEligible    = true,
                    SourceType    = "GRN",
                    SourceId      = grn.Id,
                    CreatedBy     = AdminName,
                });
                grn.AutoItcCreated = true;
            }

            await _db.SaveChangesAsync();

            return StatusCode(201, ApiResponse<object>.Ok(new
            {
                id               = grn.Id,
                grnNumber        = grn.GrnNumber,
                purchaseOrderId  = grn.PurchaseOrderId,
                poNumber         = po.PoNumber,
                vendorName       = po.Vendor.Name,
                vendorInvoiceNo  = grn.VendorInvoiceNo,
                invoiceAmount    = grn.InvoiceAmount,
                taxableAmount    = grn.TaxableAmount,
                cgstAmount       = grn.CgstAmount,
                sgstAmount       = grn.SgstAmount,
                igstAmount       = grn.IgstAmount,
                gstTotal,
                gstType          = grn.GstType,
                notes            = grn.Notes,
                receivedAt       = grn.ReceivedAt,
                inventoryUpdated,
                autoItcCreated   = grn.AutoItcCreated,
            }, "GRN created."));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Vendor Payments ──────────────────────────────────────────────────────
    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments([FromQuery] int? vendorId, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.VendorPayments.Include(p => p.Vendor).AsQueryable();
        if (vendorId.HasValue) q = q.Where(p => p.VendorId == vendorId.Value);
        var total = await q.CountAsync();
        var list  = await q.OrderByDescending(p => p.PaidAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            payments = list.Select(p => new
            {
                id         = p.Id,
                vendorId   = p.VendorId,
                vendorName = p.Vendor.Name,
                amount     = p.Amount,
                mode       = p.Mode,
                paymentRef = p.PaymentRef,
                notes      = p.Notes,
                paidAt     = p.PaidAt,
            }),
            pagination = new { total, page, limit }
        }));
    }

    [HttpPost("payments")]
    public async Task<IActionResult> CreatePayment([FromBody] VendorPaymentRequest req)
    {
        var vendor = await _db.Vendors.FindAsync(req.VendorId);
        if (vendor == null || !vendor.IsActive) return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found.", "vendorId"));
        if (req.Amount <= 0) return BadRequest(ApiResponse<object>.Fail("INVALID_AMOUNT", "Payment amount must be greater than zero.", "amount"));
        var pay = new VendorPayment
        {
            VendorId   = req.VendorId,
            Amount     = req.Amount,
            Mode       = string.IsNullOrWhiteSpace(req.Mode) ? "NEFT" : req.Mode,
            PaymentRef = req.PaymentRef ?? string.Empty,
            Notes      = req.Notes,
            PaidAt     = DateTime.UtcNow,
            CreatedBy  = AdminName,
        };
        _db.VendorPayments.Add(pay);
        if (vendor.OutstandingDues > 0) vendor.OutstandingDues = Math.Max(0, vendor.OutstandingDues - req.Amount);
        vendor.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new
        {
            id         = pay.Id,
            vendorId   = pay.VendorId,
            vendorName = vendor.Name,
            amount     = pay.Amount,
            mode       = pay.Mode,
            paymentRef = pay.PaymentRef,
            notes      = pay.Notes,
            paidAt     = pay.PaidAt,
        }, "Payment recorded."));
    }

    // ── Price History ─────────────────────────────────────────────────────────
    [HttpGet("price-history")]
    public async Task<IActionResult> GetPriceHistory([FromQuery] int? vendorId, [FromQuery] string? productName)
    {
        var q = _db.VendorPriceHistories.Include(p => p.Vendor).AsQueryable();
        if (vendorId.HasValue) q = q.Where(p => p.VendorId == vendorId.Value);
        if (!string.IsNullOrWhiteSpace(productName)) q = q.Where(p => p.ProductName.Contains(productName));
        var list = await q.OrderByDescending(p => p.EffectiveFrom).Take(200).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            history = list.Select(p => new
            {
                id            = p.Id,
                vendorId      = p.VendorId,
                vendorName    = p.Vendor.Name,
                productName   = p.ProductName,
                unitPrice     = p.UnitPrice,
                unit          = p.Unit,
                effectiveFrom = p.EffectiveFrom,
            })
        }));
    }

    [HttpPost("price-history")]
    public async Task<IActionResult> AddPriceHistory([FromBody] PriceHistoryRequest req)
    {
        var vendor = await _db.Vendors.FindAsync(req.VendorId);
        if (vendor == null || !vendor.IsActive) return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found.", "vendorId"));
        if (string.IsNullOrWhiteSpace(req.ProductName)) return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Product name is required.", "productName"));
        if (req.UnitPrice <= 0) return BadRequest(ApiResponse<object>.Fail("INVALID_PRICE", "Unit price must be greater than zero.", "unitPrice"));
        var ph = new VendorPriceHistory
        {
            VendorId      = req.VendorId,
            ProductName   = req.ProductName.Trim(),
            UnitPrice     = req.UnitPrice,
            Unit          = string.IsNullOrWhiteSpace(req.Unit) ? "kg" : req.Unit,
            EffectiveFrom = req.EffectiveFrom ?? DateTime.UtcNow,
            CreatedBy     = AdminName,
        };
        _db.VendorPriceHistories.Add(ph);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new
        {
            id            = ph.Id,
            vendorId      = ph.VendorId,
            vendorName    = vendor.Name,
            productName   = ph.ProductName,
            unitPrice     = ph.UnitPrice,
            unit          = ph.Unit,
            effectiveFrom = ph.EffectiveFrom,
        }, "Price added."));
    }

    // ── Purchase Returns ─────────────────────────────────────────────────────
    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns([FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.PurchaseReturns.Include(r => r.PurchaseOrder).ThenInclude(p => p.Vendor).AsQueryable();
        var total = await q.CountAsync();
        var list  = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            returns = list.Select(r => new
            {
                id              = r.Id,
                returnNumber    = r.ReturnNumber,
                purchaseOrderId = r.PurchaseOrderId,
                poNumber        = r.PurchaseOrder.PoNumber,
                vendorName      = r.PurchaseOrder.Vendor.Name,
                reason          = r.Reason,
                amount          = r.Amount,
                status          = r.Status,
                createdAt       = r.CreatedAt,
            }),
            pagination = new { total, page, limit }
        }));
    }

    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn([FromBody] PurchaseReturnRequest req)
    {
        var po = await _db.PurchaseOrders.Include(p => p.Vendor).FirstOrDefaultAsync(p => p.Id == req.PurchaseOrderId);
        if (po == null) return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "PO not found.", "purchaseOrderId"));
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Reason is required.", "reason"));
        if (req.Amount < 0) return BadRequest(ApiResponse<object>.Fail("INVALID_AMOUNT", "Amount cannot be negative.", "amount"));
        var ret = new PurchaseReturn
        {
            ReturnNumber    = await NextNumberAsync("PR", _db.PurchaseReturns.Select(x => x.ReturnNumber)),
            PurchaseOrderId = req.PurchaseOrderId,
            Reason          = req.Reason,
            Amount          = req.Amount,
            Status          = "Pending",
            CreatedBy       = AdminName,
        };
        _db.PurchaseReturns.Add(ret);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new
        {
            id              = ret.Id,
            returnNumber    = ret.ReturnNumber,
            purchaseOrderId = ret.PurchaseOrderId,
            poNumber        = po.PoNumber,
            vendorName      = po.Vendor.Name,
            reason          = ret.Reason,
            amount          = ret.Amount,
            status          = ret.Status,
            createdAt       = ret.CreatedAt,
        }, "Return created."));
    }

    [HttpPut("returns/{id:int}/status")]
    public async Task<IActionResult> UpdateReturnStatus(int id, [FromBody] ReturnStatusRequest req)
    {
        var ret = await _db.PurchaseReturns.Include(r => r.PurchaseOrder).ThenInclude(p => p.Vendor).FirstOrDefaultAsync(r => r.Id == id);
        if (ret == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Return not found."));
        var status = ReturnStatuses.FirstOrDefault(x => string.Equals(x, req.Status, StringComparison.OrdinalIgnoreCase));
        if (status == null)
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Status must be one of: {string.Join(", ", ReturnStatuses)}.", "status"));
        if (ret.Status != "Pending")
            return BadRequest(ApiResponse<object>.Fail("ALREADY_DECIDED", $"This return is already {ret.Status}."));
        ret.Status    = status;
        ret.UpdatedAt = DateTime.UtcNow;
        ret.UpdatedBy = AdminName;
        // An approved return is a debit note: it reduces what we owe the vendor
        if (status == "Approved" && ret.Amount > 0)
        {
            var v = ret.PurchaseOrder.Vendor;
            v.OutstandingDues = Math.Max(0, v.OutstandingDues - ret.Amount);
            v.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id = ret.Id, status = ret.Status }, $"Return {status.ToLower()}."));
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private static readonly string[] ReturnStatuses = { "Pending", "Approved", "Rejected" };

    private static ApiResponse<object>? ValidateVendor(VendorRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return ApiResponse<object>.Fail("REQUIRED", "Vendor name is required.", "name");
        if (string.IsNullOrWhiteSpace(req.Phone)) return ApiResponse<object>.Fail("REQUIRED", "Phone is required.", "phone");
        if (!string.IsNullOrWhiteSpace(req.Gstin) && !System.Text.RegularExpressions.Regex.IsMatch(req.Gstin.Trim().ToUpper(), "^[0-9]{2}[A-Z0-9]{13}$"))
            return ApiResponse<object>.Fail("INVALID_GSTIN", "GSTIN must be 15 characters (e.g. 27AAAAA0000A1Z5).", "gstin");
        return null;
    }

    /// Next "{prefix}-{year}-{seq:D4}" number, safe after deletions (uses the highest existing sequence).
    private static async Task<string> NextNumberAsync(string prefix, IQueryable<string> numbers)
    {
        var start = $"{prefix}-{DateTime.UtcNow.Year}-";
        var existing = await numbers.Where(n => n.StartsWith(start)).ToListAsync();
        var max = existing.Select(n => int.TryParse(n[start.Length..], out var k) ? k : 0).DefaultIfEmpty(0).Max();
        return $"{start}{(max + 1):D4}";
    }
    private static object MapVendor(Vendor v) => new
    {
        id              = v.Id,
        name            = v.Name,
        gstin           = v.Gstin,
        city            = v.City,
        state           = v.State,
        phone           = v.Phone,
        email           = v.Email,
        address         = v.Address,
        bankAccountNo   = SensitiveMask.Tail(v.BankAccountNo),   // full number via audited reveal
        bankIfsc        = v.BankIfsc,
        bankName        = v.BankName,
        rating          = v.Rating,
        outstandingDues = v.OutstandingDues,
    };
}

// ── Request records ───────────────────────────────────────────────────────────
public record VendorRequest(string Name, string? Gstin, string? City, string? State, string Phone, string? Email, string? Address, string? BankAccountNo, string? BankIfsc, string? BankName);
public record PurchaseOrderRequest(int VendorId, string? Items, string? Notes, DateTime? ExpectedDate, decimal TotalAmount = 0);
public record PoStatusRequest(string Status, bool? IsGrnDone);

public record GrnItemRequest(
    string ProductName,
    string? Unit,
    int? InventoryItemId,
    int OrderedQty,
    int ReceivedQty,
    int Quantity,
    decimal UnitPrice,
    decimal GstRate = 0,
    int? RawMaterialId = null   // raw material line → raw stock (kg) instead of finished goods
);
public record GrnRequest(
    int PurchaseOrderId,
    string? VendorInvoiceNo,
    string? InvoiceDate,
    decimal InvoiceAmount,
    decimal TaxableAmount,
    decimal GstAmount,
    string? GstType,
    string? Notes,
    List<GrnItemRequest>? Items
);

public record VendorPaymentRequest(int VendorId, decimal Amount, string Mode, string? PaymentRef, string? Notes);
public record PriceHistoryRequest(int VendorId, string ProductName, decimal UnitPrice, string Unit, DateTime? EffectiveFrom);
public record PurchaseReturnRequest(int PurchaseOrderId, string Reason, decimal Amount);
public record ReturnStatusRequest(string Status);
