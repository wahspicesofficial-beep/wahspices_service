using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.Infrastructure.Services;

/// <summary>One partner's requested share for a custom split (amount OR percent, per CustomMode).</summary>
public sealed record SplitInput(int? PartnerId, decimal? Amount, decimal? Percent);

/// <summary>Who paid an expense and how it is split. Used by expenses, asset purchases and licence fees.</summary>
public sealed record ExpensePaymentInput(
    string? PayerType,            // Company | Partner
    string? PaidBy,               // company account mode (ExpensePaidBy master list) when PayerType = Company
    int? PaidByPartnerId,         // when PayerType = Partner
    string? SplitMethod,          // None | Share | Equal | Custom (default: Share when a partner paid, None when the company paid)
    string? CustomMode,           // Amount | Percent (Custom only)
    List<SplitInput>? Splits);

public sealed record SplitRow(int? PartnerId, decimal Percent, decimal Amount);

public sealed class PartnerLedgerRow
{
    public int? PartnerId { get; init; }            // null = the company
    public string Name { get; init; } = "";
    public decimal SharePercent { get; init; }
    public bool IsActive { get; init; }
    public decimal PaidOnBehalf { get; set; }       // personal money spent on company expenses
    public decimal ShareOfExpenses { get; set; }    // this party's split rows
    public decimal Contributions { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal SettlementsPaid { get; set; }
    public decimal SettlementsReceived { get; set; }
    /// <summary>Expense-sharing position: + = others owe this party, − = this party owes.</summary>
    public decimal ExpenseBalance => PaidOnBehalf - ShareOfExpenses + SettlementsPaid - SettlementsReceived;
    public decimal Capital => Contributions - Withdrawals;
    /// <summary>Partner's overall account: capital + expense position.</summary>
    public decimal NetBalance => Capital + ExpenseBalance;
}

public sealed record SettlementSuggestion(int? FromPartnerId, string FromName, int? ToPartnerId, string ToName, decimal Amount);

public sealed record AlertItem(string Kind, int Id, string Title, string Category, string? Number, DateTime ExpiryDate, int DaysLeft, int ReminderDays, string Status);

public static class CompanyFinance
{
    public const string CompanyName = "Company";
    public static readonly int[] AlertThresholds = { 30, 15, 7, 1, 0 };
    public const int WarrantyWindowDays = 30;

    public static DateTime TodayIst() => EmailLayout.ToIst(DateTime.UtcNow).Date;

    // ── Split engine ───────────────────────────────────────────────────────

    /// <summary>Validates payer + split and returns the rows to store (sums exactly to <paramref name="amount"/>), or an error.</summary>
    public static async Task<(List<SplitRow> Rows, string PayerType, int? PartnerId, string SplitMethod, string? Error)> ResolveAsync(
        AppDbContext db, decimal amount, ExpensePaymentInput p)
    {
        var payer = string.Equals(p.PayerType, CompanyPayer.Partner, StringComparison.OrdinalIgnoreCase) ? CompanyPayer.Partner : CompanyPayer.Company;
        int? payerId = null;
        if (payer == CompanyPayer.Partner)
        {
            if (p.PaidByPartnerId is not int pid) return (new(), payer, null, "", "Select which partner paid.");
            if (!await db.Partners.AnyAsync(x => x.Id == pid && !x.IsDeleted)) return (new(), payer, null, "", "That partner doesn't exist.");
            payerId = pid;
        }
        var method = ExpenseSplitMethod.All.FirstOrDefault(m => string.Equals(m, p.SplitMethod, StringComparison.OrdinalIgnoreCase))
                     ?? (payer == CompanyPayer.Partner ? ExpenseSplitMethod.Share : ExpenseSplitMethod.None);
        if (amount <= 0) return (new(), payer, payerId, method, "Amount must be greater than zero.");

        var active = await db.Partners.Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.Id).ToListAsync();
        List<(int? Id, decimal Pct)> pcts;
        switch (method)
        {
            case ExpenseSplitMethod.None:
                pcts = new() { (null, 100m) };
                break;
            case ExpenseSplitMethod.Equal:
                if (active.Count == 0) return (new(), payer, payerId, method, "Add active partners before splitting equally.");
                pcts = active.Select(x => ((int?)x.Id, 100m / active.Count)).ToList();
                break;
            case ExpenseSplitMethod.Share:
                var withShare = active.Where(x => x.SharePercent > 0).ToList();
                var total = withShare.Sum(x => x.SharePercent);
                if (total <= 0) return (new(), payer, payerId, method, "Set the partners' share % before splitting by share.");
                pcts = withShare.Select(x => ((int?)x.Id, x.SharePercent * 100m / total)).ToList();   // normalised if shares ≠ 100
                break;
            default: // Custom
                var rows = (p.Splits ?? new()).Where(s => (s.Amount ?? 0) != 0 || (s.Percent ?? 0) != 0).ToList();
                if (rows.Count == 0) return (new(), payer, payerId, method, "Enter at least one partner's share for a custom split.");
                if (rows.GroupBy(r => r.PartnerId).Any(g => g.Count() > 1)) return (new(), payer, payerId, method, "Each partner can appear only once in the split.");
                var ids = rows.Where(r => r.PartnerId != null).Select(r => r.PartnerId!.Value).ToList();
                var known = await db.Partners.Where(x => ids.Contains(x.Id) && !x.IsDeleted).Select(x => x.Id).ToListAsync();
                if (known.Count != ids.Count) return (new(), payer, payerId, method, "The split contains an unknown partner.");
                var byPercent = string.Equals(p.CustomMode, "Percent", StringComparison.OrdinalIgnoreCase);
                if (byPercent)
                {
                    if (rows.Any(r => (r.Percent ?? 0) < 0)) return (new(), payer, payerId, method, "Percents can't be negative.");
                    var sum = rows.Sum(r => r.Percent ?? 0);
                    if (Math.Abs(sum - 100m) > 0.01m) return (new(), payer, payerId, method, $"Custom percents must add up to 100% (now {sum:0.##}%).");
                    pcts = rows.Select(r => (r.PartnerId, r.Percent ?? 0)).ToList();
                }
                else
                {
                    if (rows.Any(r => (r.Amount ?? 0) < 0)) return (new(), payer, payerId, method, "Amounts can't be negative.");
                    var sum = rows.Sum(r => r.Amount ?? 0);
                    if (Math.Abs(sum - amount) > 0.01m) return (new(), payer, payerId, method, $"Custom amounts must add up to the expense amount ₹{amount:0.##} (now ₹{sum:0.##}).");
                    // exact amounts as entered; percents derived
                    var exact = rows.Select(r => new SplitRow(r.PartnerId, Math.Round((r.Amount ?? 0) * 100m / amount, 4), Math.Round(r.Amount ?? 0, 2))).ToList();
                    FixRounding(exact, amount);
                    return (exact, payer, payerId, method, null);
                }
                break;
        }
        return (Distribute(amount, pcts), payer, payerId, method, null);
    }

    /// <summary>Splits an amount by percents; rounding remainder goes to the largest row so rows sum exactly.</summary>
    public static List<SplitRow> Distribute(decimal amount, IEnumerable<(int? Id, decimal Pct)> pcts)
    {
        var rows = pcts.Select(x => new SplitRow(x.Id, Math.Round(x.Pct, 4), Math.Round(amount * x.Pct / 100m, 2, MidpointRounding.AwayFromZero))).ToList();
        FixRounding(rows, amount);
        return rows;
    }

    private static void FixRounding(List<SplitRow> rows, decimal amount)
    {
        if (rows.Count == 0) return;
        var diff = Math.Round(amount, 2) - rows.Sum(r => r.Amount);
        if (diff == 0) return;
        var i = rows.IndexOf(rows.OrderByDescending(r => r.Amount).First());
        rows[i] = rows[i] with { Amount = rows[i].Amount + diff };
    }

    /// <summary>Applies payer + split to an expense (replaces its split rows). Caller saves.</summary>
    public static async Task<string?> ApplyAsync(AppDbContext db, ExpenseEntry e, ExpensePaymentInput p)
    {
        var (rows, payer, pid, method, err) = await ResolveAsync(db, e.Amount, p);
        if (err != null) return err;
        if (payer == CompanyPayer.Company)
        {
            if (string.IsNullOrWhiteSpace(p.PaidBy) || p.PaidBy == CompanyPayer.Partner) return "Select how the company paid (cash / bank / UPI).";
            e.PaidBy = p.PaidBy.Trim();
        }
        else e.PaidBy = CompanyPayer.Partner;
        e.PayerType = payer;
        e.PaidByPartnerId = pid;
        e.SplitMethod = method;
        if (e.Id > 0)
        {
            var old = await db.ExpenseSplits.Where(s => s.ExpenseEntryId == e.Id).ToListAsync();
            db.ExpenseSplits.RemoveRange(old);
        }
        e.Splits.Clear();
        foreach (var r in rows) e.Splits.Add(new ExpenseSplit { PartnerId = r.PartnerId, Percent = r.Percent, Amount = r.Amount, CreatedBy = e.UpdatedBy ?? e.CreatedBy });
        return null;
    }

    /// <summary>After an expense amount changes elsewhere: re-distribute by the stored percents (keeps the original split). Caller saves.</summary>
    public static async Task ResplitAsync(AppDbContext db, ExpenseEntry e)
    {
        var rows = await db.ExpenseSplits.Where(s => s.ExpenseEntryId == e.Id).OrderBy(s => s.Id).ToListAsync();
        if (rows.Count == 0) return;
        var fresh = Distribute(e.Amount, rows.Select(r => (r.PartnerId, r.Percent)));
        for (var i = 0; i < rows.Count; i++) { rows[i].Amount = fresh[i].Amount; rows[i].UpdatedAt = DateTime.UtcNow; }
    }

    /// <summary>Makes sure an expense category exists in the category list (asset / licence expenses use a default one).</summary>
    public static async Task EnsureCategoryAsync(AppDbContext db, string name, string color, string? by)
    {
        if (await db.ExpenseCategoryConfigs.AnyAsync(c => c.Name == name && !c.IsDeleted)) return;
        db.ExpenseCategoryConfigs.Add(new ExpenseCategoryConfig { Name = name, Color = color, IsActive = true, CreatedBy = by });
    }

    // ── Partner ledger & settlement ────────────────────────────────────────

    /// <summary>Ledger rows for every partner plus the company. Range applies to expense / transaction dates (inclusive).</summary>
    public static async Task<List<PartnerLedgerRow>> LedgerAsync(AppDbContext db, DateTime? from, DateTime? to, bool includeInactive = true)
    {
        var partners = await db.Partners.AsNoTracking().Where(x => !x.IsDeleted).OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync();
        var rows = partners.Select(x => new PartnerLedgerRow { PartnerId = x.Id, Name = x.Name, SharePercent = x.SharePercent, IsActive = x.IsActive }).ToList();
        var company = new PartnerLedgerRow { PartnerId = null, Name = CompanyName, IsActive = true };
        rows.Add(company);
        PartnerLedgerRow Row(int? id) => rows.FirstOrDefault(r => r.PartnerId == id) ?? company;

        var toEx = to?.Date.AddDays(1);
        var exq = db.ExpenseEntries.AsNoTracking().Where(e => !e.IsDeleted);
        if (from != null) exq = exq.Where(e => e.Date >= from.Value.Date);
        if (toEx != null) exq = exq.Where(e => e.Date < toEx);

        var paid = await exq.GroupBy(e => new { e.PayerType, e.PaidByPartnerId }).Select(g => new { g.Key.PayerType, g.Key.PaidByPartnerId, Total = g.Sum(x => x.Amount) }).ToListAsync();
        foreach (var p in paid)
            Row(p.PayerType == CompanyPayer.Partner ? p.PaidByPartnerId : null).PaidOnBehalf += p.Total;

        var shares = await db.ExpenseSplits.AsNoTracking().Where(s => exq.Select(e => e.Id).Contains(s.ExpenseEntryId))
            .GroupBy(s => s.PartnerId).Select(g => new { PartnerId = g.Key, Total = g.Sum(x => x.Amount) }).ToListAsync();
        foreach (var s in shares) Row(s.PartnerId).ShareOfExpenses += s.Total;
        // Unsplit legacy expenses (created before this module) are company costs
        var unsplit = await exq.Where(e => !e.Splits.Any()).SumAsync(e => (decimal?)e.Amount) ?? 0;
        company.ShareOfExpenses += unsplit;

        var tq = db.PartnerTransactions.AsNoTracking().Where(t => !t.IsDeleted);
        if (from != null) tq = tq.Where(t => t.Date >= from.Value.Date);
        if (toEx != null) tq = tq.Where(t => t.Date < toEx);
        foreach (var t in await tq.ToListAsync())
        {
            switch (t.Type)
            {
                case PartnerTxnType.Contribution: Row(t.FromPartnerId).Contributions += t.Amount; break;
                case PartnerTxnType.Withdrawal:   Row(t.ToPartnerId).Withdrawals += t.Amount; break;
                default:
                    Row(t.FromPartnerId).SettlementsPaid += t.Amount;
                    Row(t.ToPartnerId).SettlementsReceived += t.Amount;
                    break;
            }
        }
        if (!includeInactive) rows = rows.Where(r => r.IsActive || r.ExpenseBalance != 0 || r.Capital != 0).ToList();
        return rows;
    }

    /// <summary>Minimal list of payments that squares everyone's expense balance (debtors pay creditors).</summary>
    public static List<SettlementSuggestion> Settle(IEnumerable<PartnerLedgerRow> rows)
    {
        var debt = rows.Where(r => Math.Round(r.ExpenseBalance, 2) < 0).Select(r => (Row: r, Left: -Math.Round(r.ExpenseBalance, 2))).OrderByDescending(x => x.Left).ToList();
        var cred = rows.Where(r => Math.Round(r.ExpenseBalance, 2) > 0).Select(r => (Row: r, Left: Math.Round(r.ExpenseBalance, 2))).OrderByDescending(x => x.Left).ToList();
        var result = new List<SettlementSuggestion>();
        int i = 0, j = 0;
        while (i < debt.Count && j < cred.Count)
        {
            var amt = Math.Min(debt[i].Left, cred[j].Left);
            if (amt >= 0.01m)
                result.Add(new SettlementSuggestion(debt[i].Row.PartnerId, debt[i].Row.Name, cred[j].Row.PartnerId, cred[j].Row.Name, amt));
            debt[i] = (debt[i].Row, debt[i].Left - amt);
            cred[j] = (cred[j].Row, cred[j].Left - amt);
            if (debt[i].Left < 0.01m) i++;
            if (cred[j].Left < 0.01m) j++;
        }
        return result;
    }

    // ── Licences / warranties ──────────────────────────────────────────────

    public static int? DaysLeft(DateTime? expiry, DateTime today) => expiry == null ? null : (int)(expiry.Value.Date - today).TotalDays;

    /// <summary>Active | ExpiringSoon | Expired | Renewed | Lifetime</summary>
    public static string LicenceStatus(CompanyLicence l, DateTime today)
    {
        if (l.RenewedById != null) return "Renewed";
        if (l.ExpiryDate == null) return "Lifetime";
        var d = DaysLeft(l.ExpiryDate, today)!.Value;
        if (d < 0) return "Expired";
        return d <= Math.Max(0, l.ReminderDays) ? "ExpiringSoon" : "Active";
    }

    /// <summary>Thresholds that apply to a licence: 30/15/7/1/0 within its reminder window (plus the window itself if longer than 30).</summary>
    public static int[] ThresholdsFor(int reminderDays)
    {
        var r = Math.Max(1, reminderDays);
        return AlertThresholds.Append(r).Distinct().Where(t => t <= r).OrderByDescending(t => t).ToArray();
    }

    /// <summary>The most urgent threshold already reached (null = not yet in the window).</summary>
    public static int? CurrentThreshold(int daysLeft, int reminderDays)
    {
        if (daysLeft <= 0) return 0;
        var hit = ThresholdsFor(reminderDays).Where(t => daysLeft <= t).ToList();
        return hit.Count == 0 ? null : hit.Min();
    }

    /// <summary>Everything that needs attention now: licences in their reminder window / expired, warranties ending within 30 days.</summary>
    public static async Task<List<AlertItem>> CurrentAlertsAsync(AppDbContext db, DateTime today)
    {
        var list = new List<AlertItem>();
        var lic = await db.CompanyLicences.AsNoTracking().Where(l => !l.IsDeleted && l.RenewedById == null && l.ExpiryDate != null).ToListAsync();
        foreach (var l in lic)
        {
            var d = DaysLeft(l.ExpiryDate, today)!.Value;
            if (d > Math.Max(0, l.ReminderDays)) continue;
            list.Add(new AlertItem("Licence", l.Id, l.Title, l.LicenceType, l.Number, l.ExpiryDate!.Value, d, l.ReminderDays, d < 0 ? "Expired" : "ExpiringSoon"));
        }
        var limit = today.AddDays(WarrantyWindowDays);
        var assets = await db.CompanyAssets.AsNoTracking()
            .Where(a => !a.IsDeleted && a.WarrantyExpiry != null && a.WarrantyExpiry <= limit && a.WarrantyExpiry >= today.AddDays(-1)
                        && a.Status != AssetStatus.Disposed && a.Status != AssetStatus.Lost)
            .ToListAsync();
        foreach (var a in assets)
        {
            var d = DaysLeft(a.WarrantyExpiry, today)!.Value;
            if (d < 0) continue;
            list.Add(new AlertItem("Warranty", a.Id, a.Name, a.Category, a.SerialNumber, a.WarrantyExpiry!.Value, d, WarrantyWindowDays, "ExpiringSoon"));
        }
        return list.OrderBy(x => x.DaysLeft).ToList();
    }
}
