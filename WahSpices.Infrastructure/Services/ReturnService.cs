using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Return;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.Infrastructure.Services;

public class ReturnService : IReturnService
{
    private const string DefaultSupplierState = "Gujarat"; // fallback when Billing → Template has no state

    /// Registered state of the business (Billing → Template settings) — decides CGST/SGST vs IGST.
    private async Task<string> SupplierStateAsync()
    {
        var st = await _db.InvoiceSettings.Where(s => !s.IsDeleted).Select(s => s.State).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(st) ? DefaultSupplierState : st;
    }

    private readonly AppDbContext _db;
    private readonly ITaxCalculationService _tax;
    private readonly IOrderEmails _emails;

    public ReturnService(AppDbContext db, ITaxCalculationService tax, IOrderEmails emails)
    {
        _db = db;
        _tax = tax;
        _emails = emails;
    }

    private void History(int orderId, OrderStatus status, string note, string by) =>
        _db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = orderId, Status = status, Note = note, UpdatedBy = by });

    private static List<string> NextActionsFor(ReturnStatus s) => s switch
    {
        ReturnStatus.Requested => new() { "approve", "reject" },
        ReturnStatus.Approved or ReturnStatus.PickupScheduled => new() { "pickup", "received", "reject" },
        ReturnStatus.PickedUp => new() { "received" },
        ReturnStatus.Received or ReturnStatus.QualityCheck or ReturnStatus.Processed => new() { "refund" },
        _ => new()
    };

    // ── Admin: KPI tiles ──────────────────────────────────────────────────────
    public async Task<ApiResponse<ReturnStatsDto>> GetStatsAsync()
    {
        var rows = await _db.ReturnOrders.Where(r => !r.IsDeleted)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(x => x.GrandRefundAmount) })
            .ToListAsync();
        int c(params ReturnStatus[] st) => rows.Where(r => st.Contains(r.Status)).Sum(r => r.Count);
        decimal a(params ReturnStatus[] st) => rows.Where(r => st.Contains(r.Status)).Sum(r => r.Amount);
        var awaitingRefund = new[] { ReturnStatus.Received, ReturnStatus.QualityCheck, ReturnStatus.Processed };
        var open = new[] { ReturnStatus.Requested, ReturnStatus.Approved, ReturnStatus.PickupScheduled, ReturnStatus.PickedUp }.Concat(awaitingRefund).ToArray();
        return ApiResponse<ReturnStatsDto>.Ok(new ReturnStatsDto
        {
            Total = rows.Sum(r => r.Count),
            ByStatus = rows.ToDictionary(r => r.Status.ToString(), r => r.Count),
            Open = c(open),
            AwaitingAction = c(ReturnStatus.Requested),
            AwaitingRefund = c(awaitingRefund),
            RefundedAmount = a(ReturnStatus.Refunded),
            PendingRefundAmount = a(open),
        });
    }

    // ── Customer: initiate return ─────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> InitiateReturnAsync(int userId, InitiateReturnRequest request, IReadOnlyList<string>? photoPaths = null)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Address)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == userId);

        if (order == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Order not found");

        if (order.Status != OrderStatus.Delivered)
            return ApiResponse<ReturnDto>.Fail("INVALID_STATUS", "Only delivered orders can be returned");

        var existingReturn = await _db.ReturnOrders
            .AnyAsync(r => r.OrderId == request.OrderId && r.Status != ReturnStatus.Cancelled && r.Status != ReturnStatus.Rejected);

        if (existingReturn)
            return ApiResponse<ReturnDto>.Fail("DUPLICATE", "A return already exists for this order");

        if (request.Items == null || !request.Items.Any())
            return ApiResponse<ReturnDto>.Fail("VALIDATION", "At least one item is required");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return ApiResponse<ReturnDto>.Fail("VALIDATION", "Please tell us why you're returning this order");
        request.Reason = request.Reason.Trim()[..Math.Min(500, request.Reason.Trim().Length)];
        if (request.Items.GroupBy(i => i.OrderItemId).Any(g => g.Count() > 1))
            return ApiResponse<ReturnDto>.Fail("VALIDATION", "Each order item can only be listed once");

        // Validate requested items exist in order
        foreach (var reqItem in request.Items)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.Id == reqItem.OrderItemId);
            if (orderItem == null)
                return ApiResponse<ReturnDto>.Fail("INVALID_ITEM", $"Order item {reqItem.OrderItemId} not found");

            if (reqItem.Quantity <= 0 || reqItem.Quantity > orderItem.Quantity)
                return ApiResponse<ReturnDto>.Fail("INVALID_QTY", $"Invalid quantity for item {orderItem.ProductName}");
        }

        var returnNumber = await GenerateReturnNumberAsync();
        var gstType = _tax.DetermineGstType(await SupplierStateAsync(), order.Address.State);

        var returnOrder = new ReturnOrder
        {
            ReturnNumber = returnNumber,
            OrderId = request.OrderId,
            UserId = userId,
            Status = ReturnStatus.Requested,
            Type = request.Type,
            Reason = request.Reason,
            GstType = gstType,
            PriceType = TaxPriceType.Inclusive,
            RefundMethod = request.PreferredRefundMethod,
            PickupAddress = $"{order.Address.AddressLine}, {order.Address.City}, {order.Address.State} - {order.Address.Pincode}",
            PhotoUrls = photoPaths is { Count: > 0 } ? JsonSerializer.Serialize(photoPaths.Take(5)) : null,
        };

        var taxInputs = new List<ReturnItemTaxInput>();

        foreach (var reqItem in request.Items)
        {
            var orderItem = order.Items.First(i => i.Id == reqItem.OrderItemId);
            decimal lineTotal = _tax.Round(orderItem.Price * reqItem.Quantity);

            var gst = _tax.CalculateGst(lineTotal, orderItem.Product.GstRate, gstType, TaxPriceType.Inclusive);

            var returnItem = new ReturnItem
            {
                OrderItemId = reqItem.OrderItemId,
                ProductId = orderItem.ProductId,
                ProductName = orderItem.ProductName,
                Weight = orderItem.Weight,
                HsnCode = orderItem.Product.HsnCode ?? string.Empty,
                Quantity = reqItem.Quantity,
                UnitPrice = orderItem.Price,
                UnitMrp = orderItem.Mrp,
                LineTotal = lineTotal,
                GstRate = orderItem.Product.GstRate,
                TaxableValue = gst.TaxableValue,
                CgstRate = gst.CgstRate,
                SgstRate = gst.SgstRate,
                IgstRate = gst.IgstRate,
                CgstAmount = gst.CgstAmount,
                SgstAmount = gst.SgstAmount,
                IgstAmount = gst.IgstAmount,
                TotalTax = gst.TotalGst,
                LineTotalWithTax = lineTotal, // price is inclusive already
                ReturnReason = reqItem.Reason,
                Condition = reqItem.Condition
            };

            returnOrder.Items.Add(returnItem);
            taxInputs.Add(new ReturnItemTaxInput
            {
                ProductName = orderItem.ProductName,
                HsnCode = orderItem.Product.HsnCode ?? string.Empty,
                Quantity = reqItem.Quantity,
                UnitPrice = orderItem.Price,
                UnitMrp = orderItem.Mrp,
                GstRate = orderItem.Product.GstRate
            });
        }

        // Auto-calculate totals (+ delivery charge back on a full return)
        ApplyTaxSummary(returnOrder, taxInputs, gstType);
        ApplyDeliveryRule(returnOrder, order);

        _db.ReturnOrders.Add(returnOrder);

        // Update order status
        order.Status = OrderStatus.ReturnRequested;
        order.ReturnReason = request.Reason;
        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.ReturnRequested,
            Note = $"Return #{returnNumber} initiated. Reason: {request.Reason}",
            UpdatedBy = userId.ToString()
        });

        await _db.SaveChangesAsync();
        _emails.ReturnUpdate(returnOrder.Id, ReturnEmailKind.Requested);

        return ApiResponse<ReturnDto>.Ok(MapToDto(returnOrder, order.OrderNumber, "/returns"), "Return request submitted successfully");
    }

    // ── Customer: get return ──────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> GetReturnAsync(int userId, int returnId)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == returnId && r.UserId == userId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");

        var dto = MapToDto(ret, ret.Order.OrderNumber, "/returns");
        dto.NextActions = new();   // admin-only
        return ApiResponse<ReturnDto>.Ok(dto);
    }

    public async Task<ApiResponse<PagedResult<ReturnSummaryDto>>> GetUserReturnsAsync(int userId, int page, int limit)
    {
        var query = _db.ReturnOrders
            .Include(r => r.Order)
            .Include(r => r.Items)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * limit).Take(limit).ToListAsync();

        return ApiResponse<PagedResult<ReturnSummaryDto>>.Ok(new PagedResult<ReturnSummaryDto>
        {
            Items = items.Select(r => MapToSummary(r, r.Order.OrderNumber)),
            Total = total,
            Page = page,
            Limit = limit
        });
    }

    public async Task<ApiResponse<bool>> CancelReturnAsync(int userId, int returnId)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Id == returnId && r.UserId == userId);

        if (ret == null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Return not found");

        if (ret.Status > ReturnStatus.Approved)
            return ApiResponse<bool>.Fail("INVALID_STATUS", "Return cannot be cancelled at this stage");

        ret.Status = ReturnStatus.Cancelled;
        ret.Order.Status = OrderStatus.Delivered;
        History(ret.OrderId, OrderStatus.Delivered, $"Return #{ret.ReturnNumber} cancelled by customer", userId.ToString());

        await _db.SaveChangesAsync();
        return ApiResponse<bool>.Ok(true, "Return cancelled");
    }

    // ── Admin: list / detail ──────────────────────────────────────────────────

    public async Task<ApiResponse<PagedResult<ReturnSummaryDto>>> GetAllReturnsAsync(ReturnFilterRequest filter)
    {
        var query = _db.ReturnOrders
            .Include(r => r.Order)
            .Include(r => r.User)
            .Include(r => r.Items)
            .Where(r => !r.IsDeleted)
            .AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(r => r.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string s = filter.Search.Trim().ToLower();
            query = query.Where(r => r.ReturnNumber.ToLower().Contains(s)
                || r.Order.OrderNumber.ToLower().Contains(s)
                || r.User.Name.ToLower().Contains(s)
                || r.User.Email.ToLower().Contains(s)
                || r.User.Phone.Contains(s));
        }

        if (filter.FromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(r => r.CreatedAt <= filter.ToDate.Value.AddDays(1));

        query = query.OrderByDescending(r => r.CreatedAt);

        filter.Page = Math.Max(1, filter.Page);
        filter.Limit = Math.Clamp(filter.Limit, 1, 200);
        var total = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.Limit).Take(filter.Limit).ToListAsync();

        return ApiResponse<PagedResult<ReturnSummaryDto>>.Ok(new PagedResult<ReturnSummaryDto>
        {
            Items = items.Select(r => MapToSummary(r, r.Order.OrderNumber)),
            Total = total,
            Page = filter.Page,
            Limit = filter.Limit
        });
    }

    public async Task<ApiResponse<ReturnDto>> GetReturnDetailAsync(int returnId)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == returnId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");

        return ApiResponse<ReturnDto>.Ok(MapToDto(ret, ret.Order.OrderNumber));
    }

    // ── Admin: approve ────────────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> ApproveReturnAsync(int returnId, ApproveReturnRequest request)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Order).ThenInclude(o => o.Address)
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(r => r.Id == returnId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");

        if (ret.Status != ReturnStatus.Requested)
            return ApiResponse<ReturnDto>.Fail("INVALID_STATUS", "Only Requested returns can be approved");

        // Use override if provided, else auto-detect
        if (request.GstTypeOverride.HasValue)
            ret.GstType = request.GstTypeOverride.Value;

        ret.Status = ReturnStatus.Approved;
        ret.AdminNotes = request.AdminNotes;
        ret.PickupScheduledAt = request.PickupScheduledAt;

        if (!string.IsNullOrWhiteSpace(request.PickupAddress))
            ret.PickupAddress = request.PickupAddress;

        // Recalculate tax now that GST type is confirmed
        var taxInputs = ret.Items.Select(i => new ReturnItemTaxInput
        {
            ProductName = i.ProductName,
            HsnCode = i.HsnCode,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            UnitMrp = i.UnitMrp,
            GstRate = i.GstRate
        }).ToList();

        ApplyTaxSummary(ret, taxInputs, ret.GstType, request.IncludeTcs, request.TcsRate);
        ApplyDeliveryRule(ret, ret.Order);

        // Re-calculate per-item tax with confirmed GstType
        foreach (var item in ret.Items)
        {
            var gst = _tax.CalculateGst(item.LineTotal, item.GstRate, ret.GstType, ret.PriceType);
            item.TaxableValue = gst.TaxableValue;
            item.CgstRate = gst.CgstRate;
            item.SgstRate = gst.SgstRate;
            item.IgstRate = gst.IgstRate;
            item.CgstAmount = gst.CgstAmount;
            item.SgstAmount = gst.SgstAmount;
            item.IgstAmount = gst.IgstAmount;
            item.TotalTax = gst.TotalGst;
        }

        History(ret.OrderId, OrderStatus.ReturnRequested,
            $"Return #{ret.ReturnNumber} approved" + (ret.PickupScheduledAt.HasValue ? $" — pickup scheduled for {ret.PickupScheduledAt:dd MMM yyyy}" : " — we'll arrange a pickup"), "Admin");

        await _db.SaveChangesAsync();
        _emails.ReturnUpdate(ret.Id, ReturnEmailKind.Approved);

        return ApiResponse<ReturnDto>.Ok(MapToDto(ret, ret.Order.OrderNumber), "Return approved. Tax calculated automatically.");
    }

    // ── Admin: reject ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> RejectReturnAsync(int returnId, string reason)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == returnId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");
        if (ret.Status is not (ReturnStatus.Requested or ReturnStatus.Approved or ReturnStatus.PickupScheduled))
            return ApiResponse<ReturnDto>.Fail("INVALID_STATUS", $"A return that is {ret.Status} can't be rejected");
        if (string.IsNullOrWhiteSpace(reason))
            return ApiResponse<ReturnDto>.Fail("VALIDATION_ERROR", "Rejection reason is required");

        ret.Status = ReturnStatus.Rejected;
        ret.RejectionReason = reason.Trim();
        ret.Order.Status = OrderStatus.Delivered;
        History(ret.OrderId, OrderStatus.Delivered, $"Return #{ret.ReturnNumber} rejected: {ret.RejectionReason}", "Admin");

        await _db.SaveChangesAsync();
        _emails.ReturnUpdate(ret.Id, ReturnEmailKind.Rejected);
        return ApiResponse<ReturnDto>.Ok(MapToDto(ret, ret.Order.OrderNumber), "Return rejected");
    }

    // ── Admin: mark picked up ─────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> MarkPickedUpAsync(int returnId, string awbNumber, string courierName)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == returnId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");
        if (ret.Status is not (ReturnStatus.Approved or ReturnStatus.PickupScheduled))
            return ApiResponse<ReturnDto>.Fail("INVALID_STATUS", "Only approved returns can be marked as picked up");

        if (string.IsNullOrWhiteSpace(courierName))
            return ApiResponse<ReturnDto>.Fail("VALIDATION_ERROR", "Courier name is required to record a pickup");

        ret.Status = ReturnStatus.PickedUp;
        ret.AwbNumber = string.IsNullOrWhiteSpace(awbNumber) ? null : awbNumber.Trim();
        ret.CourierName = courierName.Trim();
        ret.PickedUpAt = DateTime.UtcNow;
        History(ret.OrderId, OrderStatus.ReturnRequested,
            $"Return #{ret.ReturnNumber} picked up by {ret.CourierName}" + (ret.AwbNumber != null ? $" (AWB {ret.AwbNumber})" : ""), "Admin");

        await _db.SaveChangesAsync();
        _emails.ReturnUpdate(ret.Id, ReturnEmailKind.PickedUp);
        return ApiResponse<ReturnDto>.Ok(MapToDto(ret, ret.Order.OrderNumber), "Pickup recorded");
    }

    // ── Admin: mark received ──────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> MarkReceivedAsync(int returnId, string? adminNotes)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Items)
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == returnId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");
        if (ret.Status is not (ReturnStatus.Approved or ReturnStatus.PickupScheduled or ReturnStatus.PickedUp))
            return ApiResponse<ReturnDto>.Fail("INVALID_STATUS", $"A return that is {ret.Status} can't be marked as received");

        ret.Status = ReturnStatus.Received;
        ret.ReceivedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(adminNotes))
            ret.AdminNotes = adminNotes;

        ret.Order.Status = OrderStatus.Returned;
        History(ret.OrderId, OrderStatus.Returned, $"Return #{ret.ReturnNumber} received at our warehouse — refund is being processed", "Admin");

        // Returned goods go back to stock (only if the order's stock was deducted; once per return)
        await StoreRules.RestoreAsync(_db, ret.Order.OrderNumber, ret.ReturnNumber,
            ret.Items.Where(i => i.IsApproved && !i.IsDeleted).Select(i => (i.ProductId, i.Quantity)),
            $"Return #{ret.ReturnNumber} (order #{ret.Order.OrderNumber}) received", "Admin");

        await _db.SaveChangesAsync();
        _emails.ReturnUpdate(ret.Id, ReturnEmailKind.Received);
        return ApiResponse<ReturnDto>.Ok(MapToDto(ret, ret.Order.OrderNumber), "Item received. Ready to process refund.");
    }

    // ── Admin: process refund ─────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnDto>> ProcessRefundAsync(int returnId, ProcessRefundRequest request)
    {
        var ret = await _db.ReturnOrders
            .Include(r => r.Items)
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == returnId);

        if (ret == null)
            return ApiResponse<ReturnDto>.Fail("NOT_FOUND", "Return not found");

        if (ret.Status != ReturnStatus.Received && ret.Status != ReturnStatus.QualityCheck && ret.Status != ReturnStatus.Processed)
            return ApiResponse<ReturnDto>.Fail("INVALID_STATUS", "Return must be received before processing refund");

        var creditNoteNumber = await GenerateCreditNoteNumberAsync();

        ret.Status = ReturnStatus.Refunded;
        ret.ProcessedAt = DateTime.UtcNow;
        ret.RefundMethod = request.RefundMethod;
        ret.RefundReferenceId = request.RefundReferenceId;
        ret.RefundInitiatedAt = DateTime.UtcNow;
        ret.RefundCompletedAt = DateTime.UtcNow;
        ret.CreditNoteNumber = creditNoteNumber;
        ret.CreditNoteDate = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Notes))
            ret.AdminNotes = (ret.AdminNotes ?? "") + $"\nRefund notes: {request.Notes}";

        // Payment status: full vs partial refund of what the customer paid
        var full = ret.GrandRefundAmount >= ret.Order.GrandTotal - 0.01m;
        ret.Order.Status = OrderStatus.Refunded;
        ret.Order.PaymentStatus = full ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        ret.Order.UpdatedAt = DateTime.UtcNow;

        // Online payments: record the refund against the gateway payment so Admin → Payments shows it
        var payment = await _db.Payments.Include(p => p.Refunds)
            .Where(p => p.OrderId == ret.OrderId && !p.IsDeleted && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded))
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
        if (payment != null && !payment.Refunds.Any(r => r.ReturnOrderId == ret.Id && !r.IsDeleted))
        {
            _db.Refunds.Add(new Refund
            {
                PaymentId = payment.Id, OrderId = ret.OrderId, ReturnOrderId = ret.Id,
                Amount = ret.GrandRefundAmount, Status = RefundStatus.Processed, Method = request.RefundMethod,
                Reason = $"Return #{ret.ReturnNumber}", Notes = request.RefundReferenceId ?? request.Notes,
                InitiatedBy = "Admin", ProcessedAt = DateTime.UtcNow, CreatedBy = "Admin",
            });
            var refundedSoFar = payment.Refunds.Where(r => !r.IsDeleted && r.Status != RefundStatus.Failed).Sum(r => r.Amount) + ret.GrandRefundAmount;
            payment.Status = refundedSoFar >= payment.Amount - 0.01m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        History(ret.OrderId, OrderStatus.Refunded,
            $"Refund of ₹{ret.GrandRefundAmount:N2} processed for return #{ret.ReturnNumber} ({request.RefundMethod})" +
            (string.IsNullOrWhiteSpace(request.RefundReferenceId) ? "" : $" · Ref {request.RefundReferenceId}"), "Admin");

        await _db.SaveChangesAsync();
        _emails.ReturnUpdate(ret.Id, ReturnEmailKind.Refunded);

        return ApiResponse<ReturnDto>.Ok(MapToDto(ret, ret.Order.OrderNumber), $"Refund of ₹{ret.GrandRefundAmount:N2} processed. Credit note: {creditNoteNumber}");
    }

    // ── Tax preview ───────────────────────────────────────────────────────────

    public async Task<ApiResponse<ReturnTaxPreviewDto>> PreviewReturnTaxAsync(int userId, int orderId, List<ReturnItemInput> items)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Address)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        if (order == null)
            return ApiResponse<ReturnTaxPreviewDto>.Fail("NOT_FOUND", "Order not found");

        var gstType = _tax.DetermineGstType(await SupplierStateAsync(), order.Address.State);
        var taxInputs = new List<ReturnItemTaxInput>();
        var previewItems = new List<ReturnItemPreviewDto>();

        foreach (var reqItem in items)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.Id == reqItem.OrderItemId);
            if (orderItem == null) continue;

            int qty = Math.Min(reqItem.Quantity, orderItem.Quantity);
            decimal lineTotal = _tax.Round(orderItem.Price * qty);
            var gst = _tax.CalculateGst(lineTotal, orderItem.Product.GstRate, gstType, TaxPriceType.Inclusive);

            taxInputs.Add(new ReturnItemTaxInput
            {
                ProductName = orderItem.ProductName,
                HsnCode = orderItem.Product.HsnCode ?? string.Empty,
                Quantity = qty,
                UnitPrice = orderItem.Price,
                UnitMrp = orderItem.Mrp,
                GstRate = orderItem.Product.GstRate
            });

            previewItems.Add(new ReturnItemPreviewDto
            {
                OrderItemId = orderItem.Id,
                ProductName = orderItem.ProductName,
                HsnCode = orderItem.Product.HsnCode ?? string.Empty,
                Quantity = qty,
                UnitPrice = orderItem.Price,
                LineTotal = lineTotal,
                GstRate = orderItem.Product.GstRate,
                TaxableValue = gst.TaxableValue,
                TotalTax = gst.TotalGst,
                RefundableAmount = lineTotal
            });
        }

        var summary = _tax.CalculateReturnTax(taxInputs, gstType);
        var full = IsFullReturn(order, previewItems.Select(p => (p.OrderItemId, p.Quantity)));
        if (full) summary.GrandRefundAmount = _tax.Round(summary.GrandRefundAmount + order.DeliveryCharge - order.CouponDiscount);
        var taxDto = BuildTaxDto(summary, null, null, Delivery(order, full));

        return ApiResponse<ReturnTaxPreviewDto>.Ok(new ReturnTaxPreviewDto
        {
            OrderNumber = order.OrderNumber,
            Items = previewItems,
            TaxSummary = taxDto,
            Message = $"Estimated refund: ₹{summary.GrandRefundAmount:N2} ({summary.GstTypeLabel})" +
                      (string.IsNullOrEmpty(taxDto.DeliveryNote) ? "" : $". {taxDto.DeliveryNote}")
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record DeliveryInfo(bool Full, decimal OrderDelivery, decimal DeliveryRefund, decimal CouponDeducted);

    /// True when every (non-deleted) line of the order is returned in full.
    private static bool IsFullReturn(Order? order, IEnumerable<(int OrderItemId, int Quantity)> lines)
    {
        var orderLines = order?.Items?.Where(i => !i.IsDeleted).ToList();
        if (orderLines == null || orderLines.Count == 0) return false;
        var qty = lines.GroupBy(l => l.OrderItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        return orderLines.All(l => qty.TryGetValue(l.Id, out var q) && q >= l.Quantity);
    }

    private static bool IsFullReturn(ReturnOrder ret) =>
        IsFullReturn(ret.Order, ret.Items.Where(i => !i.IsDeleted).Select(i => (i.OrderItemId, i.Quantity)));

    private static DeliveryInfo Delivery(Order? order, bool full) => new(full, order?.DeliveryCharge ?? 0,
        full ? order?.DeliveryCharge ?? 0 : 0, full ? order?.CouponDiscount ?? 0 : 0);

    /// <summary>
    /// Delivery rule: a partial return refunds only the returned items (delivery is NOT refunded);
    /// a full return of every item refunds what the customer actually paid — items − coupon + delivery.
    /// </summary>
    private void ApplyDeliveryRule(ReturnOrder ret, Order order)
    {
        if (!IsFullReturn(order, ret.Items.Where(i => !i.IsDeleted).Select(i => (i.OrderItemId, i.Quantity)))) return;
        ret.GrandRefundAmount = _tax.Round(ret.GrandRefundAmount + order.DeliveryCharge - order.CouponDiscount);
    }

    private static List<string> PhotoPaths(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); } catch (JsonException) { return new(); }
    }

    /// <summary>Stored relative path of photo #index — for the owner (userId) or any admin (userId null).</summary>
    public async Task<string?> GetPhotoPathAsync(int returnId, int index, int? userId)
    {
        var json = await _db.ReturnOrders.AsNoTracking()
            .Where(r => r.Id == returnId && !r.IsDeleted && (userId == null || r.UserId == userId))
            .Select(r => r.PhotoUrls).FirstOrDefaultAsync();
        var list = PhotoPaths(json);
        return index >= 0 && index < list.Count ? list[index] : null;
    }

    private void ApplyTaxSummary(
        ReturnOrder ret,
        List<ReturnItemTaxInput> taxInputs,
        GSTType gstType,
        bool includeTcs = false,
        decimal tcsRate = 1m)
    {
        var summary = _tax.CalculateReturnTax(taxInputs, gstType, TaxPriceType.Inclusive, includeTcs, tcsRate);

        ret.SubtotalMrp = _tax.Round(taxInputs.Sum(i => i.UnitMrp * i.Quantity));
        ret.SubtotalSelling = summary.SubtotalSelling;
        ret.TaxableValue = summary.TotalTaxableValue;
        ret.CgstRate = summary.CgstRate;
        ret.SgstRate = summary.SgstRate;
        ret.IgstRate = summary.IgstRate;
        ret.CgstAmount = summary.TotalCgst;
        ret.SgstAmount = summary.TotalSgst;
        ret.IgstAmount = summary.TotalIgst;
        ret.TotalGstAmount = summary.TotalGst;
        ret.TcsRate = summary.TcsRate;
        ret.TcsAmount = summary.TcsAmount;
        ret.CreditNoteAmount = summary.CreditNoteAmount;
        ret.GrandRefundAmount = summary.GrandRefundAmount;
    }

    private async Task<string> GenerateReturnNumberAsync()
    {
        string year = DateTime.UtcNow.Year.ToString();
        int count = await _db.ReturnOrders.CountAsync(r => r.ReturnNumber.Contains(year)) + 1;
        return $"RET/{year}/{count:D4}";
    }

    private async Task<string> GenerateCreditNoteNumberAsync()
    {
        string year = DateTime.UtcNow.Year.ToString();
        int count = await _db.ReturnOrders.CountAsync(r => r.CreditNoteNumber != null && r.CreditNoteNumber.Contains(year)) + 1;
        return $"CN/{year}/{count:D4}";
    }

    private ReturnTaxDto BuildTaxDto(ReturnTaxSummary summary, string? creditNoteNumber, DateTime? creditNoteDate, DeliveryInfo? delivery = null)
    {
        delivery ??= new DeliveryInfo(false, 0, 0, 0);
        var dto = new ReturnTaxDto
        {
            IsFullReturn = delivery.Full,
            OrderDeliveryCharge = delivery.OrderDelivery,
            DeliveryChargeRefund = delivery.DeliveryRefund,
            CouponDiscountDeducted = delivery.CouponDeducted,
            DeliveryNote = delivery.OrderDelivery <= 0 ? ""
                : delivery.Full ? $"Full return — the delivery charge of ₹{delivery.OrderDelivery:0.##} is refunded too."
                : $"Partial return — the delivery charge of ₹{delivery.OrderDelivery:0.##} is not refunded (it is refunded only when every item of the order is returned).",
            GstType = summary.GstType,
            GstTypeLabel = summary.GstTypeLabel,
            PriceType = summary.PriceType,
            SubtotalSelling = summary.SubtotalSelling,
            TotalTaxableValue = summary.TotalTaxableValue,
            CgstRate = summary.CgstRate,
            SgstRate = summary.SgstRate,
            IgstRate = summary.IgstRate,
            CgstAmount = summary.TotalCgst,
            SgstAmount = summary.TotalSgst,
            IgstAmount = summary.TotalIgst,
            TotalGstReversed = summary.TotalGst,
            TcsRate = summary.TcsRate,
            TcsAmount = summary.TcsAmount,
            CreditNoteAmount = summary.CreditNoteAmount,
            GrandRefundAmount = summary.GrandRefundAmount,
            CreditNoteNumber = creditNoteNumber,
            CreditNoteDate = creditNoteDate,
            FormulaBreakdown = BuildFormulaBreakdown(summary, delivery)
        };

        return dto;
    }

    private List<TaxFormulaLine> BuildFormulaBreakdown(ReturnTaxSummary summary, DeliveryInfo delivery)
    {
        var lines = new List<TaxFormulaLine>
        {
            new() { Label = "Selling Amount (GST-inclusive)", Formula = "Sum of (unit price × quantity)", Value = summary.SubtotalSelling },
            new() { Label = "Taxable Value (ex-GST)", Formula = $"Selling Amount × 100 / (100 + GST%)", Value = summary.TotalTaxableValue }
        };

        if (summary.GstType == GSTType.IntraState)
        {
            lines.Add(new() { Label = $"CGST ({summary.CgstRate}%)", Formula = $"Taxable Value × {summary.CgstRate} / 100", Value = summary.TotalCgst });
            lines.Add(new() { Label = $"SGST ({summary.SgstRate}%)", Formula = $"Taxable Value × {summary.SgstRate} / 100", Value = summary.TotalSgst });
        }
        else if (summary.GstType == GSTType.InterState)
        {
            lines.Add(new() { Label = $"IGST ({summary.IgstRate}%)", Formula = $"Taxable Value × {summary.IgstRate} / 100", Value = summary.TotalIgst });
        }

        lines.Add(new() { Label = "Total GST Reversed", Formula = "CGST + SGST  (or IGST)", Value = summary.TotalGst });
        lines.Add(new() { Label = "Credit Note Amount", Formula = "Taxable Value + Total GST", Value = summary.CreditNoteAmount });

        if (summary.TcsAmount > 0)
            lines.Add(new() { Label = $"TCS Returned (Section 52 @ {summary.TcsRate}%)", Formula = $"Taxable Value × {summary.TcsRate} / 100", Value = summary.TcsAmount });

        if (delivery.Full && delivery.DeliveryRefund > 0)
            lines.Add(new() { Label = "Delivery charge refunded", Formula = "Full return of every item", Value = delivery.DeliveryRefund });
        else if (!delivery.Full && delivery.OrderDelivery > 0)
            lines.Add(new() { Label = "Delivery charge (not refunded)", Formula = "Partial return — delivery is refunded only on a full return", Value = 0 });
        if (delivery.Full && delivery.CouponDeducted > 0)
            lines.Add(new() { Label = "Coupon discount deducted", Formula = "Full return refunds what was actually paid", Value = -delivery.CouponDeducted });

        lines.Add(new() { Label = "Grand Refund to Customer", Formula = delivery.Full && (delivery.DeliveryRefund > 0 || delivery.CouponDeducted > 0)
            ? "Selling Amount + Delivery − Coupon + TCS (if any)" : "Selling Amount + TCS (if any)", Value = summary.GrandRefundAmount });

        return lines;
    }

    private ReturnDto MapToDto(ReturnOrder ret, string orderNumber, string photoBase = "/admin/returns")
    {
        // Full return: decided from the order lines when loaded, else from the stored refund (it includes the adjustment)
        var full = ret.Order?.Items is { Count: > 0 } ? IsFullReturn(ret)
            : Math.Abs(ret.GrandRefundAmount - ret.SubtotalSelling - ret.TcsAmount) > 0.005m;
        var photos = PhotoPaths(ret.PhotoUrls);
        // Build summary from stored fields
        var taxSummary = new ReturnTaxSummary
        {
            GstType = ret.GstType,
            PriceType = ret.PriceType,
            SubtotalSelling = ret.SubtotalSelling,
            TotalTaxableValue = ret.TaxableValue,
            TotalCgst = ret.CgstAmount,
            TotalSgst = ret.SgstAmount,
            TotalIgst = ret.IgstAmount,
            TotalGst = ret.TotalGstAmount,
            TcsRate = ret.TcsRate,
            TcsAmount = ret.TcsAmount,
            CreditNoteAmount = ret.CreditNoteAmount,
            GrandRefundAmount = ret.GrandRefundAmount,
            CgstRate = ret.CgstRate,
            SgstRate = ret.SgstRate,
            IgstRate = ret.IgstRate
        };

        return new ReturnDto
        {
            Id = ret.Id,
            ReturnNumber = ret.ReturnNumber,
            OrderNumber = orderNumber,
            OrderId = ret.OrderId,
            Status = ret.Status,
            StatusLabel = ret.Status.ToString(),
            Type = ret.Type,
            Reason = ret.Reason,
            AdminNotes = ret.AdminNotes,
            RejectionReason = ret.RejectionReason,
            PickupAddress = ret.PickupAddress,
            CourierName = ret.CourierName,
            AwbNumber = ret.AwbNumber,
            PickupScheduledAt = ret.PickupScheduledAt,
            PickedUpAt = ret.PickedUpAt,
            ReceivedAt = ret.ReceivedAt,
            ProcessedAt = ret.ProcessedAt,
            TaxBreakdown = BuildTaxDto(taxSummary, ret.CreditNoteNumber, ret.CreditNoteDate, Delivery(ret.Order, full)),
            RefundMethod = ret.RefundMethod,
            RefundReferenceId = ret.RefundReferenceId,
            RefundInitiatedAt = ret.RefundInitiatedAt,
            RefundCompletedAt = ret.RefundCompletedAt,
            Items = ret.Items.Select(MapItemToDto).ToList(),
            CreatedAt = ret.CreatedAt,
            CustomerName = ret.User?.Name ?? string.Empty,
            CustomerEmail = ret.User?.Email ?? string.Empty,
            CustomerPhone = ret.User?.Phone ?? string.Empty,
            OrderStatus = ret.Order?.Status.ToString() ?? string.Empty,
            PaymentMethod = ret.Order?.PaymentMethod.ToString() ?? string.Empty,
            PaymentStatus = ret.Order?.PaymentStatus.ToString() ?? string.Empty,
            OrderTotal = ret.Order?.GrandTotal ?? 0,
            OrderDeliveredAt = ret.Order?.DeliveredAt,
            NextActions = NextActionsFor(ret.Status),
            Photos = photos.Select((_, i) => new ReturnPhotoDto { Index = i, Url = $"{photoBase}/{ret.Id}/photos/{i}" }).ToList(),
        };
    }

    private ReturnItemDto MapItemToDto(ReturnItem item) => new()
    {
        Id = item.Id,
        OrderItemId = item.OrderItemId,
        ProductId = item.ProductId,
        ProductName = item.ProductName,
        Weight = item.Weight,
        HsnCode = item.HsnCode,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        UnitMrp = item.UnitMrp,
        LineTotal = item.LineTotal,
        GstRate = item.GstRate,
        TaxableValue = item.TaxableValue,
        CgstRate = item.CgstRate,
        SgstRate = item.SgstRate,
        IgstRate = item.IgstRate,
        CgstAmount = item.CgstAmount,
        SgstAmount = item.SgstAmount,
        IgstAmount = item.IgstAmount,
        TotalTax = item.TotalTax,
        LineTotalWithTax = item.LineTotalWithTax,
        ReturnReason = item.ReturnReason,
        Condition = item.Condition,
        IsApproved = item.IsApproved
    };

    private ReturnSummaryDto MapToSummary(ReturnOrder ret, string orderNumber) => new()
    {
        Id = ret.Id,
        ReturnNumber = ret.ReturnNumber,
        OrderNumber = orderNumber,
        Status = ret.Status,
        StatusLabel = ret.Status.ToString(),
        Type = ret.Type,
        Reason = ret.Reason,
        ItemCount = ret.Items.Count,
        GrandRefundAmount = ret.GrandRefundAmount,
        CreditNoteNumber = ret.CreditNoteNumber,
        CreatedAt = ret.CreatedAt,
        RefundCompletedAt = ret.RefundCompletedAt,
        OrderId = ret.OrderId,
        CustomerName = ret.User?.Name ?? string.Empty,
        CustomerPhone = ret.User?.Phone ?? string.Empty,
        CustomerEmail = ret.User?.Email ?? string.Empty,
        RefundMethod = ret.RefundMethod,
        PaymentMethod = ret.Order?.PaymentMethod.ToString() ?? string.Empty,
        PhotoCount = PhotoPaths(ret.PhotoUrls).Count,
    };
}
