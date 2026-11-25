using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Invoice;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Invoicing;

namespace WahSpices.Infrastructure.Services;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _db;
    private readonly IEmailComposer _email;
    private readonly IConfiguration _config;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(AppDbContext db, IEmailComposer email,
        IConfiguration config, ILogger<InvoiceService> logger)
    {
        _db = db;
        _email = email;
        _config = config;
        _logger = logger;
    }

    // ── Auto-generate invoice from a confirmed order ──────────────────────
    public async Task<InvoiceDto> GenerateFromOrderAsync(int orderId)
    {
        // If invoice already exists for this order, return it
        var existing = await _db.Invoices
            .Include(x => x.LineItems)
            .Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.OrderId == orderId && !x.IsDeleted);

        if (existing != null)
        {
            var existSettings = await GetOrCreateSettingsAsync();
            return MapToDto(existing, existSettings);
        }

        var order = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Address)
            .Include(o => o.Items).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted)
            ?? throw AppException.NotFound("Order");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Returned)
            throw new AppException($"Order {order.OrderNumber} is {order.Status} — an invoice can't be generated for it.", 400, "ORDER_NOT_BILLABLE");
        if (!order.Items.Any(i => !i.IsDeleted))
            throw new AppException($"Order {order.OrderNumber} has no items.", 400, "ORDER_NOT_BILLABLE");

        var settings = await GetOrCreateSettingsAsync();
        var now = DateTime.UtcNow;
        var invoiceNumber = await NextInvoiceNumberAsync(settings, now);

        // Place of supply = delivery state: same state as the business → CGST + SGST, otherwise IGST
        var pos = GstStates.NameOf(order.Address?.State) ?? order.Address?.State;
        var gstType = GstStates.SameState(settings.State, order.Address?.State) || string.IsNullOrWhiteSpace(order.Address?.State)
            ? GSTType.IntraState : GSTType.InterState;

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            OrderId = orderId,
            CustomerName = string.IsNullOrWhiteSpace(order.User?.Name) ? (order.Address?.Name ?? "Customer") : order.User.Name,
            CustomerEmail = order.User?.Email,
            CustomerPhone = string.IsNullOrWhiteSpace(order.User?.Phone) ? order.Address?.Phone : order.User.Phone,
            CustomerGstin = null,
            BillingAddress = order.Address != null ? SerializeAddress(order.Address) : null,
            ShippingAddress = order.Address != null ? SerializeAddress(order.Address) : null,
            Type = InvoiceType.B2C,
            Status = order.PaymentStatus == PaymentStatus.Paid ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
            GstType = gstType,
            PlaceOfSupply = pos,
            PaymentMethod = EmailTemplates.MethodName(order.PaymentMethod),
            InvoiceDate = now,
            Source = "Online",
            DeliveryCharge = order.DeliveryCharge,
            CouponDiscount = order.CouponDiscount,
            Notes = order.Notes,
            CreatedBy = "System"
        };

        // Prices are GST-inclusive. A coupon is a discount on the invoice, so it is spread over the lines
        // (in proportion to their value) and reduces each line's taxable value.
        var items = order.Items.Where(i => !i.IsDeleted).ToList();
        var gross = items.Sum(i => i.Price * i.Quantity);
        var couponLeft = Math.Min(order.CouponDiscount, gross);
        decimal subtotal = 0, discount = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;

        for (var idx = 0; idx < items.Count; idx++)
        {
            var oi = items[idx];
            var gstRate = oi.Product?.GstRate ?? 5m;
            var halfGst = gstRate / 2;
            var lineGross = oi.Price * oi.Quantity;
            var coupon = idx == items.Count - 1 ? couponLeft
                : gross > 0 ? Math.Round(order.CouponDiscount * lineGross / gross, 2) : 0;
            coupon = Math.Min(coupon, couponLeft);
            couponLeft -= coupon;

            var lineNet = lineGross - coupon;                               // incl. GST
            var taxableAmt = Math.Round(lineNet / (1 + gstRate / 100), 2);
            var mrpUnit = oi.Mrp > 0 ? oi.Mrp : oi.Price;
            var mrpTaxable = Math.Round(mrpUnit * oi.Quantity / (1 + gstRate / 100), 2);
            var discAmt = Math.Max(0, mrpTaxable - taxableAmt);            // MRP savings + coupon, ex-GST
            var discPct = mrpTaxable > 0 ? discAmt / mrpTaxable * 100 : 0;

            decimal cgst = 0, sgst = 0, igst = 0;
            if (gstType == GSTType.IntraState)
            {
                cgst = Math.Round(taxableAmt * halfGst / 100, 2);
                sgst = cgst;
            }
            else igst = Math.Round(taxableAmt * gstRate / 100, 2);

            var name = oi.ProductName;
            if (!string.IsNullOrWhiteSpace(oi.Weight) && !name.Contains(oi.Weight, StringComparison.OrdinalIgnoreCase))
                name = $"{name} ({oi.Weight})";

            invoice.LineItems.Add(new InvoiceLineItem
            {
                ProductId = oi.ProductId,
                ProductName = name,
                HsnCode = oi.Product?.HsnCode,
                Quantity = oi.Quantity,
                Unit = "pcs",
                UnitPrice = oi.Price,
                MrpPrice = mrpUnit,
                DiscountPercent = Math.Round(discPct, 2),
                DiscountAmount = Math.Round(discAmt, 2),
                TaxableAmount = taxableAmt,
                GstRate = gstRate,
                CgstRate = gstType == GSTType.IntraState ? halfGst : 0,
                SgstRate = gstType == GSTType.IntraState ? halfGst : 0,
                IgstRate = gstType == GSTType.InterState ? gstRate : 0,
                CgstAmount = cgst,
                SgstAmount = sgst,
                IgstAmount = igst,
                TaxAmount = cgst + sgst + igst,
                LineTotal = taxableAmt + cgst + sgst + igst,
                CreatedBy = "System"
            });
            subtotal += taxableAmt; discount += discAmt;
            cgstTotal += cgst; sgstTotal += sgst; igstTotal += igst;
        }

        invoice.Subtotal = Math.Round(subtotal, 2);
        invoice.DiscountAmount = Math.Round(discount, 2);
        invoice.CgstAmount = Math.Round(cgstTotal, 2);
        invoice.SgstAmount = Math.Round(sgstTotal, 2);
        invoice.IgstAmount = Math.Round(igstTotal, 2);
        var computed = invoice.Subtotal + invoice.CgstAmount + invoice.SgstAmount + invoice.IgstAmount + order.DeliveryCharge;
        // The invoice must match what the customer paid: absorb paise rounding differences in "Round off".
        var diff = order.GrandTotal - computed;
        invoice.RoundOff = Math.Abs(diff) <= 1m ? diff : 0;
        if (Math.Abs(diff) > 1m) _logger.LogWarning("Invoice for order {Id}: computed {Computed} differs from order total {Total}", orderId, computed, order.GrandTotal);
        invoice.TotalAmount = Math.Round(computed + invoice.RoundOff, 2);

        _db.Invoices.Add(invoice);
        await CrmLastOrder.TouchAsync(_db, invoice.CustomerName, invoice.CustomerPhone, invoice.CustomerGstin, invoice.CustomerEmail, invoice.CreatedAt);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invoice {Num} generated for Order {Id}", invoiceNumber, orderId);
        invoice.Order = order;
        return MapToDto(invoice, settings);
    }

    // ── Create manual invoice (admin) ─────────────────────────────────────
    public async Task<InvoiceDto> CreateManualAsync(CreateInvoiceRequest request, string createdBy)
    {
        ValidateManual(request);
        var settings = await GetOrCreateSettingsAsync();

        // ── Stock validation before creating invoice (same inventory item on several rows is summed) ──
        foreach (var grp in request.Items!.Where(i => i.InventoryItemId.HasValue).GroupBy(i => i.InventoryItemId!.Value))
        {
            var inv = await _db.InventoryItems.FindAsync(grp.Key)
                ?? throw new AppException($"Inventory item #{grp.Key} not found.", 400, "VALIDATION_ERROR", "items");
            var needed = grp.Sum(i => i.Quantity);
            if (inv.CurrentStock < needed)
                throw new AppException(
                    $"Insufficient stock for '{grp.First().ProductName}'. Available: {inv.CurrentStock}, Requested: {needed}", 400, "INSUFFICIENT_STOCK", "items");
        }

        if (request.OrderId.HasValue)
        {
            if (!await _db.Orders.AnyAsync(o => o.Id == request.OrderId && !o.IsDeleted))
                throw new AppException($"Order #{request.OrderId} not found.", 400, "VALIDATION_ERROR", "orderId");
            if (await _db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.OrderId == request.OrderId))
                throw new AppException("This order already has an invoice.", 400, "INVOICE_EXISTS", "orderId");
        }

        var invoiceDate = request.InvoiceDate.HasValue
            ? DateTime.SpecifyKind(request.InvoiceDate.Value.Date.AddHours(6.5), DateTimeKind.Utc)   // noon IST on that day
            : DateTime.UtcNow;
        var invoiceNumber = await NextInvoiceNumberAsync(settings, invoiceDate);

        var invoiceType = Enum.Parse<InvoiceType>(request.Type!, ignoreCase: true);
        // Place of supply decides the tax split unless the admin forced a GST type
        var pos = GstStates.NameOf(request.PlaceOfSupply?.Trim()) ?? (string.IsNullOrWhiteSpace(request.PlaceOfSupply) ? null : request.PlaceOfSupply.Trim());
        if (pos == null && invoiceType == InvoiceType.B2B && request.CustomerGstin?.Trim() is { Length: >= 2 } g && char.IsDigit(g[0]))
            pos = GstStates.NameOf(g[..2]);
        var gstType = !string.IsNullOrWhiteSpace(request.GstType)
            ? Enum.Parse<GSTType>(request.GstType!, ignoreCase: true)
            : pos == null || GstStates.SameState(settings.State, pos) ? GSTType.IntraState : GSTType.InterState;
        var paidNow = string.Equals(request.Status?.Trim(), "Paid", StringComparison.OrdinalIgnoreCase);

        var invoice = new Invoice
        {
            InvoiceNumber   = invoiceNumber,
            OrderId         = request.OrderId,
            CustomerName    = request.CustomerName!.Trim(),
            CustomerEmail   = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail.Trim(),
            CustomerPhone   = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
            CustomerGstin   = string.IsNullOrWhiteSpace(request.CustomerGstin) ? null : request.CustomerGstin.Trim().ToUpperInvariant(),
            BillingAddress  = string.IsNullOrWhiteSpace(request.BillingAddress) ? null : request.BillingAddress.Trim(),
            ShippingAddress = string.IsNullOrWhiteSpace(request.ShippingAddress) ? null : request.ShippingAddress.Trim(),
            Type            = invoiceType,
            GstType         = gstType,
            Status          = paidNow ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
            DeliveryCharge  = request.DeliveryCharge,
            Notes           = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            InvoiceDate     = invoiceDate,
            PlaceOfSupply   = pos,
            PaymentMethod   = string.IsNullOrWhiteSpace(request.PaymentMethod) ? null : request.PaymentMethod.Trim(),
            Source          = request.OrderId.HasValue ? "Online" : "Offline",
            CreatedBy       = createdBy
        };

        decimal subtotal = 0, discountTotal = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;
        var hsnMaster = await HsnRates.ActiveAsync(_db);

        foreach (var item in request.Items!)
        {
            // GST rate: the HSN master's rate when the line's HSN is in it, otherwise the rate typed on the bill
            var lineRate = HsnRates.Resolve(hsnMaster, item.HsnCode)?.GstRate ?? item.GstRate;
            var halfGst = lineRate / 2;
            var discAmt = Math.Round(item.UnitPrice * item.Quantity * item.DiscountPercent / 100, 2);
            var taxable = Math.Round(item.UnitPrice * item.Quantity - discAmt, 2);

            decimal cgst = 0, sgst = 0, igst = 0;
            if (gstType == GSTType.IntraState)
            {
                cgst = Math.Round(taxable * halfGst / 100, 2);
                sgst = cgst;
            }
            else
            {
                igst = Math.Round(taxable * lineRate / 100, 2);
            }

            invoice.LineItems.Add(new InvoiceLineItem
            {
                ProductId       = item.ProductId,
                InventoryItemId = item.InventoryItemId,
                ProductName     = item.ProductName!.Trim(),
                HsnCode         = item.HsnCode,
                Quantity        = item.Quantity,
                Unit            = string.IsNullOrWhiteSpace(item.Unit) ? "pcs" : item.Unit,
                UnitPrice       = item.UnitPrice,
                MrpPrice        = item.MrpPrice,
                DiscountPercent = item.DiscountPercent,
                DiscountAmount  = discAmt,
                TaxableAmount   = taxable,
                GstRate         = lineRate,
                CgstRate        = gstType == GSTType.IntraState ? halfGst : 0,
                SgstRate        = gstType == GSTType.IntraState ? halfGst : 0,
                IgstRate        = gstType != GSTType.IntraState ? lineRate : 0,
                CgstAmount      = cgst,
                SgstAmount      = sgst,
                IgstAmount      = igst,
                TaxAmount       = cgst + sgst + igst,
                LineTotal       = taxable + cgst + sgst + igst,
                CreatedBy       = createdBy
            });

            subtotal  += taxable;
            discountTotal += discAmt;
            cgstTotal += cgst;
            sgstTotal += sgst;
            igstTotal += igst;
        }

        invoice.Subtotal    = Math.Round(subtotal, 2);
        invoice.DiscountAmount = Math.Round(discountTotal, 2);
        invoice.CgstAmount  = Math.Round(cgstTotal, 2);
        invoice.SgstAmount  = Math.Round(sgstTotal, 2);
        invoice.IgstAmount  = Math.Round(igstTotal, 2);
        var exact = invoice.Subtotal + invoice.CgstAmount + invoice.SgstAmount + invoice.IgstAmount + request.DeliveryCharge;
        invoice.RoundOff    = Math.Round(Math.Round(exact, MidpointRounding.AwayFromZero) - exact, 2);   // round the bill to whole rupees
        invoice.TotalAmount = Math.Round(exact + invoice.RoundOff, 2);

        _db.Invoices.Add(invoice);
        await CrmLastOrder.TouchAsync(_db, invoice.CustomerName, invoice.CustomerPhone, invoice.CustomerGstin, invoice.CustomerEmail, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        // ── Deduct stock after invoice is saved ──
        foreach (var li in invoice.LineItems)
        {
            if (li.InventoryItemId.HasValue && li.Quantity > 0)
            {
                var inv = await _db.InventoryItems.FindAsync(li.InventoryItemId.Value);
                if (inv != null)
                {
                    inv.CurrentStock    -= li.Quantity;
                    inv.UpdatedAt        = DateTime.UtcNow;
                    li.StockDeducted     = true;
                    _db.InventoryLedgers.Add(new InventoryLedger
                    {
                        InventoryItemId = inv.Id,
                        Type            = StockAdjustmentType.SaleDeduction,
                        Quantity        = -li.Quantity,
                        BalanceAfter    = inv.CurrentStock,
                        Reason          = $"Invoice: {invoice.InvoiceNumber}",
                        ReferenceId     = invoice.InvoiceNumber,
                        PerformedBy     = createdBy,
                    });
                }
            }
        }
        await _db.SaveChangesAsync();

        return MapToDto(invoice, settings);
    }

    // ── List invoices (server-side status filter + paging) ────────────────
    /// Status may be one name or a comma list ("Unpaid,PartiallyPaid,Overdue"); an unknown name is a 400.
    private static List<InvoiceStatus>? ParseStatuses(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var list = new List<InvoiceStatus>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out _) || !Enum.TryParse<InvoiceStatus>(part, true, out var st) || !Enum.IsDefined(st))
                throw new AppException($"Invalid invoice status '{part}'. Allowed: {string.Join(", ", Enum.GetNames<InvoiceStatus>())}.", 400, "INVALID_STATUS", "status");
            list.Add(st);
        }
        return list;
    }

    private IQueryable<Domain.Entities.Invoice> FilteredInvoices(InvoiceFilterParams filter, bool applyStatus)
    {
        var query = _db.Invoices.Where(x => !x.IsDeleted);

        var statuses = applyStatus ? ParseStatuses(filter.Status) : null;
        if (statuses != null) query = query.Where(x => statuses.Contains(x.Status));

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            if (int.TryParse(filter.Type, out _) || !Enum.TryParse<InvoiceType>(filter.Type, true, out var t) || !Enum.IsDefined(t))
                throw new AppException($"Invalid invoice type '{filter.Type}'. Allowed: {string.Join(", ", Enum.GetNames<InvoiceType>())}.", 400, "VALIDATION_ERROR", "type");
            query = query.Where(x => x.Type == t);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var kw = filter.Search.Trim().ToLower();
            query = query.Where(x =>
                x.InvoiceNumber.ToLower().Contains(kw) ||
                x.CustomerName.ToLower().Contains(kw) ||
                (x.CustomerPhone != null && x.CustomerPhone.Contains(kw)) ||
                (x.CustomerEmail != null && x.CustomerEmail.ToLower().Contains(kw)) ||
                (x.Order != null && x.Order.OrderNumber.ToLower().Contains(kw)));
        }

        if (filter.FromDate.HasValue) query = query.Where(x => x.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(x => x.CreatedAt < filter.ToDate.Value.Date.AddDays(1));
        return query;
    }

    public async Task<PagedResult<InvoiceListItemDto>> GetAllAsync(InvoiceFilterParams filter)
    {
        var page = Math.Max(1, filter.Page);
        var limit = Math.Clamp(filter.Limit, 1, 200);
        var query = FilteredInvoices(filter, applyStatus: true);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(x => new InvoiceListItemDto(
                x.Id, x.InvoiceNumber, x.CustomerName, x.CustomerEmail, x.CustomerPhone,
                x.Type, x.Status, x.Subtotal, x.TotalAmount,
                x.CgstAmount + x.SgstAmount + x.IgstAmount,
                x.CreatedAt, x.OrderId, x.Order != null ? x.Order.OrderNumber : null,
                x.LineItems.Count(li => !li.IsDeleted),
                x.Source, x.LastSentAt, x.SentCount, x.Irn != null && x.Irn != "", x.LastSentTo))
            .ToListAsync();

        return new PagedResult<InvoiceListItemDto> { Items = items, Total = total, Page = page, Limit = limit };
    }

    /// KPI tiles + per-status counts for the Billing page (same search / date / type filters, all statuses).
    public async Task<InvoiceSummaryDto> GetSummaryAsync(InvoiceFilterParams filter)
    {
        var rows = await FilteredInvoices(filter, applyStatus: false)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(x => x.TotalAmount), Gst = g.Sum(x => x.CgstAmount + x.SgstAmount + x.IgstAmount) })
            .ToListAsync();
        var unpaid = new[] { InvoiceStatus.Unpaid, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };
        var billed = rows.Where(r => r.Status != InvoiceStatus.Cancelled).ToList();
        return new InvoiceSummaryDto(
            rows.Sum(r => r.Count),
            billed.Sum(r => r.Total),
            billed.Sum(r => r.Gst),
            rows.Where(r => r.Status == InvoiceStatus.Paid).Sum(r => r.Count),
            rows.Where(r => unpaid.Contains(r.Status)).Sum(r => r.Count),
            rows.Where(r => unpaid.Contains(r.Status)).Sum(r => r.Total),
            rows.ToDictionary(r => r.Status.ToString(), r => r.Count));
    }

    public async Task<InvoiceDto?> GetByIdAsync(int id)
    {
        var inv = await LoadInvoiceAsync(x => x.Id == id);
        if (inv == null) return null;
        var settings = await GetOrCreateSettingsAsync();
        return MapToDto(inv, settings);
    }

    public async Task<InvoiceDto?> GetByOrderIdAsync(int orderId)
    {
        var inv = await LoadInvoiceAsync(x => x.OrderId == orderId);
        if (inv == null) return null;
        var settings = await GetOrCreateSettingsAsync();
        return MapToDto(inv, settings);
    }

    public async Task<InvoiceDto?> GetByInvoiceNumberAsync(string invoiceNumber)
    {
        var inv = await LoadInvoiceAsync(x => x.InvoiceNumber == invoiceNumber);
        if (inv == null) return null;
        var settings = await GetOrCreateSettingsAsync();
        return MapToDto(inv, settings);
    }

    // ── Invoice settings (template config) ───────────────────────────────
    public async Task<InvoiceSettingsDto> GetSettingsAsync()
        => MapSettings(await GetOrCreateSettingsAsync());

    public async Task<InvoiceSettingsDto> UpdateSettingsAsync(UpdateInvoiceSettingsRequest req, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyName))
            throw new AppException("Company name is required.", 400, "VALIDATION_ERROR", "companyName");
        if (string.IsNullOrWhiteSpace(req.InvoicePrefix) || req.InvoicePrefix.Contains('/'))
            throw new AppException("Invoice prefix is required and cannot contain '/'.", 400, "VALIDATION_ERROR", "invoicePrefix");

        // State + GST state code: either may be sent; they must agree. The state is stored by its official name.
        var state = req.State?.Trim() ?? "";
        var codeIn = req.StateCode?.Trim();
        if (!string.IsNullOrEmpty(codeIn))
        {
            var byCode = GstStates.NameOf(codeIn.Length == 1 ? "0" + codeIn : codeIn);
            if (byCode == null || !codeIn.All(char.IsDigit))
                throw new AppException($"'{codeIn}' is not a valid GST state code (e.g. 24 for Gujarat).", 400, "VALIDATION_ERROR", "stateCode");
            if (state.Length > 0 && GstStates.CodeOf(state) is { } sc && sc != GstStates.CodeOf(byCode))
                throw new AppException($"State code {codeIn} is {byCode}, but the state is '{state}'. Fix one of them.", 400, "VALIDATION_ERROR", "stateCode");
            state = byCode;
        }
        else if (state.Length > 0 && GstStates.NameOf(state) is { } official)
            state = official;
        if (!string.IsNullOrWhiteSpace(req.Gstin) && req.Gstin.Trim().Length >= 2 && GstStates.CodeOf(state) is { } stc
            && char.IsDigit(req.Gstin.Trim()[0]) && req.Gstin.Trim()[..2] != stc)
            throw new AppException($"GSTIN {req.Gstin.Trim()} belongs to state code {req.Gstin.Trim()[..2]}, but the state is {state} ({stc}).", 400, "VALIDATION_ERROR", "gstin");

        ValidateTemplate(req);
        var settings = await GetOrCreateSettingsAsync();
        ApplySettings(settings, req, state);
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync();
        return MapSettings(settings);
    }

    private static readonly System.Text.RegularExpressions.Regex HexColor = new("^#[0-9a-fA-F]{6}$");
    private static readonly System.Text.RegularExpressions.Regex UpiRegex = new(@"^[A-Za-z0-9.\-_]{2,256}@[A-Za-z]{2,64}$");

    private static void ValidateTemplate(UpdateInvoiceSettingsRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.PrimaryColor) && !HexColor.IsMatch(req.PrimaryColor.Trim()))
            throw new AppException("Primary colour must be a hex colour like #1a5c2e.", 400, "VALIDATION_ERROR", "primaryColor");
        if (!string.IsNullOrWhiteSpace(req.UpiId) && !UpiRegex.IsMatch(req.UpiId.Trim()))
            throw new AppException("UPI ID looks invalid (e.g. wahspices@okaxis).", 400, "VALIDATION_ERROR", "upiId");
        if (!string.IsNullOrWhiteSpace(req.NumberFormat) && !new[] { "FY", "Year" }.Contains(req.NumberFormat.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new AppException("Number format must be FY or Year.", 400, "VALIDATION_ERROR", "numberFormat");
        if (req.InvoiceTitle?.Trim().Length > 40)
            throw new AppException("Invoice title is too long (max 40).", 400, "VALIDATION_ERROR", "invoiceTitle");
        if ((req.InvoicePrefix?.Trim().Length ?? 0) > 6)
            throw new AppException("Invoice prefix can be at most 6 characters (GST allows 16 characters for the whole invoice number).", 400, "VALIDATION_ERROR", "invoicePrefix");
        foreach (var (name, v) in new[] { ("companyLogo", req.CompanyLogo), ("signatureImage", req.SignatureImage), ("stampImage", req.StampImage) })
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            var t = v.Trim();
            var ok = (t.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) || t.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) || t.StartsWith("data:image/jpg;base64,", StringComparison.OrdinalIgnoreCase))
                     || (t.StartsWith('/') && !t.StartsWith("//")) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!ok) throw new AppException("Images must be PNG/JPG uploads (or an https / site link).", 400, "VALIDATION_ERROR", name);
            if (t.Length > 600_000) throw new AppException("Image is too large — keep it under about 400 KB.", 400, "VALIDATION_ERROR", name);
        }
    }

    private static void ApplySettings(InvoiceSettings settings, UpdateInvoiceSettingsRequest req, string state)
    {
        static string? N(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        settings.InvoiceTitle = N(req.InvoiceTitle) ?? "Tax Invoice";
        settings.UdyamNumber = N(req.UdyamNumber);
        settings.UpiId = N(req.UpiId);
        settings.SignatureImage = N(req.SignatureImage);
        settings.StampImage = N(req.StampImage);
        settings.AuthorisedSignatory = N(req.AuthorisedSignatory);
        settings.Declaration = N(req.Declaration);
        settings.NumberFormat = string.Equals(N(req.NumberFormat), "Year", StringComparison.OrdinalIgnoreCase) ? "Year" : "FY";
        settings.ShowBankDetails = req.ShowBankDetails;
        settings.ShowUpiQr = req.ShowUpiQr;
        settings.ShowMrpColumn = req.ShowMrpColumn;
        settings.ShowAmountInWords = req.ShowAmountInWords;
        settings.ShowHsnSummary = req.ShowHsnSummary;
        settings.AutoGenerateOnOrder = req.AutoGenerateOnOrder;
        settings.AttachPdfToEmails = req.AttachPdfToEmails;
        settings.EInvoiceEnabled = req.EInvoiceEnabled;
        settings.CompanyName = req.CompanyName!.Trim();
        settings.CompanyLogo = req.CompanyLogo;
        settings.Address = req.Address ?? "";
        settings.City = req.City ?? "";
        settings.State = state;
        settings.Pincode = req.Pincode ?? "";
        settings.Phone = req.Phone ?? "";
        settings.Email = req.Email ?? "";
        settings.Website = req.Website;
        settings.Gstin = string.IsNullOrWhiteSpace(req.Gstin) ? null : req.Gstin.Trim().ToUpperInvariant();
        settings.Pan = req.Pan;
        settings.Fssai = req.Fssai;
        settings.BankName = req.BankName;
        settings.BankAccount = req.BankAccount;
        settings.BankIfsc = req.BankIfsc;
        settings.BankBranch = req.BankBranch;
        settings.PrimaryColor = string.IsNullOrWhiteSpace(req.PrimaryColor) ? "#1a5c2e" : req.PrimaryColor;
        settings.InvoicePrefix = req.InvoicePrefix.Trim();
        settings.TermsAndConditions = req.TermsAndConditions;
        settings.FooterText = req.FooterText;
        settings.ShowSignature = req.ShowSignature;
        settings.ShowStamp = req.ShowStamp;
        settings.ShowHsnColumn = req.ShowHsnColumn;
        settings.ShowDiscountColumn = req.ShowDiscountColumn;
    }

    // ── Send invoice via email or WhatsApp ────────────────────────────────
    public async Task<SendInvoiceResultDto> SendInvoiceAsync(int id, SendInvoiceRequest request)
    {
        var inv = await LoadInvoiceAsync(x => x.Id == id)
            ?? throw AppException.NotFound("Invoice");

        var settings = await GetOrCreateSettingsAsync();

        if (request.Via.Equals("email", StringComparison.OrdinalIgnoreCase))
        {
            var toEmail = (string.IsNullOrWhiteSpace(request.Email) ? inv.CustomerEmail : request.Email)?.Trim();
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new AppException("Customer email is required to send invoice", 400, "NO_EMAIL");
            if (!System.Text.RegularExpressions.Regex.IsMatch(toEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") || toEmail.Length > 256)
                throw new AppException("Enter a valid email address.", 400, "VALIDATION_ERROR", "email");
            if (request.Message?.Length > 1000)
                throw new AppException("Message is too long (max 1000 characters).", 400, "VALIDATION_ERROR", "message");

            var (pdf, fileName) = await RenderAsync(inv, settings);
            var model = BuildInvoiceEmailModel(inv, settings, request.Message?.Trim(), pdfAttached: true);
            try
            {
                await _email.SendNowAsync(toEmail, inv.CustomerName, "invoice", b => EmailTemplates.Invoice(b, model),
                    attachments: new[] { new EmailAttachment(fileName, pdf) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending invoice {Num} failed", inv.InvoiceNumber);
                throw new AppException("The email could not be sent right now. Please check the email settings and try again.", 502, "EMAIL_FAILED");
            }

            inv.LastSentAt = DateTime.UtcNow;
            inv.LastSentTo = toEmail;
            inv.SentCount++;
            await _db.SaveChangesAsync();

            return new SendInvoiceResultDto(true, "email", null,
                $"Invoice sent to {toEmail}");
        }

        if (request.Via.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
        {
            var phone = (request.Phone ?? inv.CustomerPhone ?? "").Replace("+", "").Replace(" ", "");
            if (string.IsNullOrWhiteSpace(phone))
                throw new AppException("Customer phone is required to share on WhatsApp", 400, "NO_PHONE");

            // Ensure Indian number (add 91 if 10 digits)
            if (phone.Length == 10) phone = "91" + phone;

            var baseUrl = request.BaseUrl?.TrimEnd('/') ?? "https://wahspices.com";
            var invoiceUrl = $"{baseUrl}/invoice/{inv.InvoiceNumber}";
            var message = Uri.EscapeDataString(
                $"Hello {inv.CustomerName}! 🌶️\n\nYour invoice from {settings.CompanyName}\n" +
                $"Invoice: {inv.InvoiceNumber}\nAmount: ₹{inv.TotalAmount}\n\nView & Download: {invoiceUrl}\n\nThank you for shopping with us!");

            var whatsappUrl = $"https://wa.me/{phone}?text={message}";

            return new SendInvoiceResultDto(true, "whatsapp", whatsappUrl,
                "WhatsApp URL generated. Open in browser to share.");
        }

        throw new AppException($"Invalid 'via' value '{request.Via}'. Use 'email' or 'whatsapp'", 400, "INVALID_VIA");
    }

    // ── Update invoice status ─────────────────────────────────────────────
    public async Task<InvoiceDto> UpdateStatusAsync(int id, string status, string updatedBy)
    {
        var inv = await LoadInvoiceAsync(x => x.Id == id)
            ?? throw AppException.NotFound("Invoice");

        if (!Enum.TryParse<InvoiceStatus>(status, true, out var newStatus) || !Enum.IsDefined(newStatus) || int.TryParse(status, out _))
            throw new AppException($"Invalid invoice status '{status}'. Allowed: {string.Join(", ", Enum.GetNames<InvoiceStatus>())}.", 400, "INVALID_STATUS", "status");
        if (inv.Status == InvoiceStatus.Cancelled && newStatus != InvoiceStatus.Cancelled)
            throw new AppException("A cancelled invoice cannot be re-opened (its stock was returned). Create a new invoice instead.", 400, "INVOICE_CANCELLED", "status");

        // ── Restore stock when invoice is cancelled ──
        if (newStatus == InvoiceStatus.Cancelled && inv.Status != InvoiceStatus.Cancelled)
        {
            foreach (var li in inv.LineItems.Where(l => l.StockDeducted && !l.IsDeleted))
            {
                if (li.InventoryItemId.HasValue && li.Quantity > 0)
                {
                    var invItem = await _db.InventoryItems.FindAsync(li.InventoryItemId.Value);
                    if (invItem != null)
                    {
                        invItem.CurrentStock += li.Quantity;
                        invItem.UpdatedAt     = DateTime.UtcNow;
                        li.StockDeducted      = false;
                        _db.InventoryLedgers.Add(new InventoryLedger
                        {
                            InventoryItemId = invItem.Id,
                            Type            = StockAdjustmentType.SaleReversal,
                            Quantity        = li.Quantity,
                            BalanceAfter    = invItem.CurrentStock,
                            Reason          = $"Invoice Cancelled: {inv.InvoiceNumber}",
                            ReferenceId     = inv.InvoiceNumber,
                            PerformedBy     = updatedBy,
                        });
                    }
                }
            }
        }

        inv.Status    = newStatus;
        inv.UpdatedAt = DateTime.UtcNow;
        inv.UpdatedBy = updatedBy;
        await _db.SaveChangesAsync();
        var settings = await GetOrCreateSettingsAsync();
        return MapToDto(inv, settings);
    }

    // ── PDF ────────────────────────────────────────────────────────────────
    public async Task<(byte[] Pdf, string FileName)?> GetPdfAsync(int id)
    {
        var inv = await LoadInvoiceAsync(x => x.Id == id);
        return inv == null ? null : await RenderAsync(inv);
    }

    public async Task<(byte[] Pdf, string FileName)?> GetPdfByOrderAsync(int orderId)
    {
        var inv = await LoadInvoiceAsync(x => x.OrderId == orderId);
        return inv == null ? null : await RenderAsync(inv);
    }

    private async Task<(byte[] Pdf, string FileName)> RenderAsync(Domain.Entities.Invoice inv, InvoiceSettings? settings = null)
    {
        settings ??= await GetOrCreateSettingsAsync();
        var site = await _db.SiteSettings.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Id).FirstOrDefaultAsync();
        var model = new InvoicePdfModel
        {
            Invoice = inv,
            Settings = settings,
            OrderNumber = inv.Order?.OrderNumber,
            OrderDateUtc = inv.Order?.CreatedAt,
            FallbackUdyam = site?.UdyamNumber,
            FallbackFssai = site?.Fssai,
        };
        var pdf = await Task.Run(() => InvoicePdfRenderer.Render(model));
        return (pdf, InvoicePdfRenderer.FileNameFor(inv.InvoiceNumber));
    }

    public async Task<byte[]> PreviewPdfAsync(UpdateInvoiceSettingsRequest req)
    {
        ValidateTemplate(req);
        var current = await GetOrCreateSettingsAsync();
        var s = new InvoiceSettings();
        ApplySettings(s, req with { CompanyName = string.IsNullOrWhiteSpace(req.CompanyName) ? current.CompanyName : req.CompanyName },
            GstStates.NameOf(req.StateCode) ?? GstStates.NameOf(req.State) ?? req.State?.Trim() ?? "");
        s.InvoicePrefix = string.IsNullOrWhiteSpace(req.InvoicePrefix) ? "WAH" : req.InvoicePrefix.Trim();
        var now = DateTime.UtcNow;
        var intra = true;
        var sample = new Domain.Entities.Invoice
        {
            InvoiceNumber = $"{s.InvoicePrefix}/{Period(s.NumberFormat, now)}/001",
            InvoiceDate = now, CreatedAt = now,
            CustomerName = "Sample Customer", CustomerPhone = "+91 98765 43210", CustomerEmail = "customer@example.com",
            BillingAddress = JsonSerializer.Serialize(new { name = "Sample Customer", phone = "+91 98765 43210", addressLine = "12, Sample Society, Ring Road", city = s.City, state = s.State, pincode = s.Pincode }),
            Type = InvoiceType.B2C, Status = InvoiceStatus.Unpaid, GstType = GSTType.IntraState,
            PlaceOfSupply = s.State, PaymentMethod = "Cash on Delivery", DeliveryCharge = 40, Source = "Online",
        };
        foreach (var (name, hsn, qty, mrp, price) in new[] { ("Garam Masala (100g)", "09109100", 2, 110m, 99m), ("Haldi Powder (200g)", "09103030", 1, 90m, 79m), ("Kitchen King Masala (50g)", "09109100", 3, 55m, 49m) })
        {
            var taxable = Math.Round(price * qty / 1.05m, 2);
            var mrpTaxable = Math.Round(mrp * qty / 1.05m, 2);
            var half = Math.Round(taxable * 0.025m, 2);
            sample.LineItems.Add(new InvoiceLineItem
            {
                ProductName = name, HsnCode = hsn, Quantity = qty, Unit = "pcs", UnitPrice = price, MrpPrice = mrp,
                DiscountAmount = mrpTaxable - taxable, TaxableAmount = taxable, GstRate = 5, CgstRate = 2.5m, SgstRate = 2.5m,
                CgstAmount = intra ? half : 0, SgstAmount = intra ? half : 0, TaxAmount = half * 2, LineTotal = taxable + half * 2,
            });
        }
        sample.Subtotal = sample.LineItems.Sum(l => l.TaxableAmount);
        sample.DiscountAmount = sample.LineItems.Sum(l => l.DiscountAmount);
        sample.CgstAmount = sample.LineItems.Sum(l => l.CgstAmount);
        sample.SgstAmount = sample.LineItems.Sum(l => l.SgstAmount);
        var exact = sample.Subtotal + sample.CgstAmount + sample.SgstAmount + sample.DeliveryCharge;
        sample.RoundOff = Math.Round(Math.Round(exact) - exact, 2);
        sample.TotalAmount = exact + sample.RoundOff;
        if (s.EInvoiceEnabled)
        {
            sample.Irn = "SAMPLE0000000000000000000000000000000000000000000000000000000000";
            sample.IrnAckNo = "112610000000001"; sample.IrnAckDate = now; sample.SignedQrCode = "SAMPLE-SIGNED-QR";
        }
        var site = await _db.SiteSettings.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Id).FirstOrDefaultAsync();
        return await Task.Run(() => InvoicePdfRenderer.Render(new InvoicePdfModel
        {
            Invoice = sample, Settings = s, OrderNumber = "WAH" + now.ToString("yyyyMMdd") + "001", OrderDateUtc = now,
            FallbackUdyam = site?.UdyamNumber, FallbackFssai = site?.Fssai, IsSample = true,
        }));
    }

    // ── e-Invoice ──────────────────────────────────────────────────────────
    public async Task<InvoiceDto> UpdateEInvoiceAsync(int id, UpdateEInvoiceRequest r, string updatedBy)
    {
        var inv = await LoadInvoiceAsync(x => x.Id == id) ?? throw AppException.NotFound("Invoice");
        var irn = r.Irn?.Trim();
        if (!string.IsNullOrEmpty(irn) && !System.Text.RegularExpressions.Regex.IsMatch(irn, "^[0-9a-fA-F]{64}$"))
            throw new AppException("IRN must be the 64-character code returned by the e-invoice portal.", 400, "VALIDATION_ERROR", "irn");
        if (r.AckNo?.Trim().Length > 30) throw new AppException("Ack No is too long.", 400, "VALIDATION_ERROR", "ackNo");
        if (r.SignedQrCode?.Length > 8000) throw new AppException("Signed QR code is too long.", 400, "VALIDATION_ERROR", "signedQrCode");
        inv.Irn = string.IsNullOrEmpty(irn) ? null : irn.ToLowerInvariant();
        inv.IrnAckNo = string.IsNullOrWhiteSpace(r.AckNo) ? null : r.AckNo.Trim();
        inv.IrnAckDate = r.AckDate.HasValue ? DateTime.SpecifyKind(r.AckDate.Value.ToUniversalTime(), DateTimeKind.Utc) : null;
        inv.SignedQrCode = string.IsNullOrWhiteSpace(r.SignedQrCode) ? null : r.SignedQrCode.Trim();
        inv.UpdatedAt = DateTime.UtcNow; inv.UpdatedBy = updatedBy;
        await _db.SaveChangesAsync();
        return MapToDto(inv, await GetOrCreateSettingsAsync());
    }

    public async Task<(string Json, string FileName)> GetEInvoiceJsonAsync(int id)
    {
        var inv = await LoadInvoiceAsync(x => x.Id == id) ?? throw AppException.NotFound("Invoice");
        var s = await GetOrCreateSettingsAsync();
        if (string.IsNullOrWhiteSpace(s.Gstin))
            throw new AppException("Add your GSTIN in Billing → Template first — e-invoices need the seller's GSTIN.", 400, "NO_SELLER_GSTIN");
        if (string.IsNullOrWhiteSpace(inv.CustomerGstin))
            throw new AppException("e-Invoices are only for B2B sales: this invoice has no customer GSTIN. B2C invoices don't need an IRN.", 400, "NOT_B2B");
        if (inv.Status == InvoiceStatus.Cancelled)
            throw new AppException("This invoice is cancelled.", 400, "INVOICE_CANCELLED");

        static string Clip(string? v, int max) { v = (v ?? "").Trim(); return v.Length > max ? v[..max] : v; }
        static int? Pin(string? text) { var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\b[1-9][0-9]{5}\b"); return m.Success ? int.Parse(m.Value) : null; }
        static string Uqc(string unit) => unit.Trim().ToLowerInvariant() switch
        {
            "pcs" or "pc" or "piece" or "pieces" => "PCS", "kg" or "kgs" => "KGS", "g" or "gm" or "gms" or "gram" => "GMS",
            "box" => "BOX", "pack" or "pkt" or "packet" => "PAC", "nos" => "NOS", "bag" => "BAG", "l" or "ltr" => "LTR", _ => "OTH",
        };
        string? addrLine = null, city = null; int? buyerPin = null;
        try
        {
            if (inv.BillingAddress?.TrimStart().StartsWith('{') == true)
            {
                using var d = JsonDocument.Parse(inv.BillingAddress);
                var root = d.RootElement;
                addrLine = root.TryGetProperty("addressLine", out var a) ? a.GetString() : null;
                city = root.TryGetProperty("city", out var c) ? c.GetString() : null;
                buyerPin = root.TryGetProperty("pincode", out var p) ? Pin(p.GetString()) : null;
            }
        }
        catch { /* plain text */ }
        addrLine ??= inv.BillingAddress;
        buyerPin ??= Pin(inv.BillingAddress);
        var buyerState = GstStates.CodeOf(inv.PlaceOfSupply) ?? inv.CustomerGstin![..2];
        if (buyerPin == null)
            throw new AppException("Add the customer's 6-digit PIN code in the billing address — the e-invoice schema requires it.", 400, "NO_BUYER_PIN");

        var lines = inv.LineItems.Where(l => !l.IsDeleted).ToList();
        var payload = new
        {
            Version = "1.1",
            TranDtls = new { TaxSch = "GST", SupTyp = "B2B", RegRev = "N", IgstOnIntra = "N" },
            DocDtls = new { Typ = "INV", No = inv.InvoiceNumber, Dt = (inv.InvoiceDate ?? inv.CreatedAt).AddHours(5.5).ToString("dd/MM/yyyy") },
            SellerDtls = new
            {
                Gstin = s.Gstin!.Trim().ToUpperInvariant(), LglNm = Clip(s.CompanyName, 100), Addr1 = Clip(s.Address, 100),
                Loc = Clip(s.City, 50), Pin = Pin(s.Pincode) ?? 0, Stcd = GstStates.CodeOf(s.State) ?? s.Gstin!.Trim()[..2],
                Ph = new string((s.Phone ?? "").Where(char.IsDigit).ToArray()) is { Length: >= 6 } ph ? ph[^Math.Min(12, ph.Length)..] : null,
                Em = string.IsNullOrWhiteSpace(s.Email) ? null : s.Email,
            },
            BuyerDtls = new
            {
                Gstin = inv.CustomerGstin!.Trim().ToUpperInvariant(), LglNm = Clip(inv.CustomerName, 100), Pos = buyerState,
                Addr1 = Clip(addrLine, 100), Loc = Clip(city ?? inv.PlaceOfSupply, 50), Pin = buyerPin, Stcd = buyerState,
                Ph = new string((inv.CustomerPhone ?? "").Where(char.IsDigit).ToArray()) is { Length: >= 6 } bph ? bph[^Math.Min(12, bph.Length)..] : null,
                Em = string.IsNullOrWhiteSpace(inv.CustomerEmail) ? null : inv.CustomerEmail,
            },
            ItemList = lines.Select((l, i) => new
            {
                SlNo = (i + 1).ToString(), PrdDesc = Clip(l.ProductName, 300), IsServc = "N", HsnCd = l.HsnCode ?? "",
                Qty = l.Quantity, Unit = Uqc(l.Unit),
                UnitPrice = l.Quantity > 0 ? Math.Round(l.TaxableAmount / l.Quantity, 3) : 0,
                TotAmt = l.TaxableAmount, Discount = 0m, AssAmt = l.TaxableAmount, GstRt = l.GstRate,
                IgstAmt = l.IgstAmount, CgstAmt = l.CgstAmount, SgstAmt = l.SgstAmount, TotItemVal = l.LineTotal,
            }).ToList(),
            ValDtls = new
            {
                AssVal = inv.Subtotal, CgstVal = inv.CgstAmount, SgstVal = inv.SgstAmount, IgstVal = inv.IgstAmount,
                Discount = 0m, OthChrg = inv.DeliveryCharge, RndOffAmt = inv.RoundOff, TotInvVal = inv.TotalAmount,
            },
        };
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.HsnCode)))
            throw new AppException("Every item needs an HSN code for an e-invoice.", 400, "NO_HSN");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        return (json, "eInvoice-" + string.Concat(inv.InvoiceNumber.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')) + ".json");
    }

    // ── Order hooks ────────────────────────────────────────────────────────
    public async Task<int?> TryAutoGenerateAsync(int orderId)
    {
        try
        {
            var settings = await GetOrCreateSettingsAsync();
            var existing = await _db.Invoices.Where(i => i.OrderId == orderId && !i.IsDeleted).Select(i => (int?)i.Id).FirstOrDefaultAsync();
            if (existing != null) { await SyncWithOrderAsync(orderId, "System"); return existing; }
            if (!settings.AutoGenerateOnOrder) return null;
            var dto = await GenerateFromOrderAsync(orderId);
            return dto.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic invoice for order {OrderId} failed", orderId);
            return null;
        }
    }

    public async Task SyncWithOrderAsync(int orderId, string updatedBy)
    {
        try
        {
            var inv = await _db.Invoices.Include(i => i.Order).FirstOrDefaultAsync(i => i.OrderId == orderId && !i.IsDeleted);
            if (inv?.Order == null || inv.Status == InvoiceStatus.Cancelled) return;
            var o = inv.Order;
            InvoiceStatus? next = null;
            if (o.Status is OrderStatus.Cancelled) next = InvoiceStatus.Cancelled;
            else if (o.PaymentStatus == PaymentStatus.Paid && inv.Status != InvoiceStatus.Paid) next = InvoiceStatus.Paid;
            if (next == null) return;
            inv.Status = next.Value;
            inv.UpdatedAt = DateTime.UtcNow;
            inv.UpdatedBy = updatedBy;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice status sync for order {OrderId} failed", orderId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// Next number {prefix}/{period}/{seq:D3}; period = Indian financial year "26-27" (NumberFormat FY) or "2026".
    /// Uses the highest existing sequence (including soft-deleted invoices, which still hold their number).
    private async Task<string> NextInvoiceNumberAsync(InvoiceSettings settings, DateTime whenUtc)
    {
        var stem = $"{settings.InvoicePrefix}/{Period(settings.NumberFormat, whenUtc)}/";
        var existing = await _db.Invoices.IgnoreQueryFilters()
            .Where(x => x.InvoiceNumber.StartsWith(stem))
            .Select(x => x.InvoiceNumber)
            .ToListAsync();
        var max = existing
            .Select(n => int.TryParse(n.Substring(stem.Length), out var k) ? k : 0)
            .DefaultIfEmpty(0).Max();
        return $"{stem}{max + 1:D3}";
    }

    private static string Period(string? format, DateTime whenUtc)
    {
        var ist = whenUtc.AddHours(5.5);
        if (string.Equals(format, "Year", StringComparison.OrdinalIgnoreCase)) return ist.Year.ToString();
        var start = ist.Month >= 4 ? ist.Year : ist.Year - 1;
        return $"{start % 100:D2}-{(start + 1) % 100:D2}";
    }

    private static void ValidateManual(CreateInvoiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.CustomerName))
            throw new AppException("Customer name is required.", 400, "VALIDATION_ERROR", "customerName");
        if (r.Items == null || r.Items.Count == 0)
            throw new AppException("Add at least one item.", 400, "VALIDATION_ERROR", "items");
        if (r.DeliveryCharge < 0)
            throw new AppException("Delivery charge cannot be negative.", 400, "VALIDATION_ERROR", "deliveryCharge");
        for (var i = 0; i < r.Items.Count; i++)
        {
            var it = r.Items[i];
            var row = $"Item {i + 1}";
            if (string.IsNullOrWhiteSpace(it.ProductName))
                throw new AppException($"{row}: product name is required.", 400, "VALIDATION_ERROR", "items");
            if (it.Quantity <= 0)
                throw new AppException($"{row} ({it.ProductName}): quantity must be greater than 0.", 400, "VALIDATION_ERROR", "items");
            if (it.UnitPrice < 0)
                throw new AppException($"{row} ({it.ProductName}): rate cannot be negative.", 400, "VALIDATION_ERROR", "items");
            if (it.DiscountPercent < 0 || it.DiscountPercent > 100)
                throw new AppException($"{row} ({it.ProductName}): discount must be between 0 and 100%.", 400, "VALIDATION_ERROR", "items");
            if (it.GstRate < 0 || it.GstRate > 100)
                throw new AppException($"{row} ({it.ProductName}): invalid GST rate.", 400, "VALIDATION_ERROR", "items");
        }
        if (!Enum.TryParse<InvoiceType>(r.Type, true, out var t) || !Enum.IsDefined(t) || int.TryParse(r.Type, out _))
            throw new AppException($"Invalid invoice type '{r.Type}'. Allowed: {string.Join(", ", Enum.GetNames<InvoiceType>())}.", 400, "VALIDATION_ERROR", "type");
        if (!string.IsNullOrWhiteSpace(r.PlaceOfSupply) && GstStates.NameOf(r.PlaceOfSupply.Trim()) == null)
            throw new AppException($"'{r.PlaceOfSupply}' is not an Indian state / UT (or GST state code).", 400, "VALIDATION_ERROR", "placeOfSupply");
        if (!string.IsNullOrWhiteSpace(r.Status) && !new[] { "Paid", "Unpaid" }.Contains(r.Status.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new AppException("Status must be Paid or Unpaid.", 400, "VALIDATION_ERROR", "status");
        if (r.PaymentMethod?.Trim().Length > 40)
            throw new AppException("Payment method is too long.", 400, "VALIDATION_ERROR", "paymentMethod");
        if (r.InvoiceDate.HasValue && (r.InvoiceDate.Value.Date > DateTime.UtcNow.AddHours(5.5).Date || r.InvoiceDate.Value.Date < DateTime.UtcNow.AddYears(-2)))
            throw new AppException("Invoice date can't be in the future or more than 2 years old.", 400, "VALIDATION_ERROR", "invoiceDate");
        if (!string.IsNullOrWhiteSpace(r.CustomerEmail) && !System.Text.RegularExpressions.Regex.IsMatch(r.CustomerEmail.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new AppException("Customer email is not valid.", 400, "VALIDATION_ERROR", "customerEmail");
        if (!string.IsNullOrWhiteSpace(r.CustomerGstin) && !System.Text.RegularExpressions.Regex.IsMatch(r.CustomerGstin.Trim().ToUpperInvariant(), "^[0-9]{2}[A-Z0-9]{10}[0-9A-Z]Z[0-9A-Z]$"))
            throw new AppException("Customer GSTIN is not valid (15 characters, e.g. 24ABCDE1234F1Z5).", 400, "VALIDATION_ERROR", "customerGstin");
        if (r.SendEmail && string.IsNullOrWhiteSpace(r.CustomerEmail))
            throw new AppException("Enter the customer's email to send the invoice.", 400, "VALIDATION_ERROR", "customerEmail");
        GSTType g = GSTType.IntraState;
        if (!string.IsNullOrWhiteSpace(r.GstType) && (!Enum.TryParse<GSTType>(r.GstType, true, out g) || !Enum.IsDefined(g) || int.TryParse(r.GstType, out _)))
            throw new AppException($"Invalid GST type '{r.GstType}'. Allowed: {string.Join(", ", Enum.GetNames<GSTType>())}.", 400, "VALIDATION_ERROR", "gstType");
        if (t == InvoiceType.B2B && string.IsNullOrWhiteSpace(r.CustomerGstin))
            throw new AppException("Customer GSTIN is required for a B2B invoice.", 400, "VALIDATION_ERROR", "customerGstin");
    }

    private async Task<Domain.Entities.Invoice?> LoadInvoiceAsync(
        System.Linq.Expressions.Expression<Func<Domain.Entities.Invoice, bool>> predicate)
        => await _db.Invoices
            .Include(x => x.LineItems)
            .Include(x => x.Order)
            .Where(x => !x.IsDeleted)
            .Where(predicate)
            .FirstOrDefaultAsync();

    private async Task<InvoiceSettings> GetOrCreateSettingsAsync()
    {
        var s = await _db.InvoiceSettings.FirstOrDefaultAsync(x => !x.IsDeleted);
        if (s != null) return s;

        var site = await _db.SiteSettings.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Id).FirstOrDefaultAsync();
        s = new InvoiceSettings
        {
            CompanyName = string.IsNullOrWhiteSpace(site?.SiteName) ? "WAH! Spices" : site!.SiteName!,
            Address = site?.Address ?? "",
            City = "Surat",
            State = "Gujarat",
            Pincode = "394101",
            Phone = site?.Phone ?? "",
            Email = site?.Email ?? "",
            Gstin = site?.Gstin,
            Fssai = site?.Fssai,
            UdyamNumber = site?.UdyamNumber,
            Declaration = "We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct.",
            PrimaryColor = "#1a5c2e",
            InvoicePrefix = "WAH",
            TermsAndConditions = "1. Goods once sold will not be taken back.\n2. All disputes subject to local jurisdiction.",
            FooterText = $"Thank you for shopping with us! For any queries write to {site?.Email ?? "us"}.",
            CreatedBy = "System"
        };

        _db.InvoiceSettings.Add(s);
        await _db.SaveChangesAsync();
        return s;
    }

    private static string SerializeAddress(Address a)
        => JsonSerializer.Serialize(new
        {
            name = a.Name,
            phone = a.Phone,
            addressLine = a.AddressLine,
            city = a.City,
            state = a.State,
            pincode = a.Pincode,
            landmark = a.Landmark
        });

    private static InvoiceDto MapToDto(Domain.Entities.Invoice inv, InvoiceSettings s)
        => new(
            inv.Id, inv.InvoiceNumber, inv.CreatedAt,
            inv.OrderId, inv.Order?.OrderNumber,
            inv.CustomerName, inv.CustomerEmail, inv.CustomerPhone, inv.CustomerGstin,
            inv.BillingAddress, inv.ShippingAddress,
            inv.Type, inv.Status, inv.GstType,
            inv.Subtotal, inv.DiscountAmount, inv.DeliveryCharge,
            inv.CgstAmount, inv.SgstAmount, inv.IgstAmount, inv.TotalAmount,
            inv.Irn, inv.Notes,
            inv.LineItems.Where(li => !li.IsDeleted).Select(li => new InvoiceLineItemDto(
                li.Id, li.ProductName, li.HsnCode, li.Quantity, li.Unit,
                li.UnitPrice, li.MrpPrice, li.DiscountPercent, li.DiscountAmount,
                li.TaxableAmount, li.GstRate, li.CgstRate, li.SgstRate, li.IgstRate,
                li.CgstAmount, li.SgstAmount, li.IgstAmount, li.TaxAmount, li.LineTotal)
            ).ToList(),
            MapSettings(s),
            inv.InvoiceDate ?? inv.CreatedAt, inv.PaymentMethod, inv.PlaceOfSupply, inv.CouponDiscount, inv.RoundOff,
            inv.IrnAckNo, inv.IrnAckDate, inv.SignedQrCode, inv.Source, inv.LastSentAt, inv.LastSentTo, inv.SentCount);

    private static InvoiceSettingsDto MapSettings(InvoiceSettings s)
        => new(s.Id, s.CompanyName, s.CompanyLogo, s.Address, s.City, s.State, s.Pincode,
            s.Phone, s.Email, s.Website, s.Gstin, s.Pan, s.Fssai,
            s.BankName, s.BankAccount, s.BankIfsc, s.BankBranch,
            s.PrimaryColor, s.InvoicePrefix,
            s.TermsAndConditions, s.FooterText,
            s.ShowSignature, s.ShowStamp, s.ShowHsnColumn, s.ShowDiscountColumn,
            GstStates.CodeOf(s.State), GstStates.CodeOf(s.State) != null ? GstStates.Format(s.State) : null,
            s.InvoiceTitle, s.UdyamNumber, s.UpiId, s.SignatureImage, s.StampImage, s.AuthorisedSignatory, s.Declaration,
            s.NumberFormat, s.ShowBankDetails, s.ShowUpiQr, s.ShowMrpColumn, s.ShowAmountInWords, s.ShowHsnSummary,
            s.AutoGenerateOnOrder, s.AttachPdfToEmails, s.EInvoiceEnabled);

    // ── Build HTML for email ──────────────────────────────────────────────
    /// <summary>Invoice content for the branded email template (same fields the old inline HTML showed).</summary>
    private static InvoiceEmailModel BuildInvoiceEmailModel(Domain.Entities.Invoice inv, InvoiceSettings s, string? message = null, bool pdfAttached = false) => new()
    {
        Message = string.IsNullOrWhiteSpace(message) ? null : message,
        PdfAttached = pdfAttached,
        RoundOff = inv.RoundOff,
        InvoiceNumber = inv.InvoiceNumber,
        DateUtc = inv.InvoiceDate ?? inv.CreatedAt,
        Status = inv.Status.ToString(),
        CompanyName = s.CompanyName,
        CompanyAddress = string.Join(", ", new[] { s.Address, s.City, s.State }.Where(x => !string.IsNullOrWhiteSpace(x)))
                         + (string.IsNullOrWhiteSpace(s.Pincode) ? "" : $" - {s.Pincode}"),
        CompanyGstin = s.Gstin,
        StateCodeLine = GstStates.CodeOf(s.State) is { } stCode ? $"State code: {stCode} ({s.State})" : null,
        CompanyContact = string.IsNullOrEmpty(s.Phone) ? null : $"{s.Phone} | {s.Email}",
        CustomerName = inv.CustomerName,
        CustomerEmail = inv.CustomerEmail,
        CustomerPhone = inv.CustomerPhone,
        Lines = inv.LineItems.Where(l => !l.IsDeleted)
            .Select(l => new InvoiceEmailLine(l.ProductName, l.HsnCode, l.Quantity, l.UnitPrice, l.TaxableAmount, l.GstRate, l.LineTotal)).ToList(),
        Subtotal = inv.Subtotal,
        Discount = inv.DiscountAmount,
        Cgst = inv.CgstAmount,
        Sgst = inv.SgstAmount,
        Igst = inv.IgstAmount,
        Delivery = inv.DeliveryCharge,
        Total = inv.TotalAmount,
        BankLine = string.IsNullOrEmpty(s.BankAccount) || !s.ShowBankDetails ? null : $"{s.BankName} | A/C: {s.BankAccount} | IFSC: {s.BankIfsc}",
        Terms = s.TermsAndConditions,
        FooterText = s.FooterText,
    };
}
