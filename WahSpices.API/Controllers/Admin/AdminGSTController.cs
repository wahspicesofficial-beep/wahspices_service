using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// GST module: HSN master, ITC register, GSTR-1 / GSTR-3B figures, annual reconciliation,
/// XLSX/CSV exports and the internal "marked as filed" log (we can't file on the GST portal itself —
/// the admin files there, then records the ARN here; we also generate the GSTR-3B summary to upload).
///
/// Output tax = every issued (non-cancelled, non-deleted) invoice dated in the period — GST liability
/// arises on invoicing, not on payment. ITC = eligible ITC entries whose period matches.
/// </summary>
[Route("api/admin/gst")]
[RequireModule("GST")]
public class AdminGSTController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminGSTController(AppDbContext db) => _db = db;

    private static readonly string[] ReturnTypes = { "GSTR3B", "GSTR1" };

    // ── HSN Master ───────────────────────────────────────────────────────────────

    [HttpGet("hsn")]
    public async Task<IActionResult> GetHsn([FromQuery] string? search)
    {
        var q = _db.HsnCodes.Where(h => !h.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(h => h.Code.Contains(search) || h.Description.Contains(search));
        var list = await q.OrderBy(h => h.Code).ToListAsync();
        return Ok(ApiResponse<object>.Ok(list.Select(MapHsn)));
    }

    [HttpPost("hsn")]
    public async Task<IActionResult> AddHsn([FromBody] HsnRequest req)
    {
        var err = ValidateHsn(req, requireCode: true);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", err));
        var code = req.Code!.Trim();
        if (await _db.HsnCodes.AnyAsync(h => h.Code == code && !h.IsDeleted))
            return Conflict(ApiResponse<object>.Fail("CONFLICT", $"HSN code {code} already exists."));
        var h = new HsnCode
        {
            Code         = code,
            Description  = req.Description!.Trim(),
            GstRate      = req.GstRate,
            CessRate     = req.CessRate,
            IsExportLut  = req.IsExportLut,
            IsActive     = req.IsActive ?? true,
            CreatedBy    = AdminName,
        };
        _db.HsnCodes.Add(h);
        await _db.SaveChangesAsync();
        var synced = await WahSpices.Infrastructure.Services.HsnRates.ApplyToProductsAsync(_db, AdminName);
        return StatusCode(201, ApiResponse<object>.Ok(MapHsn(h), $"HSN code added.{SyncNote(synced)}"));
    }

    [HttpPut("hsn/{id:int}")]
    public async Task<IActionResult> UpdateHsn(int id, [FromBody] HsnRequest req)
    {
        var h = await _db.HsnCodes.FindAsync(id);
        if (h == null || h.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "HSN not found."));
        var err = ValidateHsn(req, requireCode: false);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", err));
        h.Description = req.Description!.Trim();
        h.GstRate     = req.GstRate;
        h.CessRate    = req.CessRate;
        h.IsExportLut = req.IsExportLut;
        if (req.IsActive.HasValue) h.IsActive = req.IsActive.Value;
        h.UpdatedAt   = DateTime.UtcNow;
        h.UpdatedBy   = AdminName;
        await _db.SaveChangesAsync();
        var synced = await WahSpices.Infrastructure.Services.HsnRates.ApplyToProductsAsync(_db, AdminName);
        return Ok(ApiResponse<object>.Ok(MapHsn(h), $"HSN code updated.{SyncNote(synced)}"));
    }

    [HttpDelete("hsn/{id:int}")]
    public async Task<IActionResult> DeleteHsn(int id)
    {
        var h = await _db.HsnCodes.FindAsync(id);
        if (h == null || h.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "HSN not found."));
        h.IsDeleted = true; h.UpdatedAt = DateTime.UtcNow; h.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        // products fall back to a broader heading still in the master (e.g. 0910 after 091091 is deleted); otherwise they keep their rate
        var synced = await WahSpices.Infrastructure.Services.HsnRates.ApplyToProductsAsync(_db, AdminName);
        return Ok(ApiResponse<bool>.Ok(true, $"HSN code deleted.{SyncNote(synced)}"));
    }

    private static string SyncNote(int products) =>
        products == 0 ? "" : $" GST rate updated on {products} product{(products == 1 ? "" : "s")}.";

    private static string? ValidateHsn(HsnRequest req, bool requireCode)
    {
        if (requireCode && string.IsNullOrWhiteSpace(req.Code)) return "HSN code is required.";
        if (requireCode && !req.Code!.Trim().All(char.IsDigit)) return "HSN code must contain digits only.";
        if (requireCode && req.Code!.Trim().Length is < 4 or > 8) return "HSN code must be 4 to 8 digits.";
        if (string.IsNullOrWhiteSpace(req.Description)) return "Description is required.";
        if (req.GstRate < 0 || req.GstRate > 100) return "GST rate must be between 0 and 100.";
        if (req.CessRate < 0 || req.CessRate > 100) return "Cess rate must be between 0 and 100.";
        return null;
    }

    private static object MapHsn(HsnCode h) => new
    {
        id          = h.Id,
        code        = h.Code,
        description = h.Description,
        gstRate     = h.GstRate,
        cessRate    = h.CessRate,
        isExportLut = h.IsExportLut,
        isActive    = h.IsActive,
    };

    // ── ITC Register ─────────────────────────────────────────────────────────────

    [HttpGet("itc")]
    public async Task<IActionResult> GetItc([FromQuery] string? period)
    {
        var list = await _db.ItcEntries.Where(e => !e.IsDeleted).OrderByDescending(i => i.CreatedAt).ToListAsync();
        var p = ParsePeriod(period);
        if (p.HasValue) list = list.Where(e => ParsePeriod(e.Period) == p).ToList();
        else if (!string.IsNullOrWhiteSpace(period)) list = list.Where(e => e.Period == period).ToList();
        var total = list.Where(e => e.IsEligible).Sum(i => i.TotalItc);
        return Ok(ApiResponse<object>.Ok(new { total, entries = list.Select(MapItc) }));
    }

    [HttpPost("itc")]
    public async Task<IActionResult> AddItc([FromBody] ItcRequest req)
    {
        var err = ValidateItc(req, out var period);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", err));
        var entry = new ItcEntry { CreatedBy = AdminName, SourceType = "Manual" };
        ApplyItc(entry, req, period!);
        _db.ItcEntries.Add(entry);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(MapItc(entry), "ITC entry added."));
    }

    [HttpPut("itc/{id:int}")]
    public async Task<IActionResult> UpdateItc(int id, [FromBody] ItcRequest req)
    {
        var e = await _db.ItcEntries.FindAsync(id);
        if (e == null || e.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Entry not found."));
        var err = ValidateItc(req, out var period);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", err));
        ApplyItc(e, req, period!);
        e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapItc(e), "ITC entry updated."));
    }

    [HttpDelete("itc/{id:int}")]
    public async Task<IActionResult> DeleteItc(int id)
    {
        var e = await _db.ItcEntries.FindAsync(id);
        if (e == null || e.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Entry not found."));
        e.IsDeleted = true; e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private static string? ValidateItc(ItcRequest req, out string? period)
    {
        period = null;
        var p = ParsePeriod(req.Period);
        if (!p.HasValue) return "Period is required (e.g. \"Jun 2026\").";
        period = PeriodLabel(p.Value.year, p.Value.month);   // stored normalised as "MMM yyyy"
        if (string.IsNullOrWhiteSpace(req.SupplierName)) return "Supplier name is required.";
        if (string.IsNullOrWhiteSpace(req.InvoiceNumber)) return "Supplier invoice number is required.";
        if (!string.IsNullOrWhiteSpace(req.SupplierGstin) && req.SupplierGstin.Trim().Length != 15) return "Supplier GSTIN must be 15 characters.";
        if (req.TaxableAmount < 0 || req.IgstAmount < 0 || req.CgstAmount < 0 || req.SgstAmount < 0) return "Amounts cannot be negative.";
        return null;
    }

    private static void ApplyItc(ItcEntry e, ItcRequest req, string period)
    {
        e.Period        = period;
        e.SupplierName  = req.SupplierName!.Trim();
        e.SupplierGstin = string.IsNullOrWhiteSpace(req.SupplierGstin) ? null : req.SupplierGstin.Trim().ToUpperInvariant();
        e.InvoiceNumber = req.InvoiceNumber!.Trim();
        e.TaxableAmount = req.TaxableAmount;
        e.IgstAmount    = req.IgstAmount;
        e.CgstAmount    = req.CgstAmount;
        e.SgstAmount    = req.SgstAmount;
        e.TotalItc      = req.IgstAmount + req.CgstAmount + req.SgstAmount;
        e.IsEligible    = req.IsEligible;
    }

    private static object MapItc(ItcEntry e) => new
    {
        id            = e.Id,
        period        = e.Period,
        supplierName  = e.SupplierName,
        supplierGstin = e.SupplierGstin,
        invoiceNumber = e.InvoiceNumber,
        taxableAmount = e.TaxableAmount,
        igstAmount    = e.IgstAmount,
        cgstAmount    = e.CgstAmount,
        sgstAmount    = e.SgstAmount,
        totalItc      = e.TotalItc,
        isEligible    = e.IsEligible,
        sourceType    = e.SourceType,
        sourceId      = e.SourceId,
    };

    // ── GST Summary (GSTR-3B data) ────────────────────────────────────────────────

    /// period: "MMM yyyy" / "yyyy-MM" (omit for all time). Also accepts year + month.
    [HttpGet("summary")]
    public async Task<IActionResult> GetGstrSummary([FromQuery] string? period, [FromQuery] int? year, [FromQuery] int? month)
    {
        var p = ParsePeriod(period) ?? (year.HasValue && month is >= 1 and <= 12 ? (year.Value, month.Value) : null);
        if (!string.IsNullOrWhiteSpace(period) && p == null)
            return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", $"Unrecognised period '{period}'. Use e.g. \"Jun 2026\"."));
        var s = await Compute3BAsync(p);
        var filing = p.HasValue
            ? await _db.GstReturnFilings.Where(f => f.ReturnType == "GSTR3B" && f.Year == p.Value.year && f.Month == p.Value.month && f.Status == "Filed")
                .OrderByDescending(f => f.FiledOn).FirstOrDefaultAsync()
            : null;

        return Ok(ApiResponse<object>.Ok(new
        {
            period       = p.HasValue ? PeriodLabel(p.Value.year, p.Value.month) : "All",
            year         = p?.year,
            month        = p?.month,
            s.totalTaxable,
            s.totalCgst,
            s.totalSgst,
            s.totalIgst,
            s.totalTax,
            s.invoiceCount,
            s.zeroRatedTaxable,
            s.nilRatedTaxable,
            s.itcCgst,
            s.itcSgst,
            s.itcIgst,
            s.totalItc,
            s.netPayable,
            filing = filing == null ? null : MapFiling(filing),
        }));
    }

    // ── GSTR-1 Monthly Breakdown ─────────────────────────────────────────────────

    [HttpGet("gstr1")]
    public async Task<IActionResult> GetGstr1([FromQuery] int? year)
    {
        var q = OutputInvoices();
        if (year.HasValue) q = q.Where(i => i.CreatedAt.Year == year.Value);
        var invoices = await q.ToListAsync();

        var rows = invoices
            .GroupBy(i => new { i.CreatedAt.Year, i.CreatedAt.Month })
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Select(g =>
            {
                var b2b = g.Where(i => i.Type == InvoiceType.B2B).ToList();
                var b2c = g.Where(i => i.Type == InvoiceType.B2C).ToList();
                return new
                {
                    period       = PeriodLabel(g.Key.Year, g.Key.Month),
                    year         = g.Key.Year,
                    month        = g.Key.Month,
                    b2bTaxable   = b2b.Sum(i => i.Subtotal),
                    b2bTax       = b2b.Sum(Tax),
                    b2cTaxable   = b2c.Sum(i => i.Subtotal),
                    b2cTax       = b2c.Sum(Tax),
                    totalGst     = g.Sum(Tax),
                    invoiceCount = g.Count(),
                };
            })
            .ToList();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    // ── Annual GST Reconciliation ─────────────────────────────────────────────────

    [HttpGet("reconciliation")]
    public async Task<IActionResult> GetReconciliation([FromQuery] int? year)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var months = await BuildReconAsync(y);
        return Ok(ApiResponse<object>.Ok(new
        {
            year = y,
            months,
            totalOutput  = months.Sum(m => m.outputTax),
            totalInput   = months.Sum(m => m.inputTax),
            totalPayable = months.Sum(m => m.netPayable),
        }));
    }

    // ── Filing log ("Mark as filed") ─────────────────────────────────────────────

    [HttpGet("filings")]
    public async Task<IActionResult> GetFilings([FromQuery] int? year, [FromQuery] string? returnType)
    {
        var q = _db.GstReturnFilings.AsQueryable();
        if (year.HasValue) q = q.Where(f => f.Year == year.Value);
        if (!string.IsNullOrWhiteSpace(returnType)) q = q.Where(f => f.ReturnType == returnType.ToUpper());
        var list = await q.OrderByDescending(f => f.Year).ThenByDescending(f => f.Month).ThenByDescending(f => f.FiledOn).ToListAsync();
        return Ok(ApiResponse<object>.Ok(list.Select(MapFiling)));
    }

    /// Records that the return for a period was filed on the GST portal. Figures are computed here,
    /// not trusted from the client. A second filing for the same period needs force=true and marks the
    /// earlier record "Superseded".
    [HttpPost("filings")]
    public async Task<IActionResult> MarkFiled([FromBody] GstFilingRequest req)
    {
        var type = (req.ReturnType ?? "GSTR3B").Replace("-", "").Trim().ToUpperInvariant();
        if (!ReturnTypes.Contains(type))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Return type must be GSTR3B or GSTR1."));
        if (req.Month is < 1 or > 12 || req.Year < 2017 || req.Year > 2100)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Choose a valid month and year."));
        var now = DateTime.UtcNow;
        if (req.Year > now.Year || (req.Year == now.Year && req.Month > now.Month))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "You can't mark a future period as filed."));
        if (!string.IsNullOrWhiteSpace(req.Arn) && req.Arn.Trim().Length > 64)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "ARN is too long."));

        var existing = await _db.GstReturnFilings
            .Where(f => f.ReturnType == type && f.Year == req.Year && f.Month == req.Month && f.Status == "Filed")
            .ToListAsync();
        if (existing.Count > 0 && !req.Force)
            return Conflict(ApiResponse<object>.Fail("ALREADY_FILED",
                $"{type} for {PeriodLabel(req.Year, req.Month)} was already marked as filed on {existing[0].FiledOn:dd MMM yyyy}" +
                (string.IsNullOrWhiteSpace(existing[0].Arn) ? "." : $" (ARN {existing[0].Arn}).")));

        var s = await Compute3BAsync((req.Year, req.Month));
        foreach (var old in existing) { old.Status = "Superseded"; old.UpdatedAt = now; old.UpdatedBy = AdminName; }

        var f = new GstReturnFiling
        {
            ReturnType   = type,
            Month        = req.Month,
            Year         = req.Year,
            Status       = "Filed",
            FiledOn      = req.FiledOn?.ToUniversalTime() ?? now,
            Arn          = string.IsNullOrWhiteSpace(req.Arn) ? null : req.Arn.Trim(),
            TaxableValue = s.totalTaxable,
            TotalTax     = s.totalTax,
            ItcClaimed   = type == "GSTR3B" ? s.totalItc : 0,
            NetPayable   = type == "GSTR3B" ? s.netPayable : s.totalTax,
            Notes        = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            FiledBy      = AdminName,
            CreatedBy    = AdminName,
        };
        _db.GstReturnFilings.Add(f);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(MapFiling(f), $"{type} for {PeriodLabel(req.Year, req.Month)} marked as filed."));
    }

    [HttpPatch("filings/{id:int}")]
    public async Task<IActionResult> UpdateFiling(int id, [FromBody] GstFilingUpdateRequest req)
    {
        var f = await _db.GstReturnFilings.FindAsync(id);
        if (f == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Filing not found."));
        if (req.Arn != null) f.Arn = string.IsNullOrWhiteSpace(req.Arn) ? null : req.Arn.Trim();
        if (req.Notes != null) f.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        f.UpdatedAt = DateTime.UtcNow; f.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapFiling(f), "Filing updated."));
    }

    /// Undo a "mark as filed" (soft delete).
    [HttpDelete("filings/{id:int}")]
    public async Task<IActionResult> DeleteFiling(int id)
    {
        var f = await _db.GstReturnFilings.FindAsync(id);
        if (f == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Filing not found."));
        f.IsDeleted = true; f.UpdatedAt = DateTime.UtcNow; f.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Filing record removed."));
    }

    private static object MapFiling(GstReturnFiling f) => new
    {
        id           = f.Id,
        returnType   = f.ReturnType,
        month        = f.Month,
        year         = f.Year,
        period       = PeriodLabel(f.Year, f.Month),
        status       = f.Status,
        filedOn      = f.FiledOn,
        arn          = f.Arn,
        taxableValue = f.TaxableValue,
        totalTax     = f.TotalTax,
        itcClaimed   = f.ItcClaimed,
        netPayable   = f.NetPayable,
        notes        = f.Notes,
        filedBy      = f.FiledBy,
    };

    // ── GSTR-3B summary file (to upload / key in on the portal) ──────────────────

    [HttpGet("gstr3b/download")]
    public async Task<IActionResult> DownloadGstr3b([FromQuery] int year, [FromQuery] int month, [FromQuery] string format = "json")
    {
        if (month is < 1 or > 12 || year < 2017) return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "Choose a valid month and year."));
        var s = await Compute3BAsync((year, month));
        var gstin = await CompanyGstinAsync();
        var stem = $"GSTR3B_{year}_{month:D2}";

        if (format.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var wb = new XLWorkbook();
            Write3BSheet(wb, s, gstin, year, month);
            return Xlsx(wb, $"{stem}.xlsx");
        }

        // JSON laid out like the GSTN GSTR-3B schema (sup_details / itc_elg); amounts in ₹, 2 dp.
        var json = new
        {
            gstin = gstin ?? "",
            ret_period = $"{month:D2}{year}",
            sup_details = new
            {
                osup_det = new { txval = R(s.totalTaxable - s.zeroRatedTaxable - s.nilRatedTaxable), iamt = R(s.totalIgst - s.zeroRatedIgst), camt = R(s.totalCgst), samt = R(s.totalSgst), csamt = 0m },
                osup_zero = new { txval = R(s.zeroRatedTaxable), iamt = R(s.zeroRatedIgst), csamt = 0m },
                osup_nil_exmp = new { txval = R(s.nilRatedTaxable) },
                isup_rev = new { txval = 0m, iamt = 0m, camt = 0m, samt = 0m, csamt = 0m },
                osup_nongst = new { txval = 0m },
            },
            itc_elg = new
            {
                itc_avl = new[] { new { ty = "OTH", iamt = R(s.itcIgst), camt = R(s.itcCgst), samt = R(s.itcSgst), csamt = 0m } },
                itc_rev = Array.Empty<object>(),
                itc_net = new { iamt = R(s.itcIgst), camt = R(s.itcCgst), samt = R(s.itcSgst), csamt = 0m },
                itc_inelg = Array.Empty<object>(),
            },
            summary = new
            {
                invoice_count = s.invoiceCount,
                total_output_tax = R(s.totalTax),
                total_itc = R(s.totalItc),
                net_payable = R(s.netPayable),
                generated_at = DateTime.UtcNow,
            },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(json, new JsonSerializerOptions { WriteIndented = true });
        return File(bytes, "application/json", $"{stem}.json");
    }

    // ── Exports ──────────────────────────────────────────────────────────────────

    /// report: gstr1 | itc | hsn | gstr3b | recon · year (required) · month (optional = whole year) · format: xlsx | csv
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string report = "gstr1", [FromQuery] int? year = null,
        [FromQuery] int? month = null, [FromQuery] string format = "xlsx")
    {
        var y = year ?? DateTime.UtcNow.Year;
        if (month.HasValue && month is < 1 or > 12) return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "Month must be 1–12."));
        var csv = format.Equals("csv", StringComparison.OrdinalIgnoreCase);
        if (!csv && !format.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("INVALID_FORMAT", "Format must be xlsx or csv."));
        var label = month.HasValue ? $"{y}_{month:D2}" : $"{y}";
        report = report.Replace("-", "").ToLowerInvariant();

        using var wb = new XLWorkbook();
        switch (report)
        {
            case "gstr1":
            {
                var invoices = await OutputInvoicesFor(y, month).Include(i => i.LineItems).OrderBy(i => i.CreatedAt).ToListAsync();
                var state = await CompanyStateAsync();
                // Invoice register (all outward supplies)
                var reg = wb.Worksheets.Add("Invoices");
                Header(reg, "Invoice No", "Date", "Type", "Customer", "Customer GSTIN", "Place of Supply", "Supply Type", "Taxable Value", "CGST", "SGST", "IGST", "Total Tax", "Invoice Value", "Status");
                var r = 2;
                foreach (var i in invoices)
                {
                    Row(reg, r++, i.InvoiceNumber, i.CreatedAt.ToString("dd-MM-yyyy"), i.Type.ToString(), i.CustomerName, i.CustomerGstin ?? "",
                        PlaceOfSupply(i, state), i.GstType.ToString(), i.Subtotal, i.CgstAmount, i.SgstAmount, i.IgstAmount, Tax(i), i.TotalAmount, i.Status.ToString());
                }
                // B2B (invoice-wise, by rate)
                var b2b = wb.Worksheets.Add("B2B");
                Header(b2b, "GSTIN of Recipient", "Receiver Name", "Invoice Number", "Invoice Date", "Invoice Value", "Place Of Supply", "Rate", "Taxable Value", "IGST", "CGST", "SGST");
                r = 2;
                foreach (var i in invoices.Where(i => i.Type == InvoiceType.B2B))
                    foreach (var g in i.LineItems.Where(l => !l.IsDeleted).GroupBy(l => l.GstRate))
                        Row(b2b, r++, i.CustomerGstin ?? "", i.CustomerName, i.InvoiceNumber, i.CreatedAt.ToString("dd-MMM-yyyy"), i.TotalAmount,
                            PlaceOfSupply(i, state), g.Key, g.Sum(l => l.TaxableAmount), g.Sum(l => l.IgstAmount), g.Sum(l => l.CgstAmount), g.Sum(l => l.SgstAmount));
                // B2CS (summary by place of supply + rate)
                var b2cs = wb.Worksheets.Add("B2CS");
                Header(b2cs, "Type", "Place Of Supply", "Rate", "Taxable Value", "IGST", "CGST", "SGST");
                r = 2;
                foreach (var g in invoices.Where(i => i.Type == InvoiceType.B2C)
                             .SelectMany(i => i.LineItems.Where(l => !l.IsDeleted).Select(l => new { Pos = PlaceOfSupply(i, state), l }))
                             .GroupBy(x => new { x.Pos, x.l.GstRate }).OrderBy(g => g.Key.Pos).ThenBy(g => g.Key.GstRate))
                    Row(b2cs, r++, "OE", g.Key.Pos, g.Key.GstRate, g.Sum(x => x.l.TaxableAmount), g.Sum(x => x.l.IgstAmount), g.Sum(x => x.l.CgstAmount), g.Sum(x => x.l.SgstAmount));
                // HSN summary
                WriteHsnSummary(wb.Worksheets.Add("HSN"), invoices);
                // Documents issued
                var docs = wb.Worksheets.Add("Docs");
                Header(docs, "Nature of Document", "From", "To", "Total Number", "Cancelled");
                var all = await _db.Invoices.Where(i => !i.IsDeleted && i.CreatedAt.Year == y && (!month.HasValue || i.CreatedAt.Month == month))
                    .OrderBy(i => i.Id).Select(i => new { i.InvoiceNumber, i.Status }).ToListAsync();
                if (all.Count > 0)
                    Row(docs, 2, "Invoices for outward supply", all.First().InvoiceNumber, all.Last().InvoiceNumber, all.Count, all.Count(i => i.Status == InvoiceStatus.Cancelled));
                break;
            }
            case "itc":
            {
                var entries = (await _db.ItcEntries.Where(e => !e.IsDeleted).OrderBy(e => e.CreatedAt).ToListAsync())
                    .Where(e => ParsePeriod(e.Period) is { } p && p.year == y && (!month.HasValue || p.month == month)).ToList();
                var ws = wb.Worksheets.Add("ITC Register");
                Header(ws, "Period", "Supplier", "Supplier GSTIN", "Invoice No", "Taxable Value", "IGST", "CGST", "SGST", "Total ITC", "Eligible", "Source");
                var r = 2;
                foreach (var e in entries)
                    Row(ws, r++, e.Period, e.SupplierName, e.SupplierGstin ?? "", e.InvoiceNumber, e.TaxableAmount, e.IgstAmount, e.CgstAmount, e.SgstAmount, e.TotalItc, e.IsEligible ? "Yes" : "No (blocked)", e.SourceType ?? "Manual");
                break;
            }
            case "hsn":
            {
                var invoices = await OutputInvoicesFor(y, month).Include(i => i.LineItems).ToListAsync();
                WriteHsnSummary(wb.Worksheets.Add("HSN Summary"), invoices);
                var master = wb.Worksheets.Add("HSN Master");
                Header(master, "HSN Code", "Description", "GST Rate %", "CGST %", "SGST %", "IGST %", "Cess %", "LUT Export", "Active");
                var r = 2;
                foreach (var h in await _db.HsnCodes.Where(h => !h.IsDeleted).OrderBy(h => h.Code).ToListAsync())
                    Row(master, r++, h.Code, h.Description, h.GstRate, h.GstRate / 2, h.GstRate / 2, h.GstRate, h.CessRate, h.IsExportLut ? "Yes" : "No", h.IsActive ? "Yes" : "No");
                break;
            }
            case "gstr3b":
            {
                if (!month.HasValue) return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "GSTR-3B is monthly — choose a month."));
                Write3BSheet(wb, await Compute3BAsync((y, month.Value)), await CompanyGstinAsync(), y, month.Value);
                break;
            }
            case "recon":
            {
                var months = await BuildReconAsync(y);
                var ws = wb.Worksheets.Add("Reconciliation");
                Header(ws, "Month", "Taxable Value", "Output CGST", "Output SGST", "Output IGST", "Total Output", "Input CGST", "Input SGST", "Input IGST", "Total ITC", "Net Payable", "Invoices");
                var r = 2;
                foreach (var m in months)
                    Row(ws, r++, $"{m.month} {y}", m.taxableValue, m.outputCgst, m.outputSgst, m.outputIgst, m.outputTax, m.inputCgst, m.inputSgst, m.inputIgst, m.inputTax, m.netPayable, m.invoiceCount);
                break;
            }
            default:
                return BadRequest(ApiResponse<object>.Fail("INVALID_REPORT", "Report must be gstr1, itc, hsn, gstr3b or recon."));
        }

        foreach (var ws in wb.Worksheets) ws.Columns().AdjustToContents();
        var stem = $"{report.ToUpperInvariant()}_{label}";
        if (!csv) return Xlsx(wb, $"{stem}.xlsx");

        // CSV = first sheet (the main register) only
        var sheet = wb.Worksheets.First();
        var sb = new StringBuilder();
        foreach (var row in sheet.RowsUsed())
            sb.AppendLine(string.Join(",", row.Cells(1, sheet.LastColumnUsed()?.ColumnNumber() ?? 1).Select(c => Csv(c.GetFormattedString()))));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"{stem}.csv");
    }

    // ── Computation helpers ──────────────────────────────────────────────────────

    private IQueryable<Invoice> OutputInvoices() =>
        _db.Invoices.Where(i => !i.IsDeleted && i.Status != InvoiceStatus.Cancelled);

    private IQueryable<Invoice> OutputInvoicesFor(int year, int? month) =>
        OutputInvoices().Where(i => i.CreatedAt.Year == year && (!month.HasValue || i.CreatedAt.Month == month.Value));

    private static decimal Tax(Invoice i) => i.CgstAmount + i.SgstAmount + i.IgstAmount;
    private static decimal R(decimal v) => Math.Round(v, 2);

    private sealed record Summary3B(
        decimal totalTaxable, decimal totalCgst, decimal totalSgst, decimal totalIgst, decimal totalTax, int invoiceCount,
        decimal zeroRatedTaxable, decimal zeroRatedIgst, decimal nilRatedTaxable,
        decimal itcCgst, decimal itcSgst, decimal itcIgst, decimal totalItc, decimal netPayable);

    private async Task<Summary3B> Compute3BAsync((int year, int month)? p)
    {
        var q = OutputInvoices();
        if (p.HasValue) q = q.Where(i => i.CreatedAt.Year == p.Value.year && i.CreatedAt.Month == p.Value.month);
        var invoices = await q.Include(i => i.LineItems).ToListAsync();

        var totalCgst = invoices.Sum(i => i.CgstAmount);
        var totalSgst = invoices.Sum(i => i.SgstAmount);
        var totalIgst = invoices.Sum(i => i.IgstAmount);
        var totalTax  = totalCgst + totalSgst + totalIgst;
        var exports   = invoices.Where(i => i.GstType == GSTType.Export).ToList();
        var nil       = invoices.Where(i => i.GstType != GSTType.Export)
                                .SelectMany(i => i.LineItems.Where(l => !l.IsDeleted && l.GstRate == 0)).Sum(l => l.TaxableAmount);

        var itc = await _db.ItcEntries.Where(e => e.IsEligible && !e.IsDeleted).ToListAsync();
        if (p.HasValue) itc = itc.Where(e => ParsePeriod(e.Period) == p).ToList();
        var itcCgst = itc.Sum(e => e.CgstAmount);
        var itcSgst = itc.Sum(e => e.SgstAmount);
        var itcIgst = itc.Sum(e => e.IgstAmount);
        var totalItc = itcCgst + itcSgst + itcIgst;

        return new Summary3B(invoices.Sum(i => i.Subtotal), totalCgst, totalSgst, totalIgst, totalTax, invoices.Count,
            exports.Sum(i => i.Subtotal), exports.Sum(i => i.IgstAmount), nil,
            itcCgst, itcSgst, itcIgst, totalItc, Math.Max(0, totalTax - totalItc));
    }

    private sealed record ReconMonth(string month, int monthNumber, decimal taxableValue, decimal outputCgst, decimal outputSgst, decimal outputIgst,
        decimal outputTax, decimal inputCgst, decimal inputSgst, decimal inputIgst, decimal inputTax, decimal netPayable, int invoiceCount);

    private async Task<List<ReconMonth>> BuildReconAsync(int y)
    {
        var invoices = await OutputInvoicesFor(y, null).ToListAsync();
        var allItc = (await _db.ItcEntries.Where(e => !e.IsDeleted && e.IsEligible).ToListAsync())
            .Select(e => new { e, p = ParsePeriod(e.Period) }).Where(x => x.p.HasValue && x.p.Value.year == y).ToList();

        return Enumerable.Range(1, 12).Select(m =>
        {
            var inv = invoices.Where(i => i.CreatedAt.Month == m).ToList();
            var itc = allItc.Where(x => x.p!.Value.month == m).Select(x => x.e).ToList();
            var outputTax = inv.Sum(Tax);
            var inputTax  = itc.Sum(e => e.TotalItc);
            return new ReconMonth(
                CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m), m,
                inv.Sum(i => i.Subtotal), inv.Sum(i => i.CgstAmount), inv.Sum(i => i.SgstAmount), inv.Sum(i => i.IgstAmount), outputTax,
                itc.Sum(e => e.CgstAmount), itc.Sum(e => e.SgstAmount), itc.Sum(e => e.IgstAmount), inputTax,
                Math.Max(0, outputTax - inputTax), inv.Count);
        }).ToList();
    }

    private async Task<string?> CompanyGstinAsync()
    {
        var inv = await _db.InvoiceSettings.Where(s => !s.IsDeleted).Select(s => s.Gstin).FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(inv)) return inv;
        return await _db.SiteSettings.Select(s => s.Gstin).FirstOrDefaultAsync();
    }

    private async Task<string> CompanyStateAsync() =>
        await _db.InvoiceSettings.Where(s => !s.IsDeleted).Select(s => s.State).FirstOrDefaultAsync() ?? "";

    private static string PlaceOfSupply(Invoice i, string companyState)
    {
        foreach (var json in new[] { i.ShippingAddress, i.BillingAddress })
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(st.GetString()))
                    return WahSpices.Application.Common.GstStates.Format(st.GetString());   // "24-Gujarat"
            }
            catch (JsonException) { /* free-text address */ }
        }
        return WahSpices.Application.Common.GstStates.Format(companyState);
    }

    private static void WriteHsnSummary(IXLWorksheet ws, List<Invoice> invoices)
    {
        Header(ws, "HSN", "Description", "UQC", "Total Quantity", "Rate", "Taxable Value", "IGST", "CGST", "SGST", "Total Value");
        var r = 2;
        foreach (var g in invoices.SelectMany(i => i.LineItems.Where(l => !l.IsDeleted))
                     .GroupBy(l => new { Hsn = string.IsNullOrWhiteSpace(l.HsnCode) ? "(none)" : l.HsnCode!, l.GstRate, Unit = l.Unit.ToUpperInvariant() })
                     .OrderBy(g => g.Key.Hsn).ThenBy(g => g.Key.GstRate))
            Row(ws, r++, g.Key.Hsn, g.First().ProductName, g.Key.Unit, g.Sum(l => l.Quantity), g.Key.GstRate, g.Sum(l => l.TaxableAmount),
                g.Sum(l => l.IgstAmount), g.Sum(l => l.CgstAmount), g.Sum(l => l.SgstAmount), g.Sum(l => l.LineTotal));
    }

    private static void Write3BSheet(XLWorkbook wb, Summary3B s, string? gstin, int year, int month)
    {
        var ws = wb.Worksheets.Add("GSTR-3B");
        ws.Cell(1, 1).Value = "GSTR-3B summary"; ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "GSTIN"; ws.Cell(2, 2).Value = gstin ?? "(set GSTIN in Billing → Template)";
        ws.Cell(3, 1).Value = "Return period"; ws.Cell(3, 2).Value = PeriodLabel(year, month);
        Header(ws, 5, "Section", "Taxable Value", "IGST", "CGST", "SGST", "Cess");
        var r = 6;
        Row(ws, r++, "3.1(a) Outward taxable supplies (other than zero/nil rated)", s.totalTaxable - s.zeroRatedTaxable - s.nilRatedTaxable, s.totalIgst - s.zeroRatedIgst, s.totalCgst, s.totalSgst, 0m);
        Row(ws, r++, "3.1(b) Outward taxable supplies (zero rated)", s.zeroRatedTaxable, s.zeroRatedIgst, 0m, 0m, 0m);
        Row(ws, r++, "3.1(c) Other outward supplies (nil rated, exempted)", s.nilRatedTaxable, 0m, 0m, 0m, 0m);
        Row(ws, r++, "4(A)(5) All other ITC", "", s.itcIgst, s.itcCgst, s.itcSgst, 0m);
        Row(ws, r++, "4(C) Net ITC available", "", s.itcIgst, s.itcCgst, s.itcSgst, 0m);
        r++;
        Row(ws, r++, "Total output tax", s.totalTax);
        Row(ws, r++, "Total ITC", s.totalItc);
        Row(ws, r++, "Net tax payable (cash)", s.netPayable);
        Row(ws, r, "Invoices in period", s.invoiceCount);
        ws.Columns().AdjustToContents();
    }

    private static void Header(IXLWorksheet ws, params string[] cols) => Header(ws, 1, cols);
    private static void Header(IXLWorksheet ws, int row, params string[] cols)
    {
        for (var c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0EA");
        }
    }

    private static void Row(IXLWorksheet ws, int row, params object?[] values)
    {
        for (var c = 0; c < values.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            switch (values[c])
            {
                case decimal d: cell.Value = Math.Round(d, 2); cell.Style.NumberFormat.Format = "#,##0.00"; break;
                case int i: cell.Value = i; break;
                case null: break;
                default: cell.Value = values[c]!.ToString(); break;
            }
        }
    }

    private FileContentResult Xlsx(XLWorkbook wb, string name)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    private static string Csv(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    private static string PeriodLabel(int year, int month) =>
        $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month)} {year}";

    /// "Jun 2026", "June 2026", "2026-06", "06/2026", "06-2026" → (2026, 6)
    private static (int year, int month)? ParsePeriod(string? period)
    {
        if (string.IsNullOrWhiteSpace(period)) return null;
        var formats = new[] { "MMM yyyy", "MMMM yyyy", "yyyy-MM", "MM/yyyy", "M/yyyy", "MM-yyyy", "M-yyyy", "MMM-yyyy", "MMM-yy", "MMM yy" };
        if (DateTime.TryParseExact(period.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out var d))
            return (d.Year, d.Month);
        return null;
    }
}

public record HsnRequest(string? Code, string? Description, decimal GstRate, decimal CessRate = 0, bool IsExportLut = false, bool? IsActive = null);
public record ItcRequest(string? Period, string? SupplierName, string? SupplierGstin, string? InvoiceNumber, decimal TaxableAmount, decimal IgstAmount, decimal CgstAmount, decimal SgstAmount, bool IsEligible = true);
public record GstFilingRequest(string? ReturnType, int Month, int Year, string? Arn, string? Notes, DateTime? FiledOn, bool Force = false);
public record GstFilingUpdateRequest(string? Arn, string? Notes);
