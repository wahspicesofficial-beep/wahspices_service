using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Controllers.Admin;

public record AssetRequest(
    string Name, string Category, DateTime PurchaseDate, decimal Cost, string? Vendor, string? InvoiceNumber,
    string? Location, string? AssignedTo, string? SerialNumber, DateTime? WarrantyExpiry, string? Status,
    DateTime? DisposalDate, decimal? DisposalValue, string? Notes,
    bool CreateExpense = false, string? ExpenseCategory = null, ExpensePaymentInput? Payment = null, string? StatusNote = null);

public record AssetStatusRequest(string Status, DateTime? Date, string? Note, decimal? DisposalValue);

/// <summary>Company assets register (printer, stamps/seals, furniture, computers, machines …) with linked purchase expense.</summary>
[Route("api/admin/company/assets")]
[RequireModule("Company")]
public class AdminCompanyAssetsController : AdminBaseController
{
    public const string DefaultExpenseCategory = "Assets & Equipment";
    private readonly AppDbContext _db;
    private readonly CompanyDocStore _docs;
    public AdminCompanyAssetsController(AppDbContext db, CompanyDocStore docs) { _db = db; _docs = docs; }

    private static BadRequestObjectResult Bad(string msg) => new(ApiResponse<object>.Fail("INVALID", msg));

    private static object Map(CompanyAsset a, ExpenseEntry? e, IReadOnlyDictionary<int, string> names, DateTime today) => new
    {
        a.Id, a.Name, a.Category, a.PurchaseDate, a.Cost, a.Vendor, a.InvoiceNumber, a.Location, a.AssignedTo, a.SerialNumber,
        a.WarrantyExpiry, WarrantyDaysLeft = CompanyFinance.DaysLeft(a.WarrantyExpiry, today), a.Status, a.DisposalDate, a.DisposalValue, a.Notes,
        HasDocument = a.DocumentUrl != null && a.DocumentUrl.StartsWith(CompanyDocStore.Prefix),
        a.ExpenseEntryId,
        Expense = e == null ? null : CompanyMap.Expense(e, names),
    };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? category, [FromQuery] string? search)
    {
        var q = _db.CompanyAssets.AsNoTracking().Where(a => !a.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Name.Contains(search) || (a.SerialNumber != null && a.SerialNumber.Contains(search)) || (a.Vendor != null && a.Vendor.Contains(search)) || (a.AssignedTo != null && a.AssignedTo.Contains(search)));
        var list = await q.OrderByDescending(a => a.PurchaseDate).ThenByDescending(a => a.Id).ToListAsync();
        var expIds = list.Where(a => a.ExpenseEntryId != null).Select(a => a.ExpenseEntryId!.Value).ToList();
        var exps = await _db.ExpenseEntries.AsNoTracking().Include(e => e.Splits).Where(e => expIds.Contains(e.Id) && !e.IsDeleted).ToDictionaryAsync(e => e.Id);
        var names = await CompanyMap.PartnerNamesAsync(_db);
        var today = CompanyFinance.TodayIst();
        var live = list.Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.Lost).ToList();
        return Ok(ApiResponse<object>.Ok(new
        {
            assets = list.Select(a => Map(a, a.ExpenseEntryId is int id && exps.TryGetValue(id, out var e) ? e : null, names, today)),
            totals = new
            {
                count = list.Count, activeCount = live.Count, activeValue = live.Sum(a => a.Cost), totalCost = list.Sum(a => a.Cost),
                warrantyDue = live.Count(a => a.WarrantyExpiry != null && a.WarrantyExpiry >= today && a.WarrantyExpiry <= today.AddDays(CompanyFinance.WarrantyWindowDays)),
            },
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var a = await _db.CompanyAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Asset not found."));
        var e = a.ExpenseEntryId == null ? null : await _db.ExpenseEntries.AsNoTracking().Include(x => x.Splits).FirstOrDefaultAsync(x => x.Id == a.ExpenseEntryId && !x.IsDeleted);
        var history = await _db.CompanyAssetHistories.AsNoTracking().Where(h => h.CompanyAssetId == id && !h.IsDeleted).OrderByDescending(h => h.Date).ThenByDescending(h => h.Id).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            asset = Map(a, e, await CompanyMap.PartnerNamesAsync(_db), CompanyFinance.TodayIst()),
            history = history.Select(h => new { h.Id, h.FromStatus, h.ToStatus, h.Date, h.Note, h.CreatedBy }),
        }));
    }

    private static string? Validate(AssetRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Asset name is required.";
        if (string.IsNullOrWhiteSpace(r.Category)) return "Category is required.";
        if (r.PurchaseDate == default) return "Purchase date is required.";
        if (r.Cost < 0) return "Cost can't be negative.";
        if (r.Status != null && !AssetStatus.All.Contains(r.Status)) return "Unknown status.";
        if (r.WarrantyExpiry != null && r.WarrantyExpiry < r.PurchaseDate) return "Warranty expiry is before the purchase date.";
        if (r.CreateExpense && r.Cost <= 0) return "Enter the cost to record the purchase expense.";
        return null;
    }

    private async Task<string?> UpsertExpenseAsync(CompanyAsset a, AssetRequest r)
    {
        ExpenseEntry? e = a.ExpenseEntryId == null ? null : await _db.ExpenseEntries.Include(x => x.Splits).FirstOrDefaultAsync(x => x.Id == a.ExpenseEntryId && !x.IsDeleted);
        if (e == null && !r.CreateExpense) return null;
        var isNew = e == null;
        if (isNew)
        {
            var cat = string.IsNullOrWhiteSpace(r.ExpenseCategory) ? DefaultExpenseCategory : r.ExpenseCategory.Trim();
            if (cat == DefaultExpenseCategory) await CompanyFinance.EnsureCategoryAsync(_db, cat, "indigo", AdminName);
            e = new ExpenseEntry { Category = cat, CreatedBy = AdminName, SourceType = "Asset" };
        }
        else { e!.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName; if (!string.IsNullOrWhiteSpace(r.ExpenseCategory)) e.Category = r.ExpenseCategory.Trim(); }
        e.Date = a.PurchaseDate;
        e.Amount = a.Cost;
        e.Description = $"Asset purchase: {a.Name}" + (string.IsNullOrWhiteSpace(a.Vendor) ? "" : $" ({a.Vendor})");
        e.Notes = string.IsNullOrWhiteSpace(a.InvoiceNumber) ? e.Notes : $"Bill {a.InvoiceNumber}";
        if (r.Payment != null)
        {
            if (await CompanyFinance.ApplyAsync(_db, e, r.Payment) is { } err) return err;
        }
        else if (isNew) return "Choose who paid for the asset.";
        else await CompanyFinance.ResplitAsync(_db, e);
        if (isNew) { _db.ExpenseEntries.Add(e); await _db.SaveChangesAsync(); a.ExpenseEntryId = e.Id; e.SourceId = a.Id; }
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AssetRequest r)
    {
        if (Validate(r) is { } err) return Bad(err);
        return await CompanyMap.InTransactionAsync(_db, async () =>
        {
        var a = new CompanyAsset { CreatedBy = AdminName };
        Fill(a, r);
        a.Status = r.Status ?? AssetStatus.InUse;
        _db.CompanyAssets.Add(a);
        await _db.SaveChangesAsync();
        _db.CompanyAssetHistories.Add(new CompanyAssetHistory { CompanyAssetId = a.Id, FromStatus = null, ToStatus = a.Status, Date = a.PurchaseDate, Note = "Added to register", CreatedBy = AdminName });
        if (await UpsertExpenseAsync(a, r) is { } expErr) return Bad(expErr);   // not committed → rolled back
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { a.Id, a.ExpenseEntryId }));
        });
    }

    private static void Fill(CompanyAsset a, AssetRequest r)
    {
        a.Name = r.Name.Trim(); a.Category = r.Category.Trim(); a.PurchaseDate = r.PurchaseDate.Date; a.Cost = Math.Round(r.Cost, 2);
        a.Vendor = r.Vendor?.Trim(); a.InvoiceNumber = r.InvoiceNumber?.Trim(); a.Location = r.Location?.Trim(); a.AssignedTo = r.AssignedTo?.Trim();
        a.SerialNumber = r.SerialNumber?.Trim(); a.WarrantyExpiry = r.WarrantyExpiry?.Date; a.Notes = r.Notes;
        a.DisposalDate = r.DisposalDate?.Date; a.DisposalValue = r.DisposalValue;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AssetRequest r)
    {
        var a = await _db.CompanyAssets.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Asset not found."));
        if (Validate(r) is { } err) return Bad(err);
        return await CompanyMap.InTransactionAsync(_db, async () =>
        {
        var oldStatus = a.Status;
        Fill(a, r);
        a.UpdatedAt = DateTime.UtcNow; a.UpdatedBy = AdminName;
        if (r.Status != null && r.Status != oldStatus)
        {
            a.Status = r.Status;
            _db.CompanyAssetHistories.Add(new CompanyAssetHistory { CompanyAssetId = a.Id, FromStatus = oldStatus, ToStatus = r.Status, Date = CompanyFinance.TodayIst(), Note = r.StatusNote, CreatedBy = AdminName });
        }
        if (await UpsertExpenseAsync(a, r) is { } expErr) return Bad(expErr);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { a.Id, a.ExpenseEntryId }));
        });
    }

    /// <summary>Change status (In use / Under repair / Disposed / Lost) with a history note.</summary>
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] AssetStatusRequest r)
    {
        var a = await _db.CompanyAssets.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Asset not found."));
        if (!AssetStatus.All.Contains(r.Status)) return Bad("Unknown status.");
        if (r.Status == a.Status) return Bad("The asset already has that status.");
        var date = (r.Date ?? CompanyFinance.TodayIst()).Date;
        _db.CompanyAssetHistories.Add(new CompanyAssetHistory { CompanyAssetId = a.Id, FromStatus = a.Status, ToStatus = r.Status, Date = date, Note = r.Note, CreatedBy = AdminName });
        a.Status = r.Status;
        if (r.Status is AssetStatus.Disposed or AssetStatus.Lost) { a.DisposalDate = date; if (r.DisposalValue != null) a.DisposalValue = r.DisposalValue; }
        else { a.DisposalDate = null; a.DisposalValue = null; }
        a.UpdatedAt = DateTime.UtcNow; a.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { a.Id, a.Status }));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool withExpense = true)
    {
        var a = await _db.CompanyAssets.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Asset not found."));
        a.IsDeleted = true; a.UpdatedAt = DateTime.UtcNow; a.UpdatedBy = AdminName;
        if (withExpense && a.ExpenseEntryId != null)
        {
            var e = await _db.ExpenseEntries.FirstOrDefaultAsync(x => x.Id == a.ExpenseEntryId && !x.IsDeleted);
            if (e != null) { e.IsDeleted = true; e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName; }
        }
        await _db.SaveChangesAsync();
        _docs.Delete(a.DocumentUrl);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
