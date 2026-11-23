using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Inventory;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(AppDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Auto deduct stock when order is confirmed ─────────────────────────
    // Same rules as website orders (StoreRules): idempotent per order number and pack-size aware.
    public async Task DeductStockAsync(int orderId, string performedBy = "System")
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted)
            ?? throw AppException.NotFound("Order");

        await StoreRules.DeductForOrderAsync(_db, order.OrderNumber,
            order.Items.Where(i => !i.IsDeleted).Select(i => (i.ProductId, i.Quantity)), performedBy);
        await _db.SaveChangesAsync();
    }

    // ── Restore stock when order is cancelled ─────────────────────────────
    public async Task RestoreStockAsync(int orderId, string performedBy = "System")
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted)
            ?? throw AppException.NotFound("Order");

        await StoreRules.RestoreAsync(_db, order.OrderNumber, order.OrderNumber,
            order.Items.Where(i => !i.IsDeleted).Select(i => (i.ProductId, i.Quantity)),
            $"Order #{order.OrderNumber} cancelled", performedBy);
        await _db.SaveChangesAsync();
    }

    public async Task<PagedResult<InventoryItemDto>> GetAllAsync(InventoryFilterParams filter)
    {
        var query = _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            query = query.Where(i =>
                i.Sku.ToLower().Contains(s) ||
                i.Product.Name.ToLower().Contains(s));
        }

        if (filter.LowStockOnly == true)
            query = query.Where(i => i.CurrentStock <= i.ReorderPoint);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.Product.Name)
            .Skip((filter.Page - 1) * filter.Limit)
            .Take(filter.Limit)
            .Select(i => new InventoryItemDto(
                i.Id, i.ProductId, i.Product.Name, i.Product.ImageUrl,
                i.Sku, i.CurrentStock, i.ReorderPoint,
                i.CurrentStock <= i.ReorderPoint,
                i.BatchNumber, i.ExpiryDate, i.Location, i.CostPrice, i.Unit))
            .ToListAsync();

        return new PagedResult<InventoryItemDto> { Items = items, Total = total, Page = filter.Page, Limit = filter.Limit };
    }

    public async Task<InventoryItemDto?> GetByProductIdAsync(int productId)
    {
        var i = await _db.InventoryItems
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.ProductId == productId && !x.IsDeleted);

        return i == null ? null : new InventoryItemDto(
            i.Id, i.ProductId, i.Product.Name, i.Product.ImageUrl,
            i.Sku, i.CurrentStock, i.ReorderPoint, i.CurrentStock <= i.ReorderPoint,
            i.BatchNumber, i.ExpiryDate, i.Location, i.CostPrice, i.Unit);
    }

    // ── Manual stock adjustment ────────────────────────────────────────────
    public async Task AdjustStockAsync(int inventoryItemId, AdjustStockRequest request, string performedBy)
    {
        var inv = await _db.InventoryItems.FindAsync(inventoryItemId)
            ?? throw AppException.NotFound("InventoryItem");

        var newStock = request.Type == StockAdjustmentType.Reduction || request.Type == StockAdjustmentType.Damage
            ? inv.CurrentStock - request.Quantity
            : inv.CurrentStock + request.Quantity;

        if (newStock < 0) throw new AppException("Insufficient stock for this adjustment", 400, "INSUFFICIENT_STOCK");

        _db.InventoryLedgers.Add(new InventoryLedger
        {
            InventoryItemId = inv.Id,
            Type = request.Type,
            Quantity = request.Quantity,
            BalanceAfter = newStock,
            Reason = request.Reason,
            BatchNumber = request.BatchNumber,
            PerformedBy = performedBy,
            CreatedBy = performedBy
        });

        inv.CurrentStock = newStock;
        if (request.BatchNumber != null) inv.BatchNumber = request.BatchNumber;
        inv.UpdatedAt = DateTime.UtcNow;
        inv.UpdatedBy = performedBy;

        await _db.SaveChangesAsync();
    }

    public async Task<List<InventoryLedgerDto>> GetLedgerAsync(int inventoryItemId, int limit = 50)
    {
        return await _db.InventoryLedgers
            .Include(l => l.InventoryItem).ThenInclude(i => i.Product)
            .Where(l => l.InventoryItemId == inventoryItemId && !l.IsDeleted)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new InventoryLedgerDto(
                l.Id, l.InventoryItem.Product.Name, l.Type,
                l.Quantity, l.BalanceAfter, l.Reason, l.ReferenceId,
                l.PerformedBy, l.CreatedAt))
            .ToListAsync();
    }

    public async Task<List<InventoryItemDto>> GetLowStockAsync()
    {
        return await _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted && i.CurrentStock <= i.ReorderPoint)
            .OrderBy(i => i.CurrentStock)
            .Select(i => new InventoryItemDto(
                i.Id, i.ProductId, i.Product.Name, i.Product.ImageUrl,
                i.Sku, i.CurrentStock, i.ReorderPoint, true,
                i.BatchNumber, i.ExpiryDate, i.Location, i.CostPrice, i.Unit))
            .ToListAsync();
    }
}
