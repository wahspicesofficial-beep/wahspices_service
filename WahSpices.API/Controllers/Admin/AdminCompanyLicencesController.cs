using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Controllers.Admin;

public record LicenceRequest(
    string? LicenceType, string? Title, string? Number, string? IssuingAuthority, DateTime? StartDate, DateTime? ExpiryDate,
    decimal Fee, int? ReminderDays, string? Notes,
    bool CreateExpense = false, string? ExpenseCategory = null, ExpensePaymentInput? Payment = null);

/// <summary>Licences & registrations (FSSAI, GST, Udyam, trade licence …) with expiry status, fee expense and renewal chain.</summary>
[Route("api/admin/company/licences")]
[RequireModule("Company")]
public class AdminCompanyLicencesController : AdminBaseController
{
    public const string DefaultExpenseCategory = "Licences & Registrations";
    private readonly AppDbContext _db;
    private readonly CompanyDocStore _docs;
    public AdminCompanyLicencesController(AppDbContext db, CompanyDocStore docs) { _db = db; _docs = docs; }

    private static BadRequestObjectResult Bad(string msg) => new(ApiResponse<object>.Fail("INVALID", msg));

    private static object Map(CompanyLicence l, ExpenseEntry? e, IReadOnlyDictionary<int, string> names, DateTime today) => new
    {
        l.Id, l.LicenceType, l.Title, l.Number, l.IssuingAuthority, l.StartDate, l.ExpiryDate, l.Fee, l.ReminderDays, l.Notes,
        Status = CompanyFinance.LicenceStatus(l, today), DaysLeft = CompanyFinance.DaysLeft(l.ExpiryDate, today),
        HasDocument = l.DocumentUrl != null && l.DocumentUrl.StartsWith(CompanyDocStore.Prefix),
        l.PreviousLicenceId, l.RenewedById, l.ExpenseEntryId,
        Expense = e == null ? null : CompanyMap.Expense(e, names),
    };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeRenewed = false, [FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        var q = _db.CompanyLicences.AsNoTracking().Where(l => !l.IsDeleted);
        if (!includeRenewed) q = q.Where(l => l.RenewedById == null);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(l => l.Title.Contains(search) || (l.Number != null && l.Number.Contains(search)) || l.LicenceType.Contains(search));
        var list = await q.OrderBy(l => l.ExpiryDate == null).ThenBy(l => l.ExpiryDate).ThenBy(l => l.Title).ToListAsync();
        var today = CompanyFinance.TodayIst();
        if (!string.IsNullOrWhiteSpace(status)) list = list.Where(l => CompanyFinance.LicenceStatus(l, today) == status).ToList();
        var expIds = list.Where(l => l.ExpenseEntryId != null).Select(l => l.ExpenseEntryId!.Value).ToList();
        var exps = await _db.ExpenseEntries.AsNoTracking().Include(e => e.Splits).Where(e => expIds.Contains(e.Id) && !e.IsDeleted).ToDictionaryAsync(e => e.Id);
        var names = await CompanyMap.PartnerNamesAsync(_db);
        var current = list.Where(l => l.RenewedById == null).ToList();
        var feesThisFy = await FeesSinceAsync(new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1));
        return Ok(ApiResponse<object>.Ok(new
        {
            licences = list.Select(l => Map(l, l.ExpenseEntryId is int id && exps.TryGetValue(id, out var e) ? e : null, names, today)),
            totals = new
            {
                count = current.Count,
                active = current.Count(l => CompanyFinance.LicenceStatus(l, today) is "Active" or "Lifetime"),
                expiringSoon = current.Count(l => CompanyFinance.LicenceStatus(l, today) == "ExpiringSoon"),
                expired = current.Count(l => CompanyFinance.LicenceStatus(l, today) == "Expired"),
                feesThisFy,
            },
        }));
    }

    private async Task<decimal> FeesSinceAsync(DateTime from) =>
        await _db.CompanyLicences.Where(l => !l.IsDeleted && (l.StartDate ?? l.CreatedAt) >= from).SumAsync(l => (decimal?)l.Fee) ?? 0;

    /// <summary>One licence with its whole renewal chain (oldest → newest).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var l = await _db.CompanyLicences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (l == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Licence not found."));
        var chain = new List<CompanyLicence> { l };
        var guard = 0;
        for (var p = l.PreviousLicenceId; p != null && guard++ < 100;)
        {
            var prev = await _db.CompanyLicences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p);
            if (prev == null) break;
            chain.Insert(0, prev); p = prev.PreviousLicenceId;
        }
        for (var n = l.RenewedById; n != null && guard++ < 200;)
        {
            var next = await _db.CompanyLicences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == n);
            if (next == null) break;
            chain.Add(next); n = next.RenewedById;
        }
        var expIds = chain.Where(c => c.ExpenseEntryId != null).Select(c => c.ExpenseEntryId!.Value).ToList();
        var exps = await _db.ExpenseEntries.AsNoTracking().Include(e => e.Splits).Where(e => expIds.Contains(e.Id) && !e.IsDeleted).ToDictionaryAsync(e => e.Id);
        var names = await CompanyMap.PartnerNamesAsync(_db);
        var today = CompanyFinance.TodayIst();
        object M(CompanyLicence c) => Map(c, c.ExpenseEntryId is int eid && exps.TryGetValue(eid, out var e) ? e : null, names, today);
        var alerts = await _db.CompanyAlertLogs.AsNoTracking().Where(a => a.Kind == "Licence" && a.RefId == id && !a.IsDeleted).OrderByDescending(a => a.SentAt).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            licence = M(l),
            history = chain.Where(c => !c.IsDeleted).Select(M),
            alertsSent = alerts.Select(a => new { a.Threshold, a.ExpiryDate, a.SentAt, a.Recipient }),
        }));
    }

    private static string? Validate(LicenceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.LicenceType)) return "Licence type is required.";
        if (string.IsNullOrWhiteSpace(r.Title)) return "Name / title is required.";
        if (r.Fee < 0) return "Fee can't be negative.";
        if (r.ReminderDays is < 0 or > 365) return "Reminder days must be between 0 and 365.";
        if (r.StartDate != null && r.ExpiryDate != null && r.ExpiryDate < r.StartDate) return "Expiry date is before the start date.";
        if (r.CreateExpense && r.Fee <= 0) return "Enter the fee to record it as an expense.";
        return null;
    }

    private static void Fill(CompanyLicence l, LicenceRequest r)
    {
        l.LicenceType = r.LicenceType!.Trim(); l.Title = r.Title!.Trim(); l.Number = r.Number?.Trim(); l.IssuingAuthority = r.IssuingAuthority?.Trim();
        l.StartDate = r.StartDate?.Date; l.ExpiryDate = r.ExpiryDate?.Date; l.Fee = Math.Round(r.Fee, 2); l.ReminderDays = r.ReminderDays ?? 30; l.Notes = r.Notes;
    }

    private async Task<string?> UpsertExpenseAsync(CompanyLicence l, LicenceRequest r)
    {
        ExpenseEntry? e = l.ExpenseEntryId == null ? null : await _db.ExpenseEntries.Include(x => x.Splits).FirstOrDefaultAsync(x => x.Id == l.ExpenseEntryId && !x.IsDeleted);
        if (e == null && !r.CreateExpense) return null;
        if (e != null && l.Fee <= 0) return "This licence has a fee expense — keep a fee above zero or delete the expense first.";
        var isNew = e == null;
        if (isNew)
        {
            var cat = string.IsNullOrWhiteSpace(r.ExpenseCategory) ? DefaultExpenseCategory : r.ExpenseCategory.Trim();
            if (cat == DefaultExpenseCategory) await CompanyFinance.EnsureCategoryAsync(_db, cat, "teal", AdminName);
            e = new ExpenseEntry { Category = cat, CreatedBy = AdminName, SourceType = "Licence" };
        }
        else { e!.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName; if (!string.IsNullOrWhiteSpace(r.ExpenseCategory)) e.Category = r.ExpenseCategory.Trim(); }
        e.Date = l.StartDate ?? CompanyFinance.TodayIst();
        e.Amount = l.Fee;
        e.Description = $"Licence fee: {l.Title}" + (string.IsNullOrWhiteSpace(l.Number) ? "" : $" ({l.Number})");
        if (r.Payment != null)
        {
            if (await CompanyFinance.ApplyAsync(_db, e, r.Payment) is { } err) return err;
        }
        else if (isNew) return "Choose who paid the fee.";
        else await CompanyFinance.ResplitAsync(_db, e);
        if (isNew) { _db.ExpenseEntries.Add(e); await _db.SaveChangesAsync(); l.ExpenseEntryId = e.Id; e.SourceId = l.Id; }
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LicenceRequest r)
    {
        if (Validate(r) is { } err) return Bad(err);
        return await CompanyMap.InTransactionAsync(_db, async () =>
        {
            var l = new CompanyLicence { CreatedBy = AdminName };
            Fill(l, r);
            _db.CompanyLicences.Add(l);
            await _db.SaveChangesAsync();
            if (await UpsertExpenseAsync(l, r) is { } expErr) return Bad(expErr);
            await _db.SaveChangesAsync();
            return StatusCode(201, ApiResponse<object>.Ok(new { l.Id, l.ExpenseEntryId }));
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LicenceRequest r)
    {
        var l = await _db.CompanyLicences.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (l == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Licence not found."));
        if (Validate(r) is { } err) return Bad(err);
        return await CompanyMap.InTransactionAsync(_db, async () =>
        {
            Fill(l, r);
            l.UpdatedAt = DateTime.UtcNow; l.UpdatedBy = AdminName;
            if (await UpsertExpenseAsync(l, r) is { } expErr) return Bad(expErr);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { l.Id, l.ExpenseEntryId }));
        });
    }

    /// <summary>Renew: creates the next period (new number/dates/fee/document) linked to this one; this one stops alerting.</summary>
    [HttpPost("{id:int}/renew")]
    public async Task<IActionResult> Renew(int id, [FromBody] LicenceRequest r)
    {
        var old = await _db.CompanyLicences.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (old == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Licence not found."));
        if (old.RenewedById != null) return BadRequest(ApiResponse<object>.Fail("ALREADY_RENEWED", "This period was already renewed — renew the latest one."));
        var req = r with
        {
            LicenceType = string.IsNullOrWhiteSpace(r.LicenceType) ? old.LicenceType : r.LicenceType,
            Title = string.IsNullOrWhiteSpace(r.Title) ? old.Title : r.Title,
            IssuingAuthority = r.IssuingAuthority ?? old.IssuingAuthority,
            Number = r.Number ?? old.Number,
            ReminderDays = r.ReminderDays ?? old.ReminderDays,
        };
        if (Validate(req) is { } err) return Bad(err);
        if (req.ExpiryDate != null && old.ExpiryDate != null && req.ExpiryDate <= old.ExpiryDate) return Bad("The new expiry date must be after the current one.");
        return await CompanyMap.InTransactionAsync(_db, async () =>
        {
            var l = new CompanyLicence { CreatedBy = AdminName, PreviousLicenceId = old.Id };
            Fill(l, req);
            _db.CompanyLicences.Add(l);
            await _db.SaveChangesAsync();
            old.RenewedById = l.Id; old.UpdatedAt = DateTime.UtcNow; old.UpdatedBy = AdminName;
            if (await UpsertExpenseAsync(l, req) is { } expErr) return Bad(expErr);
            await _db.SaveChangesAsync();
            return StatusCode(201, ApiResponse<object>.Ok(new { l.Id, l.ExpenseEntryId, previousId = old.Id }, "Renewed."));
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool withExpense = true)
    {
        var l = await _db.CompanyLicences.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (l == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Licence not found."));
        if (l.RenewedById != null) return BadRequest(ApiResponse<object>.Fail("HAS_RENEWAL", "This period has a newer renewal — keep it as history, or delete the newer period first."));
        l.IsDeleted = true; l.UpdatedAt = DateTime.UtcNow; l.UpdatedBy = AdminName;
        // Deleting the latest period re-opens the previous one (so it alerts again)
        if (l.PreviousLicenceId != null)
        {
            var prev = await _db.CompanyLicences.FirstOrDefaultAsync(x => x.Id == l.PreviousLicenceId && x.RenewedById == l.Id);
            if (prev != null) { prev.RenewedById = null; prev.UpdatedAt = DateTime.UtcNow; }
        }
        if (withExpense && l.ExpenseEntryId != null)
        {
            var e = await _db.ExpenseEntries.FirstOrDefaultAsync(x => x.Id == l.ExpenseEntryId && !x.IsDeleted);
            if (e != null) { e.IsDeleted = true; e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName; }
        }
        await _db.SaveChangesAsync();
        _docs.Delete(l.DocumentUrl);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
