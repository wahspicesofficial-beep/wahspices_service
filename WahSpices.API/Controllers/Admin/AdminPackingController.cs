using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Raw materials (bulk stock in kg) and packing runs (raw → finished packets of a product's pack sizes).
/// Finished packets land in the pack-size InventoryItem rows that the website's in-stock flags follow.
/// </summary>
[Route("api/admin/packing")]
[RequireModule("Inventory")]
public class AdminPackingController : AdminBaseController
{
    private static readonly string[] AdjustTypes = { "Adjustment", "Damage" };
    private readonly AppDbContext _db;

    public AdminPackingController(AppDbContext db) => _db = db;

    private IActionResult Fail(PackingException ex) =>
        BadRequest(new { success = false, data = ex.Details, message = ex.Message, error = new { code = ex.Code, message = ex.Message, field = ex.Field } });

    // ── Summary ──────────────────────────────────────────────────────────────

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var raws = await _db.RawMaterials.AsNoTracking().Where(r => !r.IsDeleted && r.IsActive)
            .Select(r => new { r.CurrentStockKg, r.ReorderLevelKg, r.CostPerKg }).ToListAsync();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var month = await _db.PackingEntries.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == "Completed" && p.PackedOn >= monthStart)
            .Select(p => new { p.RawUsedKg, p.PackedKg, p.WastageKg, p.VarianceKg, Packets = p.Lines.Sum(l => l.Packets) }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            rawMaterials = raws.Count,
            rawStockKg = raws.Sum(r => r.CurrentStockKg),
            rawStockValue = Math.Round(raws.Sum(r => r.CurrentStockKg * r.CostPerKg), 2),
            lowStock = raws.Count(r => r.CurrentStockKg <= r.ReorderLevelKg),
            outOfStock = raws.Count(r => r.CurrentStockKg <= 0),
            month = new
            {
                entries = month.Count,
                rawUsedKg = month.Sum(m => m.RawUsedKg),
                packedKg = month.Sum(m => m.PackedKg),
                wastageKg = month.Sum(m => m.WastageKg + Math.Max(0, m.VarianceKg)),
                packets = month.Sum(m => m.Packets),
            },
        }));
    }

    // ── Raw materials ────────────────────────────────────────────────────────

    [RequireModule("Inventory", "Purchase", "Production")]   // picker on purchase GRN / production
    [HttpGet("raw-materials")]
    public async Task<IActionResult> GetRawMaterials([FromQuery] string? search, [FromQuery] bool lowOnly = false, [FromQuery] bool includeInactive = false)
    {
        var q = _db.RawMaterials.AsNoTracking().Where(r => !r.IsDeleted);
        if (!includeInactive) q = q.Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.Name.Contains(s) || r.Code.Contains(s) || (r.Category != null && r.Category.Contains(s)));
        }
        if (lowOnly) q = q.Where(r => r.CurrentStockKg <= r.ReorderLevelKg);
        var list = await q.OrderBy(r => r.Name).Select(r => new
        {
            r.Id, r.Name, r.Code, r.Category, r.DisplayUnit, r.CurrentStockKg, r.ReorderLevelKg, r.CostPerKg,
            StockValue = Math.Round(r.CurrentStockKg * r.CostPerKg, 2), r.HsnCode, r.Location, r.DefaultProductId,
            DefaultProductName = r.DefaultProduct != null ? r.DefaultProduct.Name : null, r.Notes, r.IsActive,
            IsLowStock = r.CurrentStockKg <= r.ReorderLevelKg, r.CreatedAt, r.UpdatedAt,
            LastPackedOn = _db.PackingEntries.Where(p => p.RawMaterialId == r.Id && !p.IsDeleted && p.Status == "Completed")
                .Max(p => (DateTime?)p.PackedOn),
        }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpGet("raw-materials/{id:int}")]
    public async Task<IActionResult> GetRawMaterial(int id)
    {
        var r = await _db.RawMaterials.AsNoTracking().Include(x => x.DefaultProduct).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Raw material not found."));
        return Ok(ApiResponse<object>.Ok(new
        {
            r.Id, r.Name, r.Code, r.Category, r.DisplayUnit, r.CurrentStockKg, r.ReorderLevelKg, r.CostPerKg,
            StockValue = Math.Round(r.CurrentStockKg * r.CostPerKg, 2), r.HsnCode, r.Location, r.DefaultProductId,
            DefaultProductName = r.DefaultProduct?.Name, r.Notes, r.IsActive, IsLowStock = r.CurrentStockKg <= r.ReorderLevelKg,
        }));
    }

    [HttpPost("raw-materials")]
    public async Task<IActionResult> CreateRawMaterial([FromBody] RawMaterialRequest req)
    {
        try
        {
            var err = await ValidateRawAsync(req, null);
            if (err != null) return err;
            var code = string.IsNullOrWhiteSpace(req.Code) ? await PackingRules.NextRawCodeAsync(_db) : req.Code.Trim().ToUpperInvariant();
            var rm = new RawMaterial
            {
                Name = req.Name.Trim(), Code = code, CreatedBy = AdminName,
            };
            ApplyRaw(rm, req);
            _db.RawMaterials.Add(rm);
            var opening = req.OpeningStock ?? 0;
            if (opening < 0) return BadRequest(ApiResponse<object>.Fail("INVALID", "Opening stock cannot be negative.", "openingStock"));
            if (opening > 0)
            {
                var kg = PackingRules.ToKg(opening, req.OpeningUnit ?? "kg", "openingUnit");
                PackingRules.Receive(_db, rm, kg, req.CostPerKg, "Opening", AdminName, reason: "Opening stock", batch: req.BatchNumber);
            }
            await _db.SaveChangesAsync();
            return StatusCode(201, ApiResponse<object>.Ok(new { rm.Id, rm.Code, rm.Name, rm.CurrentStockKg }, $"{rm.Name} added."));
        }
        catch (PackingException ex) { return Fail(ex); }
    }

    [HttpPut("raw-materials/{id:int}")]
    public async Task<IActionResult> UpdateRawMaterial(int id, [FromBody] RawMaterialRequest req)
    {
        try
        {
            var rm = await _db.RawMaterials.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (rm == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Raw material not found."));
            var err = await ValidateRawAsync(req, id);
            if (err != null) return err;
            rm.Name = req.Name.Trim();
            if (!string.IsNullOrWhiteSpace(req.Code)) rm.Code = req.Code.Trim().ToUpperInvariant();
            ApplyRaw(rm, req);
            if (req.IsActive.HasValue) rm.IsActive = req.IsActive.Value;
            rm.UpdatedAt = DateTime.UtcNow;
            rm.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { rm.Id, rm.Code, rm.Name }, "Saved. (Stock changes go through Adjust / GRN / Packing.)"));
        }
        catch (PackingException ex) { return Fail(ex); }
    }

    [HttpDelete("raw-materials/{id:int}")]
    public async Task<IActionResult> DeleteRawMaterial(int id)
    {
        var rm = await _db.RawMaterials.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (rm == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Raw material not found."));
        if (rm.CurrentStockKg > 0)
            return Conflict(ApiResponse<object>.Fail("HAS_STOCK", $"{rm.Name} still has {PackingRules.FormatKg(rm.CurrentStockKg)} in stock. Adjust it to zero (or mark it inactive) first."));
        rm.IsDeleted = true;
        rm.IsActive = false;
        rm.UpdatedAt = DateTime.UtcNow;
        rm.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Raw material removed."));
    }

    /// <summary>Manual raw stock correction: direction in/out, Adjustment or Damage, quantity in kg or g.</summary>
    [HttpPost("raw-materials/{id:int}/adjust")]
    public async Task<IActionResult> AdjustRawMaterial(int id, [FromBody] RawAdjustRequest req)
    {
        try
        {
            if (req.Quantity <= 0) return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Quantity must be greater than zero.", "quantity"));
            var type = AdjustTypes.FirstOrDefault(t => t.Equals(req.Type ?? "Adjustment", StringComparison.OrdinalIgnoreCase));
            if (type == null) return BadRequest(ApiResponse<object>.Fail("INVALID_TYPE", "Type must be Adjustment or Damage.", "type"));
            var stockIn = string.Equals(req.Direction, "in", StringComparison.OrdinalIgnoreCase);
            if (!stockIn && !string.Equals(req.Direction, "out", StringComparison.OrdinalIgnoreCase))
                return BadRequest(ApiResponse<object>.Fail("INVALID_DIRECTION", "Direction must be 'in' or 'out'.", "direction"));
            if (type == "Damage" && stockIn) return BadRequest(ApiResponse<object>.Fail("INVALID_DIRECTION", "Damage always takes stock out.", "direction"));
            if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Give a reason for the adjustment.", "reason"));
            var kg = PackingRules.ToKg(req.Quantity, req.Unit, "unit");
            if (kg <= 0) return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Quantity is below 1 gram.", "quantity"));

            var strategy = _db.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                var rm = await LockRawAsync(id);
                if (rm == null) return (object?)null;
                if (stockIn) PackingRules.Receive(_db, rm, kg, req.CostPerKg, type, AdminName, batch: req.BatchNumber, reason: req.Reason);
                else PackingRules.Move(_db, rm, -kg, type, AdminName, batch: req.BatchNumber, reason: req.Reason, unitCostPerKg: rm.CostPerKg);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return new { rm.Id, rm.CurrentStockKg, IsLowStock = rm.CurrentStockKg <= rm.ReorderLevelKg, rm.CostPerKg };
            });
            if (result == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Raw material not found."));
            return Ok(ApiResponse<object>.Ok(result, "Raw stock updated."));
        }
        catch (PackingException ex) { return Fail(ex); }
    }

    /// <summary>Raw-material ledger (all materials or one), newest first.</summary>
    [HttpGet("raw-ledger")]
    public async Task<IActionResult> RawLedger([FromQuery] int? rawMaterialId, [FromQuery] string? type, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 500);
        var q = _db.RawMaterialLedgers.AsNoTracking().Where(l => !l.IsDeleted);
        if (rawMaterialId.HasValue) q = q.Where(l => l.RawMaterialId == rawMaterialId.Value);
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(l => l.Type == type);
        if (from.HasValue) q = q.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(l => l.CreatedAt < to.Value.Date.AddDays(1));
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).Skip((page - 1) * limit).Take(limit)
            .Select(l => new
            {
                l.Id, l.RawMaterialId, RawMaterialName = l.RawMaterial.Name, RawMaterialCode = l.RawMaterial.Code, l.Type,
                l.QuantityKg, l.BalanceAfterKg, l.UnitCostPerKg, l.ReferenceType, l.ReferenceId, l.BatchNumber, l.Reason,
                l.PerformedBy, l.CreatedAt,
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items = rows, pagination = new { total, page, limit } }));
    }

    // ── Finished products for the packing form ───────────────────────────────

    /// <summary>Products with their pack sizes and current finished stock per pack size.</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products()
    {
        var products = await _db.Products.AsNoTracking().Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Weight, p.IsDraft, p.InStock, p.CategorySlug,
                Variants = p.Variants.Where(v => !v.IsDeleted).OrderBy(v => v.WeightGrams)
                    .Select(v => new { v.Id, v.Weight, v.WeightGrams, v.Sku, v.InStock }).ToList(),
            }).ToListAsync();
        var stock = await _db.InventoryItems.AsNoTracking().Where(i => !i.IsDeleted)
            .Select(i => new { i.Id, i.ProductId, i.VariantId, i.CurrentStock, i.Sku }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(products.Select(p => new
        {
            p.Id, p.Name, p.Weight, p.IsDraft, p.InStock, p.CategorySlug,
            ProductLevelStock = stock.Where(s => s.ProductId == p.Id && s.VariantId == null).Sum(s => (int?)s.CurrentStock),
            Variants = p.Variants.Select(v => new
            {
                v.Id, v.Weight, WeightGrams = v.WeightGrams > 0 ? v.WeightGrams : PackingRules.ParseWeightGrams(v.Weight), v.Sku, v.InStock,
                Stock = stock.Where(s => s.VariantId == v.Id).Sum(s => (int?)s.CurrentStock),
            }),
        })));
    }

    // ── Packing entries ──────────────────────────────────────────────────────

    [HttpGet("entries")]
    public async Task<IActionResult> Entries([FromQuery] int? rawMaterialId, [FromQuery] int? productId, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 500);
        var q = _db.PackingEntries.AsNoTracking().Where(p => !p.IsDeleted);
        if (rawMaterialId.HasValue) q = q.Where(p => p.RawMaterialId == rawMaterialId.Value);
        if (productId.HasValue) q = q.Where(p => p.Lines.Any(l => l.ProductId == productId.Value));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.PackingNumber.Contains(s) || (p.BatchNumber != null && p.BatchNumber.Contains(s)) || p.RawMaterial.Name.Contains(s)
                             || (p.PackedBy != null && p.PackedBy.Contains(s)));
        }
        if (from.HasValue) q = q.Where(p => p.PackedOn >= from.Value.Date);
        if (to.HasValue) q = q.Where(p => p.PackedOn < to.Value.Date.AddDays(1));
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(p => p.PackedOn).ThenByDescending(p => p.Id).Skip((page - 1) * limit).Take(limit)
            .Select(p => new
            {
                p.Id, p.PackingNumber, p.RawMaterialId, RawMaterialName = p.RawMaterial.Name, p.RawUsedKg, p.PackedKg, p.WastageKg, p.VarianceKg,
                p.CostPerKg, p.PackedOn, p.BatchNumber, p.PackedBy, p.Notes, p.Status, p.ReversedAt, p.ReversedBy, p.ReversalReason,
                p.CreatedBy, p.CreatedAt,
                Lines = p.Lines.Where(l => !l.IsDeleted).Select(l => new
                {
                    l.Id, l.ProductId, ProductName = l.Product.Name, l.VariantId, l.PackLabel, l.PackWeightGrams, l.Packets, l.TotalKg, l.InventoryItemId,
                }).ToList(),
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items = rows, pagination = new { total, page, limit } }));
    }

    [HttpGet("entries/{id:int}")]
    public async Task<IActionResult> Entry(int id)
    {
        var p = await _db.PackingEntries.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted)
            .Select(p => new
            {
                p.Id, p.PackingNumber, p.RawMaterialId, RawMaterialName = p.RawMaterial.Name, RawMaterialCode = p.RawMaterial.Code,
                p.RawUsedKg, p.PackedKg, p.WastageKg, p.VarianceKg, p.CostPerKg, p.PackedOn, p.BatchNumber, p.PackedBy, p.Notes,
                p.Status, p.ReversedAt, p.ReversedBy, p.ReversalReason, p.CreatedBy, p.CreatedAt,
                Lines = p.Lines.Where(l => !l.IsDeleted).Select(l => new
                {
                    l.Id, l.ProductId, ProductName = l.Product.Name, l.VariantId, l.PackLabel, l.PackWeightGrams, l.Packets, l.TotalKg,
                    l.InventoryItemId, Sku = l.InventoryItem.Sku, CurrentStock = l.InventoryItem.CurrentStock,
                }).ToList(),
            }).FirstOrDefaultAsync();
        if (p == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Packing entry not found."));
        var rawLedger = await _db.RawMaterialLedgers.AsNoTracking().Where(l => l.ReferenceId == p.PackingNumber && !l.IsDeleted)
            .OrderBy(l => l.Id).Select(l => new { l.Id, l.Type, l.QuantityKg, l.BalanceAfterKg, l.Reason, l.PerformedBy, l.CreatedAt }).ToListAsync();
        var stockLedger = await _db.InventoryLedgers.AsNoTracking().Where(l => l.ReferenceId == p.PackingNumber && !l.IsDeleted)
            .OrderBy(l => l.Id).Select(l => new
            {
                l.Id, Type = l.Type.ToString(), l.Quantity, l.BalanceAfter, l.InventoryItemId, l.InventoryItem.Sku,
                ProductName = l.InventoryItem.Product.Name, PackLabel = l.InventoryItem.Variant != null ? l.InventoryItem.Variant.Weight : null,
                l.Reason, l.PerformedBy, l.CreatedAt,
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { entry = p, rawLedger, stockLedger }));
    }

    /// <summary>Validate a packing run and show what will happen, without saving.</summary>
    [HttpPost("entries/preview")]
    public async Task<IActionResult> Preview([FromBody] PackingInput req)
    {
        try
        {
            var plan = await PackingRules.PlanAsync(_db, req);
            return Ok(ApiResponse<object>.Ok(PlanDto(plan)));
        }
        catch (PackingException ex) { return Fail(ex); }
    }

    /// <summary>Record a packing run: raw stock out, finished packets in (one transaction, raw row locked).</summary>
    [HttpPost("entries")]
    public async Task<IActionResult> Create([FromBody] PackingInput req)
    {
        if (req.PackedOn.HasValue && req.PackedOn.Value.Date > DateTime.UtcNow.Date.AddDays(1))
            return BadRequest(ApiResponse<object>.Fail("INVALID_DATE", "Packing date cannot be in the future.", "packedOn"));
        if (req.PackedOn.HasValue && req.PackedOn.Value < DateTime.UtcNow.AddYears(-2))
            return BadRequest(ApiResponse<object>.Fail("INVALID_DATE", "Packing date is too far in the past.", "packedOn"));
        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            var (entry, plan) = await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                var rm = await LockRawAsync(req.RawMaterialId)
                         ?? throw new PackingException("NOT_FOUND", "Raw material not found.", "rawMaterialId");
                var plan = await PackingRules.PlanAsync(_db, req, rm);
                var entry = await PackingRules.ApplyAsync(_db, req, plan, AdminName);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (entry, plan);
            });
            return StatusCode(201, ApiResponse<object>.Ok(new
            {
                entry.Id, entry.PackingNumber, entry.RawUsedKg, entry.PackedKg, entry.WastageKg, entry.VarianceKg,
                RawStockAfterKg = plan.RawMaterial.CurrentStockKg,
                Lines = entry.Lines.Select(l => new { l.ProductId, l.VariantId, l.PackLabel, l.Packets, l.TotalKg, l.InventoryItemId, StockAfter = l.InventoryItem.CurrentStock }),
                plan.Warnings,
            }, $"{entry.PackingNumber} saved — {entry.Lines.Sum(l => l.Packets)} packets added, {PackingRules.FormatKg(entry.RawUsedKg)} {plan.RawMaterial.Name} deducted."));
        }
        catch (PackingException ex) { return Fail(ex); }
    }

    /// <summary>Cancel a packing run: packets leave finished stock, raw kg returns. Refused if the packets were already sold.</summary>
    [HttpPost("entries/{id:int}/reverse")]
    public async Task<IActionResult> Reverse(int id, [FromBody] ReversePackingRequest? req)
    {
        if (string.IsNullOrWhiteSpace(req?.Reason))
            return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Give a reason for reversing this packing entry.", "reason"));
        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            var entry = await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                var head = await _db.PackingEntries.AsNoTracking().Where(p => p.Id == id && !p.IsDeleted).Select(p => new { p.RawMaterialId }).FirstOrDefaultAsync();
                if (head == null) return null;
                await LockRawAsync(head.RawMaterialId);
                var entry = await _db.PackingEntries.Include(p => p.Lines).Include(p => p.RawMaterial).FirstAsync(p => p.Id == id);
                await PackingRules.ReverseAsync(_db, entry, req.Reason, AdminName);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return entry;
            });
            if (entry == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Packing entry not found."));
            return Ok(ApiResponse<object>.Ok(new { entry.Id, entry.PackingNumber, entry.Status, RawStockAfterKg = entry.RawMaterial.CurrentStockKg },
                $"{entry.PackingNumber} reversed — {entry.Lines.Sum(l => l.Packets)} packets removed, {PackingRules.FormatKg(entry.RawUsedKg)} returned to {entry.RawMaterial.Name}."));
        }
        catch (PackingException ex) { return Fail(ex); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Reads the raw-material row with an update lock so concurrent packings can't overdraw it (inside a transaction).</summary>
    private Task<RawMaterial?> LockRawAsync(int id) =>
        _db.RawMaterials.FromSqlInterpolated($"SELECT * FROM RawMaterials WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}")
            .Where(r => !r.IsDeleted).FirstOrDefaultAsync();

    private async Task<IActionResult?> ValidateRawAsync(RawMaterialRequest req, int? id)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("REQUIRED", "Name is required.", "name"));
        if (req.Name.Trim().Length > 150) return BadRequest(ApiResponse<object>.Fail("TOO_LONG", "Name is too long.", "name"));
        if ((req.CostPerKg ?? 0) < 0 || (req.ReorderLevel ?? 0) < 0)
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Cost and reorder level cannot be negative."));
        var name = req.Name.Trim();
        if (await _db.RawMaterials.AnyAsync(r => !r.IsDeleted && r.Name == name && r.Id != (id ?? 0)))
            return Conflict(ApiResponse<object>.Fail("DUPLICATE_NAME", $"A raw material named {name} already exists.", "name"));
        if (!string.IsNullOrWhiteSpace(req.Code))
        {
            var code = req.Code.Trim().ToUpperInvariant();
            if (code.Length > 32) return BadRequest(ApiResponse<object>.Fail("TOO_LONG", "Code is too long.", "code"));
            if (await _db.RawMaterials.AnyAsync(r => r.Code == code && r.Id != (id ?? 0)))
                return Conflict(ApiResponse<object>.Fail("DUPLICATE_CODE", $"Code {code} is already used.", "code"));
        }
        if (req.DefaultProductId.HasValue && !await _db.Products.AnyAsync(p => p.Id == req.DefaultProductId.Value && !p.IsDeleted))
            return BadRequest(ApiResponse<object>.Fail("PRODUCT_NOT_FOUND", "Default product not found.", "defaultProductId"));
        var unit = (req.DisplayUnit ?? "kg").Trim().ToLowerInvariant();
        if (!PackingRules.Units.Contains(unit)) return BadRequest(ApiResponse<object>.Fail("INVALID_UNIT", "Display unit must be kg or g.", "displayUnit"));
        return null;
    }

    private static void ApplyRaw(RawMaterial rm, RawMaterialRequest req)
    {
        rm.Category = string.IsNullOrWhiteSpace(req.Category) ? null : req.Category.Trim();
        rm.DisplayUnit = (req.DisplayUnit ?? "kg").Trim().ToLowerInvariant();
        rm.ReorderLevelKg = PackingRules.ToKg(req.ReorderLevel ?? 0, req.ReorderUnit ?? "kg", "reorderUnit");
        if (req.CostPerKg.HasValue) rm.CostPerKg = Math.Round(req.CostPerKg.Value, 2);
        rm.HsnCode = string.IsNullOrWhiteSpace(req.HsnCode) ? null : req.HsnCode.Trim();
        rm.Location = string.IsNullOrWhiteSpace(req.Location) ? "Warehouse A" : req.Location.Trim();
        rm.DefaultProductId = req.DefaultProductId;
        rm.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
    }

    private static object PlanDto(PackingPlan p) => new
    {
        RawMaterialId = p.RawMaterial.Id, RawMaterialName = p.RawMaterial.Name,
        p.RawUsedKg, p.PackedKg, p.WastageKg, p.VarianceKg, p.ToleranceKg, p.RawStockBeforeKg, p.RawStockAfterKg,
        Lines = p.Lines.Select(l => new
        {
            ProductId = l.Product.Id, ProductName = l.Product.Name, VariantId = l.Variant?.Id, l.PackLabel, l.PackWeightGrams,
            l.Packets, l.TotalKg, StockBefore = l.CurrentStock, StockAfter = l.CurrentStock + l.Packets,
        }),
        p.Warnings,
    };
}

public record RawMaterialRequest(
    string Name, string? Code, string? Category, string? DisplayUnit,
    decimal? OpeningStock, string? OpeningUnit, decimal? ReorderLevel, string? ReorderUnit,
    decimal? CostPerKg, string? HsnCode, string? Location, int? DefaultProductId, string? Notes,
    string? BatchNumber, bool? IsActive);

public record RawAdjustRequest(string Direction, string? Type, decimal Quantity, string? Unit, string? Reason, string? BatchNumber, decimal? CostPerKg);

public record ReversePackingRequest(string? Reason);
