using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WahSpices.API.Controllers.Admin;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

[Route("api/admin/barcodes")]
[RequireModule("Barcode")]
public class AdminBarcodeController : AdminBaseController
{
    private readonly AppDbContext _db;
    private new string AdminName => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    public AdminBarcodeController(AppDbContext db) => _db = db;

    // GET /api/admin/barcodes/products — list products with inventory SKU info
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts([FromQuery] string? search)
    {
        try
        {
            var q = _db.InventoryItems
                .Include(i => i.Product)
                .Where(i => !i.IsDeleted && !i.Product.IsDeleted);

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(i => i.Product.Name.Contains(search) || i.Sku.Contains(search));

            var rows = await q.OrderBy(i => i.Product.Name).Select(i => new
            {
                i.Id,
                i.ProductId,
                productName = i.Product.Name,
                i.Sku,
                mrp    = i.Product.Mrp,
                weight = i.Product.Weight,
                i.BatchNumber,
                i.ExpiryDate,
                i.ManufactureDate,
                i.CurrentStock,
                i.Unit,
                IsLowStock = i.CurrentStock <= i.ReorderPoint,
            }).ToListAsync();

            // Also include products without inventory items (use slug as SKU)
            var withInventory = rows.Select(r => r.ProductId).ToHashSet();
            var noInvQ = _db.Products.Where(p => !p.IsDeleted && !withInventory.Contains(p.Id));
            if (!string.IsNullOrWhiteSpace(search))
                noInvQ = noInvQ.Where(p => p.Name.Contains(search));

            var noInv = await noInvQ.Select(p => new
            {
                Id         = 0,
                ProductId  = p.Id,
                productName = p.Name,
                Sku        = p.Slug.ToUpper(),
                mrp        = p.Mrp,
                weight     = p.Weight,
                BatchNumber = (string?)null,
                ExpiryDate  = (DateTime?)null,
                ManufactureDate = (DateTime?)null,
                CurrentStock = 0,
                Unit         = "pcs",
                IsLowStock   = false,
            }).ToListAsync();

            // One row per SKU: the same SKU can be stocked at several locations
            var bySku = rows
                .GroupBy(r => r.Sku)
                .Select(g => g.First() with
                {
                    CurrentStock = g.Sum(x => x.CurrentStock),
                    IsLowStock   = g.All(x => x.IsLowStock),
                })
                .ToList();

            return Ok(ApiResponse<object>.Ok(new { items = bySku.Concat(noInv).OrderBy(x => x.productName) }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // POST /api/admin/barcodes/scan — lookup by barcode/SKU, log it
    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] ScanRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.BarcodeValue))
                return BadRequest(ApiResponse<object>.Fail("REQUIRED", "BarcodeValue required."));

            // Try to find matching inventory item by SKU
            var item = await _db.InventoryItems
                .Include(i => i.Product)
                .Where(i => !i.IsDeleted && i.Sku.ToUpper() == req.BarcodeValue.ToUpper())
                .FirstOrDefaultAsync();

            // Fallback: match by product slug
            Product? product = item?.Product;
            if (product == null)
            {
                var slug = req.BarcodeValue.ToLower().Replace(" ", "-");
                product = await _db.Products.FirstOrDefaultAsync(p =>
                    !p.IsDeleted && (p.Slug == slug || p.Slug == req.BarcodeValue.ToLower()));
            }

            var log = new BarcodeLog
            {
                ProductId    = product?.Id ?? item?.ProductId,
                ProductName  = product?.Name ?? item?.Product?.Name ?? "Unknown",
                Sku          = item?.Sku ?? req.BarcodeValue.ToUpper(),
                BarcodeValue = req.BarcodeValue,
                BarcodeType  = req.BarcodeType ?? "Code128",
                Action       = "Scan",
                Quantity     = 1,
                Notes        = req.Notes,
                CreatedBy    = AdminName,
            };
            _db.BarcodeLogs.Add(log);
            await _db.SaveChangesAsync();

            if (product == null && item == null)
                return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"No product found for '{req.BarcodeValue}'."));

            return Ok(ApiResponse<object>.Ok(new
            {
                found   = true,
                productId   = product?.Id ?? item?.ProductId,
                productName = product?.Name ?? item?.Product?.Name,
                sku         = item?.Sku ?? req.BarcodeValue.ToUpper(),
                mrp         = product?.Mrp,
                weight      = product?.Weight,
                currentStock = item?.CurrentStock,
                unit         = item?.Unit,
                batchNumber  = item?.BatchNumber,
                expiryDate   = item?.ExpiryDate,
                logId        = log.Id,
            }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // POST /api/admin/barcodes/generate — log a generation event
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest req)
    {
        try
        {
            var product = req.ProductId.HasValue
                ? await _db.Products.FindAsync(req.ProductId.Value)
                : null;

            var item = req.ProductId.HasValue
                ? await _db.InventoryItems.Where(i => i.ProductId == req.ProductId.Value && !i.IsDeleted).OrderBy(i => i.VariantId.HasValue).FirstOrDefaultAsync()
                : null;

            var sku = item?.Sku ?? product?.Slug?.ToUpper() ?? req.Sku ?? "MANUAL";

            var log = new BarcodeLog
            {
                ProductId    = product?.Id,
                ProductName  = product?.Name ?? req.ProductName ?? "Manual",
                Sku          = sku,
                BarcodeValue = req.BarcodeValue ?? sku,
                BarcodeType  = req.BarcodeType ?? "Code128",
                Action       = "Generate",
                Quantity     = req.Quantity > 0 ? req.Quantity : 1,
                Notes        = req.Notes,
                CreatedBy    = AdminName,
            };
            _db.BarcodeLogs.Add(log);
            await _db.SaveChangesAsync();

            return Ok(ApiResponse<object>.Ok(new
            {
                logId        = log.Id,
                barcodeValue = log.BarcodeValue,
                barcodeType  = log.BarcodeType,
                productName  = log.ProductName,
                sku          = log.Sku,
                quantity     = log.Quantity,
                mrp          = product?.Mrp,
                weight       = product?.Weight,
                batch        = item?.BatchNumber,
                expiryDate   = item?.ExpiryDate,
                manufactureDate = item?.ManufactureDate,
            }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // POST /api/admin/barcodes/print — log a print event
    [HttpPost("print")]
    public async Task<IActionResult> Print([FromBody] PrintRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Sku) && string.IsNullOrWhiteSpace(req.BarcodeValue))
                return BadRequest(ApiResponse<object>.Fail("REQUIRED", "SKU or barcode value is required."));
            if (req.Quantity > 1000)
                return BadRequest(ApiResponse<object>.Fail("INVALID_QUANTITY", "Print at most 1000 labels at a time.", "quantity"));
            var log = new BarcodeLog
            {
                ProductId    = req.ProductId,
                ProductName  = req.ProductName ?? string.Empty,
                Sku          = req.Sku ?? string.Empty,
                BarcodeValue = req.BarcodeValue ?? req.Sku ?? string.Empty,
                BarcodeType  = req.BarcodeType ?? "Code128",
                Action       = "Print",
                Quantity     = req.Quantity > 0 ? req.Quantity : 1,
                Notes        = req.Notes,
                CreatedBy    = AdminName,
            };
            _db.BarcodeLogs.Add(log);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { logId = log.Id }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // GET /api/admin/barcodes/stats — today's counts (IST day) + all-time totals
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var ist        = TimeSpan.FromHours(5.5);
        var todayStart = DateTime.SpecifyKind((DateTime.UtcNow + ist).Date - ist, DateTimeKind.Utc);
        var logs       = _db.BarcodeLogs.Where(b => !b.IsDeleted);
        var today      = logs.Where(b => b.CreatedAt >= todayStart);

        return Ok(ApiResponse<object>.Ok(new
        {
            scansToday          = await today.CountAsync(b => b.Action == "Scan"),
            generatedToday      = await today.CountAsync(b => b.Action == "Generate"),
            labelsPrintedToday  = await today.Where(b => b.Action == "Print").SumAsync(b => (int?)b.Quantity) ?? 0,
            totalScans          = await logs.CountAsync(b => b.Action == "Scan"),
            totalGenerated      = await logs.CountAsync(b => b.Action == "Generate"),
            totalLabelsPrinted  = await logs.Where(b => b.Action == "Print").SumAsync(b => (int?)b.Quantity) ?? 0,
            since               = todayStart,
        }));
    }

    // GET /api/admin/barcodes/history — scan/generate/print history
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? action,
        [FromQuery] string? sku,
        [FromQuery] int limit = 50)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 1000);
            var q = _db.BarcodeLogs.Where(b => !b.IsDeleted);
            if (!string.IsNullOrWhiteSpace(action))
                q = q.Where(b => b.Action == action);
            if (!string.IsNullOrWhiteSpace(sku))
                q = q.Where(b => b.Sku.Contains(sku) || b.BarcodeValue.Contains(sku));

            var rows = await q.OrderByDescending(b => b.CreatedAt)
                .Take(limit)
                .Select(b => new
                {
                    b.Id,
                    b.ProductId,
                    b.ProductName,
                    b.Sku,
                    b.BarcodeValue,
                    b.BarcodeType,
                    b.Action,
                    b.Quantity,
                    b.Notes,
                    b.CreatedAt,
                    b.CreatedBy,
                }).ToListAsync();

            return Ok(ApiResponse<object>.Ok(new { items = rows }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // DELETE /api/admin/barcodes/history/{id}
    [HttpDelete("history/{id:int}")]
    public async Task<IActionResult> DeleteLog(int id)
    {
        try
        {
            var log = await _db.BarcodeLogs.FindAsync(id);
            if (log == null || log.IsDeleted)
                return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Log not found."));
            log.IsDeleted = true;
            log.UpdatedAt = DateTime.UtcNow;
            log.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { deleted = true }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }
}

// ── Request models ──────────────────────────────────────────────────────────
public record ScanRequest(string BarcodeValue, string? BarcodeType, string? Notes);
public record GenerateRequest(int? ProductId, string? ProductName, string? Sku, string? BarcodeValue, string? BarcodeType, int Quantity, string? Notes);
public record PrintRequest(int? ProductId, string? ProductName, string? Sku, string? BarcodeValue, string? BarcodeType, int Quantity, string? Notes);
