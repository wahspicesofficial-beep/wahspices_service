using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/inventory")]
[RequireModule("Inventory")]
public class AdminInventoryController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventoryService;

    public AdminInventoryController(AppDbContext db, IInventoryService inventoryService)
    {
        _db = db;
        _inventoryService = inventoryService;
    }

    /// <summary>Get all inventory items</summary>
    [RequireModule("Inventory", "Billing", "Purchase", "Production", "Barcode")]   // stock picker on billing/purchase
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? location,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 30)
    {
        var query = _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(i => i.Product.CategorySlug == category);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.Product.Name.Contains(search) || i.Sku.Contains(search) || (i.Variant != null && i.Variant.Weight.Contains(search)));

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(i => i.Location == location);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.Product.Name).ThenBy(i => i.Variant != null ? i.Variant.WeightGrams : 0).ThenBy(i => i.Location)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(i => new InventoryDto(
                i.Id, i.Sku, i.ProductId, (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")),
                i.Product.CategorySlug, i.Unit,
                i.CurrentStock, i.ReorderPoint, i.CostPrice,
                i.CostPrice * i.CurrentStock,
                i.BatchNumber, i.ExpiryDate, i.Location,
                i.CurrentStock <= i.ReorderPoint
            ))
            .ToListAsync();

        var totalValue    = await query.SumAsync(i => (decimal?)(i.CostPrice * i.CurrentStock)) ?? 0;
        var lowStockTotal = await query.CountAsync(i => i.CurrentStock <= i.ReorderPoint);
        var locationCount = await query.Select(i => i.Location).Distinct().CountAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Items = items,
            Summary = new { total, TotalValue = totalValue, LowStock = lowStockTotal, Locations = locationCount },
            Pagination = new { total, page, limit }
        }));
    }

    /// <summary>Get items below reorder level</summary>
    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock()
    {
        var items = await _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted && i.CurrentStock <= i.ReorderPoint)
            .Select(i => new InventoryDto(
                i.Id, i.Sku, i.ProductId, (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")),
                i.Product.CategorySlug, i.Unit,
                i.CurrentStock, i.ReorderPoint, i.CostPrice,
                i.CostPrice * i.CurrentStock,
                i.BatchNumber, i.ExpiryDate, i.Location, true
            ))
            .ToListAsync();

        return Ok(ApiResponse<List<InventoryDto>>.Ok(items));
    }

    /// <summary>Create inventory item (auto-creates product if not found by SKU)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInventoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductName))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Product name is required.", "productName"));
        if (string.IsNullOrWhiteSpace(request.Sku))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "SKU is required.", "sku"));
        if (request.CurrentStock < 0 || request.ReorderPoint < 0 || request.CostPrice < 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Stock, reorder point and cost price cannot be negative."));
        var location = string.IsNullOrWhiteSpace(request.Location) ? "Warehouse A" : request.Location.Trim();
        var sku = request.Sku.Trim();
        // The schema allows one inventory row per product and a globally unique SKU (Product 1:1 InventoryItem)
        var skuOwner = await _db.InventoryItems.Include(i => i.Product).FirstOrDefaultAsync(i => i.Sku == sku);
        if (skuOwner != null && !skuOwner.IsDeleted)
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_SKU", $"SKU {sku} is already used by {skuOwner.Product?.Name} ({skuOwner.Location}). Adjust its stock instead.", "sku"));

        // Link to an existing product (by slug or exact name) or create a draft product
        var slug = sku.ToLower().Replace(" ", "-");
        var name = request.ProductName.Trim();
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Slug == slug || p.Name == name);

        if (product != null)
        {
            var existing = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductId == product.Id && i.VariantId == null);
            if (existing != null && !existing.IsDeleted)
                return Conflict(ApiResponse<object>.Fail("ALREADY_TRACKED", $"{product.Name} is already in inventory (SKU {existing.Sku}, {existing.Location}). Adjust its stock instead.", "productName"));
            if (existing != null)
            {
                if (skuOwner != null && skuOwner.Id != existing.Id)
                    return Conflict(ApiResponse<object>.Fail("DUPLICATE_SKU", $"SKU {sku} belonged to a removed item of another product. Use a different SKU.", "sku"));
                // Re-adding a removed item: revive the row instead of inserting a duplicate
                existing.IsDeleted    = false;
                existing.Sku          = sku;
                existing.CurrentStock = request.CurrentStock;
                existing.ReorderPoint = request.ReorderPoint;
                existing.CostPrice    = request.CostPrice;
                existing.Unit         = string.IsNullOrWhiteSpace(request.Unit) ? "pcs" : request.Unit;
                existing.Location     = location;
                existing.BatchNumber  = request.BatchNumber;
                existing.ExpiryDate   = request.ExpiryDate;
                existing.UpdatedAt    = DateTime.UtcNow;
                existing.UpdatedBy    = AdminName;
                _db.InventoryLedgers.Add(new InventoryLedger
                {
                    InventoryItemId = existing.Id, Type = StockAdjustmentType.OpeningBalance,
                    Quantity = request.CurrentStock, BalanceAfter = request.CurrentStock,
                    Reason = "Opening balance (re-added)", BatchNumber = request.BatchNumber, PerformedBy = AdminName
                });
                await _db.SaveChangesAsync();
                return StatusCode(201, ApiResponse<object>.Ok(new InventoryDto(
                    existing.Id, existing.Sku, existing.ProductId, product.Name, product.CategorySlug, existing.Unit,
                    existing.CurrentStock, existing.ReorderPoint, existing.CostPrice, existing.CostPrice * existing.CurrentStock,
                    existing.BatchNumber, existing.ExpiryDate, existing.Location, existing.CurrentStock <= existing.ReorderPoint
                ), "Inventory item re-added."));
            }
        }
        if (skuOwner != null)
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_SKU", $"SKU {sku} belonged to a removed item. Use a different SKU.", "sku"));

        if (product == null)
        {
            product = new Product
            {
                Name         = name,
                Slug         = slug,
                CategorySlug = string.IsNullOrWhiteSpace(request.CategorySlug) ? "blend-masala" : request.CategorySlug,
                Price        = request.CostPrice,
                Mrp          = request.CostPrice,
                Weight       = request.Unit,
                IsDraft      = true,
            };
            _db.Products.Add(product);
        }

        var item = new InventoryItem
        {
            Product      = product,
            Sku          = sku,
            CurrentStock = request.CurrentStock,
            ReorderPoint = request.ReorderPoint,
            CostPrice    = request.CostPrice,
            Unit         = string.IsNullOrWhiteSpace(request.Unit) ? "pcs" : request.Unit,
            Location     = location,
            BatchNumber  = request.BatchNumber,
            ExpiryDate   = request.ExpiryDate,
            CreatedBy    = AdminName,
        };
        _db.InventoryItems.Add(item);

        // Navigation (not InventoryItemId) so EF inserts the item first and fills the FK
        _db.InventoryLedgers.Add(new InventoryLedger
        {
            InventoryItem   = item,
            Type            = StockAdjustmentType.OpeningBalance,
            Quantity        = request.CurrentStock,
            BalanceAfter    = request.CurrentStock,
            Reason          = "Opening balance",
            BatchNumber     = request.BatchNumber,
            PerformedBy     = AdminName
        });

        await _db.SaveChangesAsync();

        return StatusCode(201, ApiResponse<object>.Ok(new InventoryDto(
            item.Id, item.Sku, item.ProductId, product.Name,
            product.CategorySlug, item.Unit,
            item.CurrentStock, item.ReorderPoint, item.CostPrice,
            item.CostPrice * item.CurrentStock,
            item.BatchNumber, item.ExpiryDate, item.Location,
            item.CurrentStock <= item.ReorderPoint
        ), "Inventory item created."));
    }

    /// <summary>Update inventory item</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateInventoryRequest request)
    {
        var item = await _db.InventoryItems.FindAsync(id);
        if (item == null || item.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Item not found."));
        if (request.ReorderPoint < 0 || request.CostPrice < 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Reorder point and cost price cannot be negative."));

        if (request.ReorderPoint.HasValue) item.ReorderPoint = request.ReorderPoint.Value;
        if (request.CostPrice.HasValue)    item.CostPrice    = request.CostPrice.Value;
        if (!string.IsNullOrWhiteSpace(request.Location)) item.Location = request.Location.Trim();
        if (request.BatchNumber != null)   item.BatchNumber  = request.BatchNumber;
        if (request.ExpiryDate.HasValue)   item.ExpiryDate   = request.ExpiryDate;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = AdminName;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            id           = item.Id,
            reorderPoint = item.ReorderPoint,
            costPrice    = item.CostPrice,
            location     = item.Location,
            batchNumber  = item.BatchNumber,
            expiryDate   = item.ExpiryDate,
            stockValue   = item.CostPrice * item.CurrentStock,
            isLowStock   = item.CurrentStock <= item.ReorderPoint,
        }, "Updated."));
    }

    /// <summary>Delete inventory item</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.InventoryItems.FindAsync(id);
        if (item == null || item.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Item not found."));

        item.IsDeleted = true;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Deleted."));
    }

    /// <summary>Manual stock adjustment</summary>
    [HttpPost("{id:int}/adjust")]
    public async Task<IActionResult> Adjust(int id, [FromBody] StockAdjustmentRequest request)
    {
        var item = await _db.InventoryItems.FindAsync(id);
        if (item == null || item.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Item not found."));

        if (!Enum.TryParse<StockAdjustmentType>(request.Type, true, out var adjType) || !Enum.IsDefined(adjType))
            return BadRequest(ApiResponse<object>.Fail("INVALID_TYPE", "Invalid adjustment type."));
        if (request.Quantity <= 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Quantity must be greater than zero.", "quantity"));

        // Types that bring stock in; everything else (Reduction, Damage, SaleDeduction) takes stock out
        if (adjType is StockAdjustmentType.Addition or StockAdjustmentType.OpeningBalance
            or StockAdjustmentType.Return or StockAdjustmentType.GRNReceipt or StockAdjustmentType.SaleReversal)
            item.CurrentStock += request.Quantity;
        else
        {
            if (item.CurrentStock < request.Quantity)
                return BadRequest(ApiResponse<object>.Fail("INSUFFICIENT_STOCK", "Reduction would make stock negative."));
            item.CurrentStock -= request.Quantity;
        }

        if (!string.IsNullOrWhiteSpace(request.BatchNumber)) item.BatchNumber = request.BatchNumber;
        if (request.ExpiryDate.HasValue) item.ExpiryDate = request.ExpiryDate;
        item.UpdatedAt = DateTime.UtcNow;

        _db.InventoryLedgers.Add(new InventoryLedger
        {
            InventoryItemId = id,
            Type            = adjType,
            Quantity        = request.Quantity,
            BalanceAfter    = item.CurrentStock,
            Reason          = request.Reason,
            BatchNumber     = request.BatchNumber,
            PerformedBy     = AdminName
        });

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            id           = item.Id,
            currentStock = item.CurrentStock,
            isLowStock   = item.CurrentStock <= item.ReorderPoint,
            stockValue   = item.CostPrice * item.CurrentStock,
            batchNumber  = item.BatchNumber,
            expiryDate   = item.ExpiryDate,
        }, $"Stock adjusted. New quantity: {item.CurrentStock}"));
    }

    /// <summary>Get stock movement ledger</summary>
    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedger(
        [FromQuery] int? productId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var query = _db.InventoryLedgers
            .Include(l => l.InventoryItem).ThenInclude(i => i.Product)
            .AsQueryable();

        if (productId.HasValue)
            query = query.Where(l => l.InventoryItem.ProductId == productId);
        if (dateFrom.HasValue) query = query.Where(l => l.CreatedAt >= dateFrom);
        if (dateTo.HasValue)   query = query.Where(l => l.CreatedAt <= dateTo);

        var total = await query.CountAsync();
        var entries = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(l => new InventoryLedgerDto(
                l.Id, l.CreatedAt,
                (l.InventoryItem.Product.Name + (l.InventoryItem.Variant != null ? " · " + l.InventoryItem.Variant.Weight : "")),
                l.Type.ToString(), l.Quantity, l.BalanceAfter,
                l.Reason ?? "", l.PerformedBy
            ))
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Ledger = entries, Pagination = new { total, page, limit } }));
    }

    // ── SKU Management ────────────────────────────────────────
    /// <summary>List all SKUs currently in inventory</summary>
    [HttpGet("skus")]
    public async Task<IActionResult> GetSkus([FromQuery] string? search)
    {
        var q = _db.InventoryItems.Where(i => !i.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(i => i.Sku.Contains(search) || i.Product.Name.Contains(search));

        // One row per SKU (the same SKU can be stocked at several locations)
        var rows = await q
            .Select(i => new { i.Sku, ProductName = (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")), i.Product.CategorySlug, i.Unit, i.CurrentStock, i.Location })
            .ToListAsync();
        var skus = rows
            .GroupBy(r => r.Sku)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                sku          = g.Key,
                productName  = g.First().ProductName,
                categorySlug = g.First().CategorySlug,
                unit         = g.First().Unit,
                totalStock   = g.Sum(x => x.CurrentStock),
                locations    = g.Select(x => x.Location).Distinct().ToList(),
            })
            .ToList();

        return Ok(ApiResponse<object>.Ok(skus));
    }

    /// <summary>Auto-generate a unique SKU from product name + category</summary>
    [HttpGet("sku/generate")]
    public async Task<IActionResult> GenerateSku([FromQuery] string name, [FromQuery] string? category)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "name is required"));

        // Category prefix: first 3 uppercase letters of category slug
        var catCode = string.IsNullOrWhiteSpace(category)
            ? "GEN"
            : new string(category.Replace("-", "").ToUpper().Where(char.IsLetter).Take(3).ToArray()).PadRight(3, 'X');

        // Product code: remove vowels + spaces, take first 5 uppercase chars
        var vowels = new HashSet<char>("aeiouAEIOU ");
        var prodRaw = new string(name.Where(c => !vowels.Contains(c) && char.IsLetterOrDigit(c)).ToArray()).ToUpper();
        var prodCode = (prodRaw.Length >= 4 ? prodRaw[..4] : prodRaw.PadRight(4, 'X'));

        // Find next available sequence number
        var prefix = $"{catCode}-{prodCode}-";
        var existing = await _db.InventoryItems
            .Where(i => i.Sku.StartsWith(prefix))
            .Select(i => i.Sku)
            .ToListAsync();

        var seq = 1;
        string sku;
        do { sku = $"{prefix}{seq:D3}"; seq++; }
        while (existing.Contains(sku));

        return Ok(ApiResponse<object>.Ok(new { sku, prefix, suggestion = sku }));
    }

    // ── Batch Tracking ───────────────────────────────────────────
    /// <summary>All items with batch info; status = all | expiring | expired</summary>
    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches([FromQuery] string? status, [FromQuery] int expiryDays = 30)
    {
        var today  = DateTime.UtcNow.Date;
        var cutoff = today.AddDays(expiryDays);

        var q = _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted)
            .AsQueryable();

        var all = await q
            .Select(i => new
            {
                i.Id, i.Sku, i.ProductId,
                ProductName   = (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")),
                CategorySlug  = i.Product.CategorySlug,
                i.CurrentStock, i.Unit, i.Location,
                i.BatchNumber, i.ExpiryDate, i.ManufactureDate,
                i.CostPrice,
                IsExpired   = i.ExpiryDate.HasValue && i.ExpiryDate.Value.Date < today,
                IsExpiring  = i.ExpiryDate.HasValue && i.ExpiryDate.Value.Date >= today && i.ExpiryDate.Value.Date <= cutoff,
                DaysToExpiry = i.ExpiryDate.HasValue
                    ? (int?)(i.ExpiryDate.Value.Date - today).TotalDays
                    : null
            })
            .ToListAsync();

        var filtered = status switch
        {
            "expired"  => all.Where(x => x.IsExpired).ToList(),
            "expiring" => all.Where(x => x.IsExpiring).ToList(),
            _          => all
        };

        return Ok(ApiResponse<object>.Ok(new
        {
            Items       = filtered.OrderBy(x => x.ExpiryDate ?? DateTime.MaxValue),
            TotalExpired  = all.Count(x => x.IsExpired),
            TotalExpiring = all.Count(x => x.IsExpiring),
        }));
    }

    // ── Alerts ────────────────────────────────────────────────────
    /// <summary>Combined low-stock + expiry alerts</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] int expiryDays = 30)
    {
        var today  = DateTime.UtcNow.Date;
        var cutoff = today.AddDays(expiryDays);

        var items = await _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted)
            .Select(i => new
            {
                i.Id, i.Sku,
                ProductName = (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")),
                i.CurrentStock, i.ReorderPoint, i.Unit, i.Location,
                i.BatchNumber, i.ExpiryDate, i.CostPrice,
                IsLowStock  = i.CurrentStock <= i.ReorderPoint,
                IsExpired   = i.ExpiryDate.HasValue && i.ExpiryDate.Value.Date < today,
                IsExpiring  = i.ExpiryDate.HasValue && i.ExpiryDate.Value.Date >= today && i.ExpiryDate.Value.Date <= cutoff,
                DaysToExpiry = i.ExpiryDate.HasValue ? (int?)(i.ExpiryDate.Value.Date - today).TotalDays : null,
                StockValue  = i.CostPrice * i.CurrentStock,
            })
            .ToListAsync();

        var lowStock = items.Where(i => i.IsLowStock).ToList();
        var expired  = items.Where(i => i.IsExpired).ToList();
        var expiring = items.Where(i => i.IsExpiring).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            LowStock  = lowStock,
            Expired   = expired,
            Expiring  = expiring,
            Summary   = new
            {
                TotalLowStock   = lowStock.Count,
                TotalExpired    = expired.Count,
                TotalExpiring   = expiring.Count,
                ExpiredValue    = expired.Sum(i => i.StockValue),
                ExpiringValue   = expiring.Sum(i => i.StockValue),
            }
        }));
    }

    // ── Multi-Location ────────────────────────────────────────────
    /// <summary>Stock summary grouped by location</summary>
    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations()
    {
        var items = await _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted)
            .Select(i => new
            {
                i.Location, i.Sku,
                ProductName  = (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")),
                i.CurrentStock, i.Unit, i.CostPrice,
                i.BatchNumber, i.ExpiryDate,
                StockValue   = i.CostPrice * i.CurrentStock,
                IsLowStock   = i.CurrentStock <= i.ReorderPoint,
            })
            .ToListAsync();

        var byLocation = items
            .GroupBy(i => i.Location ?? "Unassigned")
            .Select(g => new
            {
                Location     = g.Key,
                ItemCount    = g.Count(),
                TotalValue   = g.Sum(i => i.StockValue),
                LowStockCount = g.Count(i => i.IsLowStock),
                Items        = g.OrderBy(i => i.ProductName).ToList(),
            })
            .OrderBy(g => g.Location)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            Locations    = byLocation,
            AllLocations = byLocation.Select(l => l.Location).ToList(),
        }));
    }

    /// <summary>Transfer stock from one location to another</summary>
    [HttpPost("{id:int}/transfer")]
    public async Task<IActionResult> Transfer(int id, [FromBody] TransferRequest req)
    {
        var source = await _db.InventoryItems
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

        if (source == null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Inventory item not found."));
        if (string.IsNullOrWhiteSpace(req.ToLocation))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Destination location is required.", "toLocation"));
        if (req.Quantity <= 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Quantity must be greater than zero.", "quantity"));
        if (source.Location == req.ToLocation)
            return BadRequest(ApiResponse<object>.Fail("SAME_LOCATION", "Source and destination are the same."));
        if (source.CurrentStock < req.Quantity)
            return BadRequest(ApiResponse<object>.Fail("INSUFFICIENT_STOCK", "Not enough stock to transfer."));

        // The schema keeps ONE inventory row per product (Product 1:1 InventoryItem, unique SKU), so stock
        // can't be split across locations yet: a transfer moves the item's whole stock to the new location.
        if (req.Quantity != source.CurrentStock)
            return BadRequest(ApiResponse<object>.Fail("PARTIAL_TRANSFER_UNSUPPORTED",
                $"Each product is stocked at one location, so move all {source.CurrentStock} {source.Unit} at once (partial transfers need multi-location stock).", "quantity"));

        var fromLocation = source.Location;
        source.Location  = req.ToLocation.Trim();
        source.UpdatedAt = DateTime.UtcNow;
        source.UpdatedBy = AdminName;

        var note = string.IsNullOrWhiteSpace(req.Notes) ? "" : $": {req.Notes.Trim()}";
        _db.InventoryLedgers.Add(new InventoryLedger
        {
            InventoryItemId = source.Id,
            Type            = StockAdjustmentType.Reduction,
            Quantity        = req.Quantity,
            BalanceAfter    = 0,
            Reason          = $"Transfer out of {fromLocation} to {source.Location}{note}",
            PerformedBy     = AdminName
        });
        _db.InventoryLedgers.Add(new InventoryLedger
        {
            InventoryItemId = source.Id,
            Type            = StockAdjustmentType.Addition,
            Quantity        = req.Quantity,
            BalanceAfter    = source.CurrentStock,
            Reason          = $"Transfer into {source.Location} from {fromLocation}{note}",
            PerformedBy     = AdminName
        });
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            SourceId     = source.Id,
            DestId       = source.Id,
            SourceStock  = 0,
            DestStock    = source.CurrentStock,
            FromLocation = fromLocation,
            ToLocation   = source.Location,
            Quantity     = req.Quantity,
        }, $"Moved {req.Quantity} {source.Unit} from {fromLocation} to {source.Location}."));
    }

    /// <summary>Trigger auto stock deduction for a confirmed order (for COD or manual)</summary>
    [RequireModule("Inventory", "Orders")]
    [HttpPost("deduct-order/{orderId:int}")]
    public async Task<IActionResult> DeductOrder(int orderId)
    {
        try
        {
            var adminName = User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
            await _inventoryService.DeductStockAsync(orderId, adminName);
            return Ok(ApiResponse<string?>.Ok(null, "Stock deducted for order"));
        }
        catch (AppException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    // ── Website stock insights ───────────────────────────────────────────────

    /// <summary>
    /// Stock overview for the admin: totals, out-of-stock / low-stock lists (with whether the website shows the
    /// product as unavailable), products not tracked in inventory, and recent stock-in from the ledger
    /// (GRNs, additions, opening balances, returns) plus recent GRNs.
    /// </summary>
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights([FromQuery] int days = 30, [FromQuery] int limit = 50)
    {
        days = Math.Clamp(days, 1, 365);
        limit = Math.Clamp(limit, 1, 200);
        var since = DateTime.UtcNow.AddDays(-days);
        var manual = await WahSpices.Infrastructure.Services.ProductStockSync.GetManualOutOfStockAsync(_db);

        var tracked = await _db.InventoryItems.AsNoTracking()
            .Where(i => !i.IsDeleted && !i.Product.IsDeleted)
            .Select(i => new
            {
                InventoryItemId = i.Id, i.ProductId, ProductName = (i.Product.Name + (i.Variant != null ? " · " + i.Variant.Weight : "")), ProductSlug = i.Product.Slug,
                i.Product.CategorySlug, i.Product.IsDraft, WebsiteInStock = i.Product.InStock,
                i.Sku, i.CurrentStock, i.ReorderPoint, i.Unit, i.Location, i.UpdatedAt,
            })
            .ToListAsync();

        var trackedIds = tracked.Select(t => t.ProductId).ToHashSet();
        var products = await _db.Products.AsNoTracking().Where(p => !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.Slug, p.CategorySlug, p.IsDraft, p.InStock })
            .ToListAsync();
        var untracked = products.Where(p => !trackedIds.Contains(p.Id)).ToList();

        var outOfStock = tracked.Where(t => t.CurrentStock <= 0)
            .Select(t => new { t.InventoryItemId, t.ProductId, t.ProductName, t.ProductSlug, t.Sku, t.CurrentStock, t.ReorderPoint, t.Unit, t.Location, t.WebsiteInStock, Reason = manual.Contains(t.ProductId) ? "Inventory + manual" : "Inventory", t.UpdatedAt })
            .Concat(products.Where(p => manual.Contains(p.Id) && !(tracked.Any(t => t.ProductId == p.Id && t.CurrentStock <= 0)))
                .Select(p =>
                {
                    var t = tracked.FirstOrDefault(x => x.ProductId == p.Id);
                    return new { InventoryItemId = t?.InventoryItemId ?? 0, ProductId = p.Id, ProductName = p.Name, ProductSlug = p.Slug, Sku = t?.Sku ?? "", CurrentStock = t?.CurrentStock ?? 0, ReorderPoint = t?.ReorderPoint ?? 0, Unit = t?.Unit ?? "", Location = t?.Location ?? "", WebsiteInStock = p.InStock, Reason = "Manual", UpdatedAt = t?.UpdatedAt };
                }))
            .OrderBy(x => x.ProductName).ToList();

        var lowStock = tracked.Where(t => t.CurrentStock > 0 && t.CurrentStock <= t.ReorderPoint)
            .OrderBy(t => t.CurrentStock).ThenBy(t => t.ProductName)
            .Select(t => new { t.InventoryItemId, t.ProductId, t.ProductName, t.ProductSlug, t.Sku, t.CurrentStock, t.ReorderPoint, t.Unit, t.Location, t.WebsiteInStock })
            .ToList();

        var inTypes = new[] { StockAdjustmentType.Addition, StockAdjustmentType.GRNReceipt, StockAdjustmentType.OpeningBalance,
                              StockAdjustmentType.Return, StockAdjustmentType.SaleReversal, StockAdjustmentType.Packing };
        var stockInQ = _db.InventoryLedgers.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CreatedAt >= since && inTypes.Contains(l.Type) && l.Quantity > 0);
        var recentStockIn = await stockInQ.OrderByDescending(l => l.CreatedAt).Take(limit)
            .Select(l => new
            {
                l.Id, l.InventoryItemId, l.InventoryItem.ProductId, ProductName = (l.InventoryItem.Product.Name + (l.InventoryItem.Variant != null ? " · " + l.InventoryItem.Variant.Weight : "")),
                l.InventoryItem.Sku, l.InventoryItem.Unit, Type = l.Type.ToString(), l.Quantity, l.BalanceAfter,
                l.Reason, l.ReferenceId, l.PerformedBy, l.CreatedAt,
            })
            .ToListAsync();
        var stockInByType = await stockInQ.GroupBy(l => l.Type)
            .Select(g => new { Type = g.Key, Entries = g.Count(), Quantity = g.Sum(x => x.Quantity) }).ToListAsync();

        var recentGrns = await _db.GoodsReceiptNotes.AsNoTracking()
            .Where(g => !g.IsDeleted && g.ReceivedAt >= since)
            .OrderByDescending(g => g.ReceivedAt).Take(20)
            .Select(g => new
            {
                g.Id, g.GrnNumber, g.ReceivedAt, g.PurchaseOrderId, g.PurchaseOrder.PoNumber, VendorName = g.PurchaseOrder.Vendor.Name,
                g.InvoiceAmount, Items = g.Items.Count, ReceivedQty = g.Items.Sum(i => (int?)i.ReceivedQty) ?? 0,
                LinkedToInventory = g.Items.Count(i => i.InventoryItemId != null),
            })
            .ToListAsync();

        // Products whose website flag disagrees with the rule (e.g. data from before stock sync existed)
        // (a product may have several rows — product-level + pack sizes — so compare against the product's total)
        var mismatched = tracked.GroupBy(t => t.ProductId)
                           .Count(g => g.First().WebsiteInStock != WahSpices.Infrastructure.Services.ProductStockSync.Effective(manual.Contains(g.Key), g.Sum(t => t.CurrentStock)))
                       + untracked.Count(p => p.InStock != WahSpices.Infrastructure.Services.ProductStockSync.Effective(manual.Contains(p.Id), null));

        return Ok(ApiResponse<object>.Ok(new
        {
            Days = days,
            Totals = new
            {
                Skus = tracked.Count,
                Products = products.Count,
                InStock = tracked.Count(t => t.CurrentStock > t.ReorderPoint),
                LowStock = lowStock.Count,
                OutOfStock = tracked.Count(t => t.CurrentStock <= 0),
                ManualOutOfStock = manual.Count(id => products.Any(p => p.Id == id)),
                WebsiteOutOfStock = products.Count(p => !p.InStock && !p.IsDraft),
                Untracked = untracked.Count,
                StockInEntries = stockInByType.Sum(x => x.Entries),
                StockInQuantity = stockInByType.Sum(x => x.Quantity),
                Mismatched = mismatched,
            },
            OutOfStock = outOfStock,
            LowStock = lowStock,
            Untracked = untracked.OrderBy(p => p.Name).Select(p => new { ProductId = p.Id, ProductName = p.Name, ProductSlug = p.Slug, p.CategorySlug, p.IsDraft, WebsiteInStock = p.InStock, ManualOutOfStock = manual.Contains(p.Id) }),
            StockInByType = stockInByType.Select(x => new { Type = x.Type.ToString(), x.Entries, x.Quantity }),
            RecentStockIn = recentStockIn,
            RecentGrns = recentGrns,
        }));
    }

    /// <summary>Recompute every product's website InStock flag from inventory + manual overrides (repair tool).</summary>
    [HttpPost("sync-stock")]
    public async Task<IActionResult> SyncStock()
    {
        var changed = await WahSpices.Infrastructure.Services.ProductStockSync.SyncAsync(_db);
        return Ok(ApiResponse<object>.Ok(new { Changed = changed },
            changed == 0 ? "Website stock is already in sync with inventory." : $"Updated website stock status for {changed} product(s)."));
    }
}

public record CreateInventoryRequest(string ProductName, string? CategorySlug, string Sku, int CurrentStock, int ReorderPoint, decimal CostPrice, string Unit, string Location, string? BatchNumber, DateTime? ExpiryDate);
public record UpdateInventoryRequest(int? ReorderPoint, decimal? CostPrice, string? Location, string? BatchNumber = null, DateTime? ExpiryDate = null);
public record TransferRequest(int Quantity, string ToLocation, string? Notes);
