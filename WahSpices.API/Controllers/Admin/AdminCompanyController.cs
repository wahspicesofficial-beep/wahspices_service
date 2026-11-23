using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Controllers.Admin;

// ═══════════════════════════════════════════════════════════════════════════
// Company finance & compliance — partners, who-paid + split on expenses, partner
// ledger & settlement, attachments, alerts. Assets / licences / reports live in
// AdminCompanyAssetsController.cs, AdminCompanyLicencesController.cs, AdminCompanyReportsController.cs.
// Partner capital & settlement data is sensitive: everything sits behind the "Company" module.
// ═══════════════════════════════════════════════════════════════════════════

public record PartnerRequest(string Name, string? Phone, string? Email, decimal SharePercent, bool IsActive = true, DateTime? JoinedOn = null, string? Notes = null);
public record PartnerTxnRequest(DateTime Date, string Type, int? FromPartnerId, int? ToPartnerId, decimal Amount, string? Mode, string? Reference, string? Notes);
public record CompanyExpenseRequest(DateTime Date, string Category, string Description, decimal Amount, string? Notes,
    string? PayerType, string? PaidBy, int? PaidByPartnerId, string? SplitMethod, string? CustomMode, List<SplitInput>? Splits)
{
    public ExpensePaymentInput Payment => new(PayerType, PaidBy, PaidByPartnerId, SplitMethod, CustomMode, Splits);
}

/// <summary>Shared JSON shapes for the company module.</summary>
internal static class CompanyMap
{
    public static async Task<Dictionary<int, string>> PartnerNamesAsync(AppDbContext db) =>
        await db.Partners.AsNoTracking().Where(p => !p.IsDeleted).ToDictionaryAsync(p => p.Id, p => p.Name);

    public static string PartyName(int? id, IReadOnlyDictionary<int, string> names) =>
        id == null ? CompanyFinance.CompanyName : names.TryGetValue(id.Value, out var n) ? n : $"Partner #{id}";

    public static object Expense(ExpenseEntry e, IReadOnlyDictionary<int, string> names) => new
    {
        e.Id, e.Date, e.Category, e.Description, e.Amount, e.PaidBy, e.PayerType, e.PaidByPartnerId,
        PaidByName = e.PayerType == CompanyPayer.Partner ? PartyName(e.PaidByPartnerId, names) : CompanyFinance.CompanyName,
        e.SplitMethod, e.IsApproved, e.ApprovedBy, e.Notes, e.SourceType, e.SourceId,
        HasReceipt = e.ReceiptUrl != null && e.ReceiptUrl.StartsWith(CompanyDocStore.Prefix),
        Splits = e.Splits.OrderBy(s => s.PartnerId == null).ThenBy(s => s.Id)
            .Select(s => new { s.PartnerId, PartnerName = PartyName(s.PartnerId, names), s.Percent, s.Amount }),
    };

    public static object Ledger(PartnerLedgerRow r) => new
    {
        r.PartnerId, r.Name, r.SharePercent, r.IsActive, r.PaidOnBehalf, r.ShareOfExpenses, r.Contributions, r.Withdrawals,
        r.SettlementsPaid, r.SettlementsReceived, r.ExpenseBalance, r.Capital, r.NetBalance,
    };

    /// <summary>Runs the body in a DB transaction (inside the retrying execution strategy); commits only on a 2xx result.</summary>
    public static Task<IActionResult> InTransactionAsync(AppDbContext db, Func<Task<IActionResult>> body) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await body();
            if (result is ObjectResult { StatusCode: >= 200 and < 300 } || result is OkResult) await tx.CommitAsync();
            return result;
        });

    /// <summary>Warning text when active partners' shares don't total 100%.</summary>
    public static async Task<(decimal Total, string? Warning)> ShareCheckAsync(AppDbContext db)
    {
        var active = await db.Partners.AsNoTracking().Where(p => !p.IsDeleted && p.IsActive).ToListAsync();
        var total = active.Sum(p => p.SharePercent);
        if (active.Count == 0) return (0, "No active partners yet — add partners to split expenses.");
        return (total, Math.Abs(total - 100m) > 0.009m ? $"Active partners' shares add up to {total:0.##}%, not 100%. Splits by share are normalised until you fix this." : null);
    }
}

[Route("api/admin/company")]
[RequireModule("Company")]
public class AdminCompanyController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly CompanyDocStore _docs;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminCompanyController> _log;

    public AdminCompanyController(AppDbContext db, CompanyDocStore docs, IConfiguration config, ILogger<AdminCompanyController> log)
    {
        _db = db; _docs = docs; _config = config; _log = log;
    }

    private static BadRequestObjectResult Bad(string msg, string? field = null) => new(ApiResponse<object>.Fail("INVALID", msg, field));
    private NotFoundObjectResult Missing(string what) => NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"{what} not found."));

    // ── Overview ───────────────────────────────────────────────────────────

    /// <summary>Dashboard widget: renewal alerts, share check, quick totals.</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        var today = CompanyFinance.TodayIst();
        var alerts = await CompanyFinance.CurrentAlertsAsync(_db, today);
        var (shareTotal, shareWarning) = await CompanyMap.ShareCheckAsync(_db);
        var ledger = await CompanyFinance.LedgerAsync(_db, null, today);
        var fyStart = new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
        var fyExpenses = await _db.ExpenseEntries.Where(e => !e.IsDeleted && e.Date >= fyStart).SumAsync(e => (decimal?)e.Amount) ?? 0;
        var assets = _db.CompanyAssets.Where(a => !a.IsDeleted && a.Status != AssetStatus.Disposed && a.Status != AssetStatus.Lost);
        return Ok(ApiResponse<object>.Ok(new
        {
            alerts,
            shareTotal, shareWarning,
            partners = ledger.Where(r => r.PartnerId != null).Select(CompanyMap.Ledger),
            settlement = CompanyFinance.Settle(ledger),
            totals = new
            {
                fyExpenses, fyStart,
                assetCount = await assets.CountAsync(),
                assetValue = await assets.SumAsync(a => (decimal?)a.Cost) ?? 0,
                licenceCount = await _db.CompanyLicences.CountAsync(l => !l.IsDeleted && l.RenewedById == null),
            },
        }));
    }

    // ── Partners ───────────────────────────────────────────────────────────

    [HttpGet("partners")]
    public async Task<IActionResult> Partners()
    {
        var list = await _db.Partners.AsNoTracking().Where(p => !p.IsDeleted).OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync();
        var (total, warning) = await CompanyMap.ShareCheckAsync(_db);
        return Ok(ApiResponse<object>.Ok(new
        {
            partners = list.Select(p => new { p.Id, p.Name, p.Phone, p.Email, p.SharePercent, p.IsActive, p.JoinedOn, p.Notes }),
            shareTotal = total, shareWarning = warning,
        }));
    }

    private static string? ValidatePartner(PartnerRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Partner name is required.";
        if (r.Name.Trim().Length > 128) return "Name is too long.";
        if (r.SharePercent is < 0 or > 100) return "Share % must be between 0 and 100.";
        if (!string.IsNullOrWhiteSpace(r.Email) && !r.Email.Contains('@')) return "Enter a valid email.";
        return null;
    }

    [HttpPost("partners")]
    public async Task<IActionResult> CreatePartner([FromBody] PartnerRequest r)
    {
        if (ValidatePartner(r) is { } err) return Bad(err);
        var p = new Partner
        {
            Name = r.Name.Trim(), Phone = r.Phone?.Trim(), Email = r.Email?.Trim(), SharePercent = Math.Round(r.SharePercent, 2),
            IsActive = r.IsActive, JoinedOn = r.JoinedOn, Notes = r.Notes, CreatedBy = AdminName,
        };
        _db.Partners.Add(p);
        await _db.SaveChangesAsync();
        var (_, warning) = await CompanyMap.ShareCheckAsync(_db);
        return StatusCode(201, ApiResponse<object>.Ok(new { p.Id, p.Name, shareWarning = warning }));
    }

    [HttpPut("partners/{id:int}")]
    public async Task<IActionResult> UpdatePartner(int id, [FromBody] PartnerRequest r)
    {
        var p = await _db.Partners.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Missing("Partner");
        if (ValidatePartner(r) is { } err) return Bad(err);
        p.Name = r.Name.Trim(); p.Phone = r.Phone?.Trim(); p.Email = r.Email?.Trim(); p.SharePercent = Math.Round(r.SharePercent, 2);
        p.IsActive = r.IsActive; p.JoinedOn = r.JoinedOn; p.Notes = r.Notes; p.UpdatedAt = DateTime.UtcNow; p.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        var (_, warning) = await CompanyMap.ShareCheckAsync(_db);
        return Ok(ApiResponse<object>.Ok(new { p.Id, p.Name, shareWarning = warning }));
    }

    [HttpDelete("partners/{id:int}")]
    public async Task<IActionResult> DeletePartner(int id)
    {
        var p = await _db.Partners.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Missing("Partner");
        var used = await _db.ExpenseSplits.AnyAsync(s => s.PartnerId == id && !s.ExpenseEntry.IsDeleted)
                   || await _db.ExpenseEntries.AnyAsync(e => e.PaidByPartnerId == id && !e.IsDeleted)
                   || await _db.PartnerTransactions.AnyAsync(t => (t.FromPartnerId == id || t.ToPartnerId == id) && !t.IsDeleted);
        if (used) return BadRequest(ApiResponse<object>.Fail("IN_USE", "This partner has expenses or ledger entries. Mark them inactive instead of deleting."));
        p.IsDeleted = true; p.UpdatedAt = DateTime.UtcNow; p.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Ledger, transactions, settlement ───────────────────────────────────

    /// <summary>Per-partner figures for the date range + the all-time "who owes whom" (up to <paramref name="to"/>).</summary>
    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var rows = await CompanyFinance.LedgerAsync(_db, from, to);
        var cumulative = from == null ? rows : await CompanyFinance.LedgerAsync(_db, null, to);
        return Ok(ApiResponse<object>.Ok(new
        {
            from, to,
            rows = rows.Select(CompanyMap.Ledger),
            balances = cumulative.Select(CompanyMap.Ledger),
            settlement = CompanyFinance.Settle(cumulative),
        }));
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? partnerId, [FromQuery] string? type)
    {
        var q = _db.PartnerTransactions.AsNoTracking().Where(t => !t.IsDeleted);
        if (from != null) q = q.Where(t => t.Date >= from.Value.Date);
        if (to != null) { var end = to.Value.Date.AddDays(1); q = q.Where(t => t.Date < end); }
        if (partnerId != null) q = q.Where(t => t.FromPartnerId == partnerId || t.ToPartnerId == partnerId);
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(t => t.Type == type);
        var list = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(2000).ToListAsync();
        var names = await CompanyMap.PartnerNamesAsync(_db);
        return Ok(ApiResponse<object>.Ok(list.Select(t => new
        {
            t.Id, t.Date, t.Type, t.FromPartnerId, t.ToPartnerId, t.Amount, t.Mode, t.Reference, t.Notes, t.CreatedBy,
            FromName = CompanyMap.PartyName(t.FromPartnerId, names), ToName = CompanyMap.PartyName(t.ToPartnerId, names),
        })));
    }

    [HttpPost("transactions")]
    public async Task<IActionResult> CreateTransaction([FromBody] PartnerTxnRequest r)
    {
        if (r.Date == default) return Bad("Date is required.");
        if (r.Amount <= 0) return Bad("Amount must be greater than zero.");
        var type = PartnerTxnType.All.FirstOrDefault(t => string.Equals(t, r.Type, StringComparison.OrdinalIgnoreCase));
        if (type == null) return Bad("Type must be Contribution, Withdrawal or Settlement.");
        int? from = r.FromPartnerId, to = r.ToPartnerId;
        switch (type)
        {
            case PartnerTxnType.Contribution:
                if (from == null) return Bad("Select the partner who contributed.");
                to = null; break;
            case PartnerTxnType.Withdrawal:
                if (to == null) return Bad("Select the partner who withdrew.");
                from = null; break;
            default:
                if (from == null && to == null) return Bad("A settlement needs at least one partner.");
                if (from == to) return Bad("Payer and receiver must be different.");
                break;
        }
        foreach (var id in new[] { from, to }.Where(x => x != null))
            if (!await _db.Partners.AnyAsync(p => p.Id == id && !p.IsDeleted)) return Bad("Unknown partner.");
        var t = new PartnerTransaction
        {
            Date = r.Date.Date, Type = type, FromPartnerId = from, ToPartnerId = to, Amount = Math.Round(r.Amount, 2),
            Mode = r.Mode?.Trim(), Reference = r.Reference?.Trim(), Notes = r.Notes, CreatedBy = AdminName,
        };
        _db.PartnerTransactions.Add(t);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { t.Id }));
    }

    [HttpDelete("transactions/{id:int}")]
    public async Task<IActionResult> DeleteTransaction(int id)
    {
        var t = await _db.PartnerTransactions.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (t == null) return Missing("Entry");
        t.IsDeleted = true; t.UpdatedAt = DateTime.UtcNow; t.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>Running statement for one partner: expenses paid, expense shares, contributions, withdrawals, settlements.</summary>
    [HttpGet("partners/{id:int}/statement")]
    public async Task<IActionResult> Statement(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var p = await _db.Partners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (p == null) return Missing("Partner");
        var names = await CompanyMap.PartnerNamesAsync(_db);
        var end = to?.Date.AddDays(1);
        var ex = _db.ExpenseEntries.AsNoTracking().Where(e => !e.IsDeleted);
        if (from != null) ex = ex.Where(e => e.Date >= from.Value.Date);
        if (end != null) ex = ex.Where(e => e.Date < end);
        var lines = new List<(DateTime Date, int Order, string Kind, string Text, decimal Credit, decimal Debit, int? ExpenseId)>();
        foreach (var e in await ex.Where(e => e.PayerType == CompanyPayer.Partner && e.PaidByPartnerId == id).ToListAsync())
            lines.Add((e.Date, 0, "Paid", $"Paid: {e.Description} ({e.Category})", e.Amount, 0, e.Id));
        var shares = await _db.ExpenseSplits.AsNoTracking().Include(s => s.ExpenseEntry)
            .Where(s => s.PartnerId == id && ex.Select(e => e.Id).Contains(s.ExpenseEntryId)).ToListAsync();
        foreach (var s in shares)
            lines.Add((s.ExpenseEntry.Date, 1, "Share", $"Share {s.Percent:0.##}% of {s.ExpenseEntry.Description}", 0, s.Amount, s.ExpenseEntryId));
        var tq = _db.PartnerTransactions.AsNoTracking().Where(t => !t.IsDeleted && (t.FromPartnerId == id || t.ToPartnerId == id));
        if (from != null) tq = tq.Where(t => t.Date >= from.Value.Date);
        if (end != null) tq = tq.Where(t => t.Date < end);
        foreach (var t in await tq.ToListAsync())
        {
            var paidOut = t.FromPartnerId == id;
            var other = CompanyMap.PartyName(paidOut ? t.ToPartnerId : t.FromPartnerId, names);
            var text = t.Type switch
            {
                PartnerTxnType.Contribution => "Capital contribution",
                PartnerTxnType.Withdrawal => "Withdrawal",
                _ => paidOut ? $"Settlement paid to {other}" : $"Settlement received from {other}",
            } + (string.IsNullOrWhiteSpace(t.Reference) ? "" : $" · {t.Reference}");
            lines.Add((t.Date, 2, t.Type, text, paidOut ? t.Amount : 0, paidOut ? 0 : t.Amount, null));
        }
        decimal run = 0;
        var rows = lines.OrderBy(l => l.Date).ThenBy(l => l.Order).Select(l =>
        {
            run += l.Credit - l.Debit;
            return new { l.Date, l.Kind, l.Text, l.Credit, l.Debit, Balance = run, l.ExpenseId };
        }).ToList();
        return Ok(ApiResponse<object>.Ok(new { partner = new { p.Id, p.Name, p.SharePercent }, rows, closing = run }));
    }

    // ── Expenses with payer + split ────────────────────────────────────────

    [HttpGet("expenses")]
    public async Task<IActionResult> Expenses([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? partnerId,
        [FromQuery] string? payer, [FromQuery] string? category, [FromQuery] string? search)
    {
        var q = _db.ExpenseEntries.AsNoTracking().Include(e => e.Splits).Where(e => !e.IsDeleted);
        if (from != null) q = q.Where(e => e.Date >= from.Value.Date);
        if (to != null) { var end = to.Value.Date.AddDays(1); q = q.Where(e => e.Date < end); }
        if (!string.IsNullOrWhiteSpace(payer)) q = q.Where(e => e.PayerType == payer);
        if (partnerId != null) q = q.Where(e => e.PaidByPartnerId == partnerId || e.Splits.Any(s => s.PartnerId == partnerId));
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => e.Description.Contains(search) || (e.Notes != null && e.Notes.Contains(search)));
        var list = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Take(3000).ToListAsync();
        var names = await CompanyMap.PartnerNamesAsync(_db);
        return Ok(ApiResponse<object>.Ok(new
        {
            expenses = list.Select(e => CompanyMap.Expense(e, names)),
            totals = new
            {
                amount = list.Sum(e => e.Amount),
                count = list.Count,
                partnerPaid = list.Where(e => e.PayerType == CompanyPayer.Partner).Sum(e => e.Amount),
                companyPaid = list.Where(e => e.PayerType != CompanyPayer.Partner).Sum(e => e.Amount),
            },
        }));
    }

    [HttpGet("expenses/{id:int}")]
    public async Task<IActionResult> GetExpense(int id)
    {
        var e = await _db.ExpenseEntries.AsNoTracking().Include(x => x.Splits).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e == null) return Missing("Expense");
        return Ok(ApiResponse<object>.Ok(CompanyMap.Expense(e, await CompanyMap.PartnerNamesAsync(_db))));
    }

    private static string? ValidateExpense(CompanyExpenseRequest r)
    {
        if (r.Date == default) return "Date is required.";
        if (string.IsNullOrWhiteSpace(r.Category)) return "Category is required.";
        if (string.IsNullOrWhiteSpace(r.Description)) return "Description is required.";
        if (r.Amount <= 0) return "Amount must be greater than zero.";
        return null;
    }

    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] CompanyExpenseRequest r)
    {
        if (ValidateExpense(r) is { } err) return Bad(err);
        var e = new ExpenseEntry { Date = r.Date, Category = r.Category.Trim(), Description = r.Description.Trim(), Amount = Math.Round(r.Amount, 2), Notes = r.Notes, CreatedBy = AdminName };
        if (await CompanyFinance.ApplyAsync(_db, e, r.Payment) is { } splitErr) return Bad(splitErr);
        _db.ExpenseEntries.Add(e);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(CompanyMap.Expense(e, await CompanyMap.PartnerNamesAsync(_db))));
    }

    [HttpPut("expenses/{id:int}")]
    public async Task<IActionResult> UpdateExpense(int id, [FromBody] CompanyExpenseRequest r)
    {
        var e = await _db.ExpenseEntries.Include(x => x.Splits).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e == null) return Missing("Expense");
        if (ValidateExpense(r) is { } err) return Bad(err);
        e.Date = r.Date; e.Category = r.Category.Trim(); e.Description = r.Description.Trim(); e.Amount = Math.Round(r.Amount, 2); e.Notes = r.Notes;
        e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName;
        if (await CompanyFinance.ApplyAsync(_db, e, r.Payment) is { } splitErr) return Bad(splitErr);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(CompanyMap.Expense(e, await CompanyMap.PartnerNamesAsync(_db))));
    }

    [HttpDelete("expenses/{id:int}")]
    public async Task<IActionResult> DeleteExpense(int id)
    {
        var e = await _db.ExpenseEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e == null) return Missing("Expense");
        e.IsDeleted = true; e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName;
        // unlink from the asset / licence it came from (the record stays)
        if (e.SourceType == "Asset") await _db.CompanyAssets.Where(a => a.ExpenseEntryId == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.ExpenseEntryId, (int?)null));
        if (e.SourceType == "Licence") await _db.CompanyLicences.Where(l => l.ExpenseEntryId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.ExpenseEntryId, (int?)null));
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>Split preview without saving (for the form).</summary>
    [HttpPost("expenses/preview-split")]
    public async Task<IActionResult> PreviewSplit([FromBody] CompanyExpenseRequest r)
    {
        var (rows, payer, pid, method, err) = await CompanyFinance.ResolveAsync(_db, Math.Round(r.Amount, 2), r.Payment);
        if (err != null) return Bad(err);
        var names = await CompanyMap.PartnerNamesAsync(_db);
        return Ok(ApiResponse<object>.Ok(new { payer, paidByPartnerId = pid, method, rows = rows.Select(x => new { x.PartnerId, PartnerName = CompanyMap.PartyName(x.PartnerId, names), x.Percent, x.Amount }) }));
    }

    // ── Attachments (receipts, bills, licence documents) ───────────────────

    private async Task<(Func<string?>? Get, Action<string?>? Set, string? Label)> TargetAsync(string kind, int id)
    {
        switch (kind.ToLowerInvariant())
        {
            case "expense":
                var e = await _db.ExpenseEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                return e == null ? (null, null, null) : (() => e.ReceiptUrl, v => { e.ReceiptUrl = v; e.UpdatedAt = DateTime.UtcNow; }, $"Receipt-{id}");
            case "asset":
                var a = await _db.CompanyAssets.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                return a == null ? (null, null, null) : (() => a.DocumentUrl, v => { a.DocumentUrl = v; a.UpdatedAt = DateTime.UtcNow; }, $"Bill-{a.Name}");
            case "licence":
                var l = await _db.CompanyLicences.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                return l == null ? (null, null, null) : (() => l.DocumentUrl, v => { l.DocumentUrl = v; l.UpdatedAt = DateTime.UtcNow; }, $"{l.LicenceType}-{l.Number ?? l.Id.ToString()}");
            default: return (null, null, null);
        }
    }

    /// <summary>Attach a file (multipart "file"): kind = expense | asset | licence. Replaces any previous file.</summary>
    [HttpPost("files/{kind}/{id:int}")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequireModule("Company", "Expenses")]
    public async Task<IActionResult> Upload(string kind, int id, IFormFile? file)
    {
        if (!await KindAllowedAsync(kind)) return StatusCode(403, ApiResponse<object>.Fail("MODULE_FORBIDDEN", "Your role doesn't have access to Company."));
        var (get, set, _) = await TargetAsync(kind, id);
        if (get == null || set == null) return Missing("Record");
        var (token, error) = await _docs.SaveAsync(file, kind);
        if (error != null) return BadRequest(ApiResponse<object>.Fail("INVALID_FILE", error));
        var old = get();
        set(token);
        await _db.SaveChangesAsync();
        if (old != token) _docs.Delete(old);
        return Ok(ApiResponse<object>.Ok(new { hasFile = true }, "File attached."));
    }

    [HttpGet("files/{kind}/{id:int}")]
    [RequireModule("Company", "Expenses")]
    public async Task<IActionResult> Download(string kind, int id)
    {
        if (!await KindAllowedAsync(kind)) return StatusCode(403, ApiResponse<object>.Fail("MODULE_FORBIDDEN", "Your role doesn't have access to Company."));
        var (get, _, label) = await TargetAsync(kind, id);
        var path = get == null ? null : _docs.Resolve(get());
        if (path == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "No file attached."));
        var name = string.Concat($"{label}{Path.GetExtension(path)}".Split(Path.GetInvalidFileNameChars()));
        return PhysicalFile(path, CompanyDocStore.ContentType(path), name);
    }

    [HttpDelete("files/{kind}/{id:int}")]
    [RequireModule("Company", "Expenses")]
    public async Task<IActionResult> RemoveFile(string kind, int id)
    {
        if (!await KindAllowedAsync(kind)) return StatusCode(403, ApiResponse<object>.Fail("MODULE_FORBIDDEN", "Your role doesn't have access to Company."));
        var (get, set, _) = await TargetAsync(kind, id);
        if (get == null || set == null) return Missing("Record");
        var old = get();
        set(null);
        await _db.SaveChangesAsync();
        _docs.Delete(old);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>Expense receipts are open to the Expenses module too; asset / licence files need Company.</summary>
    private async Task<bool> KindAllowedAsync(string kind)
    {
        if (string.Equals(kind, "expense", StringComparison.OrdinalIgnoreCase)) return true;
        var access = await AdminPermissions.GetAccessAsync(HttpContext);
        return access?.Can("Company") == true;
    }

    // ── Alerts ─────────────────────────────────────────────────────────────

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts()
    {
        var alerts = await CompanyFinance.CurrentAlertsAsync(_db, CompanyFinance.TodayIst());
        var log = await _db.CompanyAlertLogs.AsNoTracking().Where(l => !l.IsDeleted).OrderByDescending(l => l.SentAt).Take(50).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            alerts,
            thresholds = CompanyFinance.AlertThresholds,
            sent = log.Select(l => new { l.Kind, l.RefId, l.ExpiryDate, l.Threshold, l.SentAt, l.Recipient }),
        }));
    }

    /// <summary>Run the daily alert job now (sends only alerts not emailed before).</summary>
    [HttpPost("alerts/run")]
    public async Task<IActionResult> RunAlerts()
    {
        try
        {
            var r = await CompanyAlertService.RunAsync(HttpContext.RequestServices, _config, _log, HttpContext.RequestAborted);
            var msg = r.Sent > 0 ? $"Alert email sent to {r.Recipient} ({r.Sent} item(s))." : r.Skipped ?? "Nothing to send.";
            return Ok(ApiResponse<object>.Ok(new { r.Sent, r.Recipient, r.Items, r.Skipped }, msg));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Manual company alert run failed");
            return StatusCode(502, ApiResponse<object>.Fail("EMAIL_FAILED", "Could not send the alert email. Check the email settings."));
        }
    }
}
