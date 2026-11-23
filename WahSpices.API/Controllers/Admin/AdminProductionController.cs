using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/production")]
[RequireModule("Production")]
public class AdminProductionController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminProductionController(AppDbContext db) => _db = db;

    // ── Products / Inventory helpers ──────────────────────────

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        var products = await _db.Products
            .Where(p => !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.CategorySlug, p.Weight, p.Slug })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(products));
    }

    [HttpGet("inventory-summary")]
    public async Task<IActionResult> GetInventorySummary()
    {
        var items = await _db.InventoryItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted)
            .Select(i => new
            {
                i.Id, i.Sku, i.ProductId,
                ProductName = i.Product.Name,
                i.CurrentStock, i.ReorderPoint, i.Unit,
                IsLowStock = i.CurrentStock <= i.ReorderPoint
            })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { Items = items, LowStockCount = items.Count(i => i.IsLowStock) }));
    }

    // ── BOM ───────────────────────────────────────────────────

    [HttpGet("bom")]
    public async Task<IActionResult> GetBom([FromQuery] string? search)
    {
        var q = _db.BomRecipes
            .Include(b => b.Ingredients.Where(i => !i.IsDeleted))
            .Where(b => !b.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(b => b.ProductName.Contains(search));

        var bom = await q
            .OrderBy(b => b.ProductName)
            .Select(b => new
            {
                b.Id, b.ProductName, b.Description, b.ExpectedYield,
                b.ProductId, b.CreatedAt,
                // Include() filters are ignored by projections, so filter soft-deleted rows here
                Ingredients = b.Ingredients.Where(i => !i.IsDeleted).Select(i => new
                {
                    i.Id, i.RawMaterialName, i.QuantityPer, i.Unit, i.CostPerUnit
                }),
                TotalCost = b.Ingredients.Where(i => !i.IsDeleted).Sum(i => i.QuantityPer * (i.CostPerUnit ?? 0))
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(bom));
    }

    [HttpGet("bom/{id:int}")]
    public async Task<IActionResult> GetBomById(int id)
    {
        var bom = await _db.BomRecipes
            .Include(b => b.Ingredients.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted);

        if (bom == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "BOM recipe not found."));

        return Ok(ApiResponse<object>.Ok(new
        {
            bom.Id, bom.ProductName, bom.Description, bom.ExpectedYield, bom.ProductId,
            Ingredients = bom.Ingredients.Select(i => new
            {
                i.Id, i.RawMaterialName, i.QuantityPer, i.Unit, i.CostPerUnit
            }),
            TotalCost = bom.Ingredients.Sum(i => i.QuantityPer * (i.CostPerUnit ?? 0))
        }));
    }

    [HttpPost("bom")]
    public async Task<IActionResult> CreateBom([FromBody] BomRequest req)
    {
        var err = ValidateBom(req);
        if (err != null) return BadRequest(err);
        var recipe = new BomRecipe
        {
            ProductName = req.ProductName,
            Description = req.Description,
            ExpectedYield = req.ExpectedYield,
            ProductId = req.ProductId,
            CreatedBy = AdminName
        };
        _db.BomRecipes.Add(recipe);
        await _db.SaveChangesAsync();

        if (req.Ingredients?.Any() == true)
        {
            foreach (var ing in req.Ingredients)
            {
                _db.BomIngredients.Add(new BomIngredient
                {
                    BomRecipeId = recipe.Id,
                    RawMaterialName = ing.RawMaterialName,
                    QuantityPer = ing.QuantityPer,
                    Unit = ing.Unit ?? "kg",
                    CostPerUnit = ing.CostPerUnit,
                    CreatedBy = AdminName
                });
            }
            await _db.SaveChangesAsync();
        }

        return StatusCode(201, ApiResponse<object>.Ok(new { recipe.Id, recipe.ProductName }, "BOM recipe created."));
    }

    [HttpPut("bom/{id:int}")]
    public async Task<IActionResult> UpdateBom(int id, [FromBody] BomRequest req)
    {
        var recipe = await _db.BomRecipes
            .Include(b => b.Ingredients)
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted);
        if (recipe == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "BOM recipe not found."));
        var err = ValidateBom(req);
        if (err != null) return BadRequest(err);

        recipe.ProductName = req.ProductName;
        recipe.Description = req.Description;
        recipe.ExpectedYield = req.ExpectedYield;
        recipe.ProductId = req.ProductId;
        recipe.UpdatedAt = DateTime.UtcNow;
        recipe.UpdatedBy = AdminName;

        // Replace ingredients
        foreach (var ing in recipe.Ingredients) ing.IsDeleted = true;
        if (req.Ingredients?.Any() == true)
        {
            foreach (var ing in req.Ingredients)
            {
                _db.BomIngredients.Add(new BomIngredient
                {
                    BomRecipeId = recipe.Id,
                    RawMaterialName = ing.RawMaterialName,
                    QuantityPer = ing.QuantityPer,
                    Unit = ing.Unit ?? "kg",
                    CostPerUnit = ing.CostPerUnit,
                    CreatedBy = AdminName
                });
            }
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { recipe.Id }, "BOM recipe updated."));
    }

    [HttpDelete("bom/{id:int}")]
    public async Task<IActionResult> DeleteBom(int id)
    {
        var recipe = await _db.BomRecipes.FindAsync(id);
        if (recipe == null || recipe.IsDeleted)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "BOM recipe not found."));
        recipe.IsDeleted = true;
        recipe.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Deleted."));
    }

    // ── Production Orders ─────────────────────────────────────

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] string? status,
        [FromQuery] string? shift,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var q = _db.ProductionOrders.Where(o => !o.IsDeleted).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(shift))
            q = q.Where(o => o.Shift == shift);
        if (dateFrom.HasValue) q = q.Where(o => o.PlannedDate >= dateFrom);
        if (dateTo.HasValue)   q = q.Where(o => o.PlannedDate <= dateTo);

        var total = await q.CountAsync();
        var orders = await q
            .OrderByDescending(o => o.PlannedDate)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.ProductName, o.ProductId, o.BomRecipeId,
                o.TargetQuantity, o.ActualQuantity, o.WastageQuantity, o.Unit,
                o.Shift, o.PlannedDate, o.StartedAt, o.CompletedAt, o.Status,
                o.TotalCost, o.CostPerUnit, o.YieldPercentage, o.Notes, o.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Orders = orders, Pagination = new { total, page, limit } }));
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] ProductionOrderRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ProductName))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Product name is required.", "productName"));
        if (req.TargetQuantity <= 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Target quantity must be greater than zero.", "targetQuantity"));
        if (req.BomRecipeId.HasValue && !await _db.BomRecipes.AnyAsync(b => b.Id == req.BomRecipeId && !b.IsDeleted))
            return BadRequest(ApiResponse<object>.Fail("NOT_FOUND", "BOM recipe not found.", "bomRecipeId"));
        var prefix = $"PRD-{DateTime.UtcNow:yyyyMMdd}-";
        var todays = await _db.ProductionOrders.IgnoreQueryFilters().Where(o => o.OrderNumber.StartsWith(prefix)).Select(o => o.OrderNumber).ToListAsync();
        var seq = todays.Select(n => int.TryParse(n[prefix.Length..], out var k) ? k : 0).DefaultIfEmpty(0).Max() + 1;
        var orderNumber = $"{prefix}{seq:D3}";

        var order = new ProductionOrder
        {
            OrderNumber = orderNumber,
            ProductName = req.ProductName,
            ProductId = req.ProductId,
            BomRecipeId = req.BomRecipeId,
            TargetQuantity = req.TargetQuantity,
            Unit = req.Unit ?? "kg",
            Shift = req.Shift ?? "Morning",
            PlannedDate = req.PlannedDate,
            Notes = req.Notes,
            Status = "Planned",
            CreatedBy = AdminName
        };
        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync();

        return StatusCode(201, ApiResponse<object>.Ok(new { order.Id, order.OrderNumber }, "Production order created."));
    }

    [HttpPut("orders/{id:int}/status")]
    public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] ProductionStatusRequest req)
    {
        var order = await _db.ProductionOrders.FindAsync(id);
        if (order == null || order.IsDeleted)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found."));
        var status = OrderStatuses.FirstOrDefault(x => string.Equals(x, req.Status, StringComparison.OrdinalIgnoreCase));
        if (status == null)
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Status must be one of: {string.Join(", ", OrderStatuses)}.", "status"));
        if ((order.Status is "Completed" or "Cancelled") && status != order.Status)
            return BadRequest(ApiResponse<object>.Fail("INVALID_TRANSITION", $"A {order.Status} order can't be changed."));
        if (req.ActualQuantity < 0 || req.WastageQuantity < 0 || req.TotalCost < 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Quantities and cost cannot be negative."));
        req = req with { Status = status };

        order.Status = req.Status;
        if (req.Status == "InProgress" && order.StartedAt == null)
            order.StartedAt = DateTime.UtcNow;
        if (req.Status == "Completed")
        {
            order.CompletedAt = DateTime.UtcNow;
            if (req.ActualQuantity.HasValue)
            {
                order.ActualQuantity = req.ActualQuantity.Value;
                order.YieldPercentage = order.TargetQuantity > 0
                    ? Math.Round(req.ActualQuantity.Value / order.TargetQuantity * 100, 2)
                    : 0;
            }
            if (req.WastageQuantity.HasValue) order.WastageQuantity = req.WastageQuantity.Value;
            if (req.TotalCost.HasValue)
            {
                order.TotalCost = req.TotalCost.Value;
                order.CostPerUnit = order.ActualQuantity > 0
                    ? Math.Round(req.TotalCost.Value / order.ActualQuantity, 2)
                    : 0;
            }
        }
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { order.Id, order.Status }, "Order status updated."));
    }

    [HttpDelete("orders/{id:int}")]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var order = await _db.ProductionOrders.FindAsync(id);
        if (order == null || order.IsDeleted)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found."));
        order.IsDeleted = true;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Deleted."));
    }

    // ── Production Batches (FSSAI) ────────────────────────────

    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches([FromQuery] int? orderId, [FromQuery] string? status)
    {
        var q = _db.ProductionBatches
            .Include(b => b.ProductionOrder)
            .Where(b => !b.IsDeleted)
            .AsQueryable();

        if (orderId.HasValue) q = q.Where(b => b.ProductionOrderId == orderId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(b => b.Status == status);

        var batches = await q
            .OrderByDescending(b => b.ManufactureDate)
            .Select(b => new
            {
                b.Id, b.BatchCode, b.ProductionOrderId,
                OrderNumber = b.ProductionOrder.OrderNumber,
                ProductName = b.ProductionOrder.ProductName,
                b.ManufactureDate, b.ExpiryDate, b.Quantity, b.Unit,
                b.FssaiLicenseNo, b.Status, b.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(batches));
    }

    [HttpPost("batches")]
    public async Task<IActionResult> CreateBatch([FromBody] BatchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.BatchCode))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Batch code is required.", "batchCode"));
        if (req.Quantity <= 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Quantity must be greater than zero.", "quantity"));
        if (req.ExpiryDate.HasValue && req.ExpiryDate.Value.Date < req.ManufactureDate.Date)
            return BadRequest(ApiResponse<object>.Fail("INVALID_DATE", "Expiry date can't be before the manufacture date.", "expiryDate"));
        if (await _db.ProductionBatches.AnyAsync(b => b.BatchCode == req.BatchCode.Trim() && !b.IsDeleted))
            return Conflict(ApiResponse<object>.Fail("DUPLICATE", $"Batch code {req.BatchCode} already exists.", "batchCode"));
        var order = await _db.ProductionOrders.FindAsync(req.ProductionOrderId);
        if (order == null || order.IsDeleted)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Production order not found."));

        var batch = new ProductionBatch
        {
            ProductionOrderId = req.ProductionOrderId,
            BatchCode = req.BatchCode.Trim(),
            ManufactureDate = req.ManufactureDate,
            ExpiryDate = req.ExpiryDate,
            Quantity = req.Quantity,
            Unit = req.Unit ?? "kg",
            FssaiLicenseNo = req.FssaiLicenseNo,
            Status = "Active",
            CreatedBy = AdminName
        };
        _db.ProductionBatches.Add(batch);
        await _db.SaveChangesAsync();

        return StatusCode(201, ApiResponse<object>.Ok(new { batch.Id, batch.BatchCode }, "Batch created."));
    }

    // ── Quality Checks ────────────────────────────────────────

    [HttpGet("quality")]
    public async Task<IActionResult> GetQualityChecks([FromQuery] int? orderId, [FromQuery] string? status)
    {
        var q = _db.QualityChecks
            .Include(c => c.ProductionOrder)
            .Where(c => !c.IsDeleted)
            .AsQueryable();

        if (orderId.HasValue) q = q.Where(c => c.ProductionOrderId == orderId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);

        var checks = await q
            .OrderByDescending(c => c.CheckedAt)
            .Select(c => new
            {
                c.Id, c.ProductionOrderId,
                OrderNumber = c.ProductionOrder.OrderNumber,
                ProductName = c.ProductionOrder.ProductName,
                c.CheckedBy, c.Status, c.Remarks, c.FailureReason, c.CheckedAt, c.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(checks));
    }

    [HttpPost("quality")]
    public async Task<IActionResult> CreateQualityCheck([FromBody] QualityCheckRequest req)
    {
        var order = await _db.ProductionOrders.FindAsync(req.ProductionOrderId);
        if (order == null || order.IsDeleted)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Production order not found."));

        var qcStatus = QcStatuses.FirstOrDefault(x => string.Equals(x, req.Status ?? "Pending", StringComparison.OrdinalIgnoreCase));
        if (qcStatus == null)
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Result must be one of: {string.Join(", ", QcStatuses)}.", "status"));
        if (qcStatus == "Failed" && string.IsNullOrWhiteSpace(req.FailureReason))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Enter the failure reason for a failed check.", "failureReason"));

        var check = new QualityCheck
        {
            ProductionOrderId = req.ProductionOrderId,
            CheckedBy = string.IsNullOrWhiteSpace(req.CheckedBy) ? AdminName : req.CheckedBy,
            Status = qcStatus,
            Remarks = req.Remarks,
            FailureReason = req.FailureReason,
            CheckedAt = DateTime.UtcNow,
            CreatedBy = AdminName
        };
        _db.QualityChecks.Add(check);
        await _db.SaveChangesAsync();

        return StatusCode(201, ApiResponse<object>.Ok(new { check.Id, check.Status }, "Quality check recorded."));
    }

    // ── Wastage Records ───────────────────────────────────────

    [HttpGet("wastage")]
    public async Task<IActionResult> GetWastage([FromQuery] int? orderId)
    {
        var q = _db.WastageRecords
            .Include(w => w.ProductionOrder)
            .Where(w => !w.IsDeleted)
            .AsQueryable();

        if (orderId.HasValue) q = q.Where(w => w.ProductionOrderId == orderId);

        var records = await q
            .OrderByDescending(w => w.RecordedAt)
            .Select(w => new
            {
                w.Id, w.ProductionOrderId,
                OrderNumber = w.ProductionOrder.OrderNumber,
                ProductName = w.ProductionOrder.ProductName,
                w.WastageQty, w.Unit, w.Reason, w.Stage, w.RecordedAt, w.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(records));
    }

    [HttpPost("wastage")]
    public async Task<IActionResult> CreateWastage([FromBody] WastageRequest req)
    {
        var order = await _db.ProductionOrders.FindAsync(req.ProductionOrderId);
        if (order == null || order.IsDeleted)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Production order not found."));
        if (req.WastageQty <= 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Wastage quantity must be greater than zero.", "wastageQty"));
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Reason is required.", "reason"));

        var record = new WastageRecord
        {
            ProductionOrderId = req.ProductionOrderId,
            WastageQty = req.WastageQty,
            Unit = string.IsNullOrWhiteSpace(req.Unit) ? order.Unit : req.Unit,
            Reason = req.Reason.Trim(),
            Stage = req.Stage,
            RecordedAt = DateTime.UtcNow,
            CreatedBy = AdminName
        };
        _db.WastageRecords.Add(record);

        order.WastageQuantity += req.WastageQty;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return StatusCode(201, ApiResponse<object>.Ok(new { record.Id, orderWastage = order.WastageQuantity }, "Wastage recorded."));
    }

    private static readonly string[] OrderStatuses = { "Planned", "InProgress", "Completed", "Cancelled" };
    private static readonly string[] QcStatuses = { "Passed", "Failed", "Pending" };

    private static ApiResponse<object>? ValidateBom(BomRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ProductName)) return ApiResponse<object>.Fail("REQUIRED", "Product name is required.", "productName");
        if (req.ExpectedYield <= 0 || req.ExpectedYield > 100) return ApiResponse<object>.Fail("INVALID_YIELD", "Expected yield must be between 1 and 100%.", "expectedYield");
        if (req.Ingredients?.Any(i => string.IsNullOrWhiteSpace(i.RawMaterialName) || i.QuantityPer <= 0 || i.CostPerUnit < 0) == true)
            return ApiResponse<object>.Fail("INVALID_INGREDIENT", "Each ingredient needs a name and a quantity above zero.", "ingredients");
        return null;
    }

    // ── Reports ───────────────────────────────────────────────

    [HttpGet("reports/shift")]
    public async Task<IActionResult> GetShiftReport([FromQuery] DateTime? date)
    {
        var targetDate = date ?? DateTime.UtcNow.Date;
        var from = targetDate.Date;
        var to = from.AddDays(1);

        var orders = await _db.ProductionOrders
            .Where(o => !o.IsDeleted && o.PlannedDate >= from && o.PlannedDate < to)
            .GroupBy(o => o.Shift)
            .Select(g => new
            {
                Shift = g.Key,
                TotalOrders = g.Count(),
                Completed = g.Count(o => o.Status == "Completed"),
                TotalTarget = g.Sum(o => o.TargetQuantity),
                TotalActual = g.Sum(o => o.ActualQuantity),
                TotalWastage = g.Sum(o => o.WastageQuantity),
                TotalCost = g.Sum(o => o.TotalCost)
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Date = targetDate, Shifts = orders }));
    }

    [HttpGet("reports/yield")]
    public async Task<IActionResult> GetYieldReport([FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo)
    {
        var from = dateFrom ?? DateTime.UtcNow.AddDays(-30);
        var to   = dateTo   ?? DateTime.UtcNow;

        var data = await _db.ProductionOrders
            .Where(o => !o.IsDeleted && o.Status == "Completed" && o.CompletedAt >= from && o.CompletedAt <= to)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.ProductName, o.TargetQuantity, o.ActualQuantity,
                o.WastageQuantity, o.YieldPercentage, o.CostPerUnit, o.CompletedAt
            })
            .OrderByDescending(o => o.CompletedAt)
            .ToListAsync();

        var avgYield = data.Count > 0 ? data.Average(d => d.YieldPercentage) : 0;
        var totalWastage = data.Sum(d => d.WastageQuantity);

        return Ok(ApiResponse<object>.Ok(new { Orders = data, AvgYield = Math.Round(avgYield, 2), TotalWastage = totalWastage }));
    }

    [HttpGet("reports/cost")]
    public async Task<IActionResult> GetCostReport([FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo)
    {
        var from = dateFrom ?? DateTime.UtcNow.AddDays(-30);
        var to   = dateTo   ?? DateTime.UtcNow;

        var data = await _db.ProductionOrders
            .Where(o => !o.IsDeleted && o.Status == "Completed" && o.CompletedAt >= from && o.CompletedAt <= to)
            .GroupBy(o => o.ProductName)
            .Select(g => new
            {
                ProductName = g.Key,
                TotalBatches = g.Count(),
                TotalQuantity = g.Sum(o => o.ActualQuantity),
                TotalCost = g.Sum(o => o.TotalCost),
                AvgCostPerUnit = g.Average(o => o.CostPerUnit)
            })
            .OrderByDescending(g => g.TotalCost)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { Products = data, TotalCost = data.Sum(d => d.TotalCost) }));
    }

    // ── Legacy stock endpoints (kept for backward compatibility) ──

    [HttpPost("stock-deduct")]
    public async Task<IActionResult> DeductStock([FromBody] StockDeductRequest req)
    {
        var item = await _db.InventoryItems.Where(i => i.ProductId == req.ProductId && !i.IsDeleted).OrderBy(i => i.VariantId.HasValue).FirstOrDefaultAsync();   // product-level row first (pack-size rows are filled by packing runs)
        if (item == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Inventory item not found."));
        if (item.CurrentStock < req.Quantity) return BadRequest(ApiResponse<object>.Fail("INSUFFICIENT_STOCK", "Not enough stock."));
        item.CurrentStock -= req.Quantity;
        item.UpdatedAt = DateTime.UtcNow;
        _db.InventoryLedgers.Add(new Domain.Entities.InventoryLedger
        {
            InventoryItemId = item.Id,
            Type = Domain.Enums.StockAdjustmentType.Reduction,
            Quantity = req.Quantity,
            BalanceAfter = item.CurrentStock,
            Reason = req.Remark ?? "Production deduction",
            PerformedBy = AdminName
        });
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { item.Id, item.CurrentStock }, "Stock deducted."));
    }

    [HttpPost("stock-add")]
    public async Task<IActionResult> AddStock([FromBody] StockDeductRequest req)
    {
        var item = await _db.InventoryItems.Where(i => i.ProductId == req.ProductId && !i.IsDeleted).OrderBy(i => i.VariantId.HasValue).FirstOrDefaultAsync();   // product-level row first (pack-size rows are filled by packing runs)
        if (item == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Inventory item not found."));
        item.CurrentStock += req.Quantity;
        item.UpdatedAt = DateTime.UtcNow;
        _db.InventoryLedgers.Add(new Domain.Entities.InventoryLedger
        {
            InventoryItemId = item.Id,
            Type = Domain.Enums.StockAdjustmentType.Addition,
            Quantity = req.Quantity,
            BalanceAfter = item.CurrentStock,
            Reason = req.Remark ?? "Production output",
            PerformedBy = AdminName
        });
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { item.Id, item.CurrentStock }, "Stock added."));
    }
}

// ── Request records ───────────────────────────────────────────

public record StockDeductRequest(int ProductId, int Quantity, string? Remark);

public record IngredientRequest(string RawMaterialName, decimal QuantityPer, string? Unit, decimal? CostPerUnit);

public record BomRequest(
    string ProductName,
    string? Description,
    decimal ExpectedYield,
    int? ProductId,
    List<IngredientRequest>? Ingredients);

public record ProductionOrderRequest(
    string ProductName,
    int? ProductId,
    int? BomRecipeId,
    decimal TargetQuantity,
    string? Unit,
    string? Shift,
    DateTime PlannedDate,
    string? Notes);

public record ProductionStatusRequest(
    string Status,
    decimal? ActualQuantity,
    decimal? WastageQuantity,
    decimal? TotalCost);

public record BatchRequest(
    int ProductionOrderId,
    string BatchCode,
    DateTime ManufactureDate,
    DateTime? ExpiryDate,
    decimal Quantity,
    string? Unit,
    string? FssaiLicenseNo);

public record QualityCheckRequest(
    int ProductionOrderId,
    string? CheckedBy,
    string? Status,
    string? Remarks,
    string? FailureReason);

public record WastageRequest(
    int ProductionOrderId,
    decimal WastageQty,
    string? Unit,
    string Reason,
    string? Stage);
