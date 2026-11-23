using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Controllers.Admin;

/// <summary>Company finance reports: totals by partner / category / month, asset register value, licence fees; Excel export.</summary>
[Route("api/admin/company/reports")]
[RequireModule("Company")]
public class AdminCompanyReportsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminCompanyReportsController(AppDbContext db) => _db = db;

    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var today = CompanyFinance.TodayIst();
        var f = (from ?? new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1)).Date;   // default: this financial year
        var t = (to ?? today).Date;
        return (f, t < f ? f : t);
    }

    private async Task<List<ExpenseEntry>> ExpensesAsync(DateTime f, DateTime t)
    {
        var end = t.AddDays(1);
        return await _db.ExpenseEntries.AsNoTracking().Include(e => e.Splits)
            .Where(e => !e.IsDeleted && e.Date >= f && e.Date < end).OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync();
    }

    [HttpGet]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = Range(from, to);
        var list = await ExpensesAsync(f, t);
        var names = await CompanyMap.PartnerNamesAsync(_db);
        var ledger = await CompanyFinance.LedgerAsync(_db, f, t);

        var byCategory = list.GroupBy(e => e.Category).Select(g => new { category = g.Key, total = g.Sum(e => e.Amount), count = g.Count() }).OrderByDescending(x => x.total).ToList();
        var byMonth = list.GroupBy(e => new { e.Date.Year, e.Date.Month })
            .Select(g => new
            {
                year = g.Key.Year, month = g.Key.Month, total = g.Sum(e => e.Amount),
                partnerPaid = g.Where(e => e.PayerType == CompanyPayer.Partner).Sum(e => e.Amount),
                companyPaid = g.Where(e => e.PayerType != CompanyPayer.Partner).Sum(e => e.Amount),
            })
            .OrderBy(x => x.year).ThenBy(x => x.month).ToList();
        // partner × category: each partner's share of expenses per category
        var partnerCategory = list.SelectMany(e => e.Splits.Select(s => new { s.PartnerId, e.Category, s.Amount }))
            .GroupBy(x => new { x.PartnerId, x.Category })
            .Select(g => new { partnerId = g.Key.PartnerId, partnerName = CompanyMap.PartyName(g.Key.PartnerId, names), category = g.Key.Category, total = g.Sum(x => x.Amount) })
            .OrderBy(x => x.partnerName).ThenByDescending(x => x.total).ToList();

        var assets = await _db.CompanyAssets.AsNoTracking().Where(a => !a.IsDeleted).ToListAsync();
        var live = assets.Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.Lost).ToList();
        var licences = await _db.CompanyLicences.AsNoTracking().Where(l => !l.IsDeleted).ToListAsync();
        var feesInRange = licences.Where(l => (l.StartDate ?? l.CreatedAt.Date) >= f && (l.StartDate ?? l.CreatedAt.Date) <= t).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            from = f, to = t,
            totals = new
            {
                amount = list.Sum(e => e.Amount), count = list.Count,
                partnerPaid = list.Where(e => e.PayerType == CompanyPayer.Partner).Sum(e => e.Amount),
                companyPaid = list.Where(e => e.PayerType != CompanyPayer.Partner).Sum(e => e.Amount),
            },
            byPartner = ledger.Select(CompanyMap.Ledger),
            byCategory, byMonth, partnerCategory,
            assets = new
            {
                count = live.Count, value = live.Sum(a => a.Cost), allTimeCost = assets.Sum(a => a.Cost),
                disposedValue = assets.Where(a => a.Status == AssetStatus.Disposed).Sum(a => a.DisposalValue ?? 0),
                byCategory = live.GroupBy(a => a.Category).Select(g => new { category = g.Key, count = g.Count(), value = g.Sum(a => a.Cost) }).OrderByDescending(x => x.value),
                byStatus = assets.GroupBy(a => a.Status).Select(g => new { status = g.Key, count = g.Count(), value = g.Sum(a => a.Cost) }),
            },
            licenceFees = new
            {
                total = feesInRange.Sum(l => l.Fee), allTime = licences.Sum(l => l.Fee),
                byType = feesInRange.GroupBy(l => l.LicenceType).Select(g => new { type = g.Key, count = g.Count(), total = g.Sum(l => l.Fee) }).OrderByDescending(x => x.total),
            },
        }));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = Range(from, to);
        var list = await ExpensesAsync(f, t);
        var partners = await _db.Partners.AsNoTracking().Where(p => !p.IsDeleted).OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync();
        var names = partners.ToDictionary(p => p.Id, p => p.Name);
        var paidByLabels = await _db.MasterDataValues.AsNoTracking().Where(v => v.Type == "ExpensePaidBy").ToDictionaryAsync(v => v.Value, v => v.Label);
        var ledger = await CompanyFinance.LedgerAsync(_db, f, t);
        var cumulative = await CompanyFinance.LedgerAsync(_db, null, t);
        const string money = "#,##0.00";

        using var wb = new XLWorkbook();
        static void Header(IXLWorksheet ws, params string[] h)
        {
            for (var i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
            ws.Row(1).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
        }

        // 1. Expenses with payer + per-partner split columns
        var ws1 = wb.Worksheets.Add("Expenses");
        var head = new List<string> { "Date", "Category", "Description", "Amount", "Paid by", "Mode", "Split", "Company share" };
        head.AddRange(partners.Select(p => p.Name));
        head.Add("Notes");
        Header(ws1, head.ToArray());
        var r = 2;
        foreach (var e in list)
        {
            ws1.Cell(r, 1).Value = e.Date.Date; ws1.Cell(r, 1).Style.DateFormat.Format = "dd-mmm-yyyy";
            ws1.Cell(r, 2).Value = e.Category;
            ws1.Cell(r, 3).Value = e.Description;
            ws1.Cell(r, 4).Value = e.Amount;
            ws1.Cell(r, 5).Value = e.PayerType == CompanyPayer.Partner ? CompanyMap.PartyName(e.PaidByPartnerId, names) : CompanyFinance.CompanyName;
            ws1.Cell(r, 6).Value = e.PayerType == CompanyPayer.Partner ? "Personal" : (paidByLabels.TryGetValue(e.PaidBy, out var l) ? l : e.PaidBy);
            ws1.Cell(r, 7).Value = e.Splits.Count == 0 ? "None" : e.SplitMethod;
            ws1.Cell(r, 8).Value = e.Splits.Count == 0 ? e.Amount : e.Splits.Where(s => s.PartnerId == null).Sum(s => s.Amount);
            for (var i = 0; i < partners.Count; i++)
                ws1.Cell(r, 9 + i).Value = e.Splits.Where(s => s.PartnerId == partners[i].Id).Sum(s => s.Amount);
            ws1.Cell(r, 9 + partners.Count).Value = e.Notes ?? "";
            r++;
        }
        ws1.Cell(r, 3).Value = "TOTAL";
        ws1.Cell(r, 4).FormulaA1 = $"SUM(D2:D{Math.Max(2, r - 1)})";
        for (var c = 8; c < 9 + partners.Count; c++)
            ws1.Cell(r, c).FormulaA1 = $"SUM({ws1.Cell(2, c).Address.ColumnLetter}2:{ws1.Cell(2, c).Address.ColumnLetter}{Math.Max(2, r - 1)})";
        ws1.Row(r).Style.Font.Bold = true;
        ws1.Column(4).Style.NumberFormat.Format = money;
        for (var c = 8; c < 9 + partners.Count; c++) ws1.Column(c).Style.NumberFormat.Format = money;

        // 2. Partner ledger (range) + balances / settlement (cumulative)
        var ws2 = wb.Worksheets.Add("Partner ledger");
        Header(ws2, "Partner", "Share %", "Paid on behalf", "Share of expenses", "Contributions", "Withdrawals", "Settlements paid", "Settlements received", "Expense balance", "Capital", "Net balance");
        r = 2;
        foreach (var x in ledger)
        {
            object[] v = { x.Name, x.SharePercent, x.PaidOnBehalf, x.ShareOfExpenses, x.Contributions, x.Withdrawals, x.SettlementsPaid, x.SettlementsReceived, x.ExpenseBalance, x.Capital, x.NetBalance };
            for (var i = 0; i < v.Length; i++) ws2.Cell(r, i + 1).Value = XLCellValue.FromObject(v[i]);
            r++;
        }
        ws2.Range(2, 3, Math.Max(2, r), 11).Style.NumberFormat.Format = money;
        r += 1;
        ws2.Cell(r, 1).Value = $"Who owes whom (all entries up to {t:dd MMM yyyy})"; ws2.Row(r).Style.Font.Bold = true; r++;
        var settle = CompanyFinance.Settle(cumulative);
        if (settle.Count == 0) { ws2.Cell(r, 1).Value = "All settled."; r++; }
        foreach (var s in settle) { ws2.Cell(r, 1).Value = $"{s.FromName} pays {s.ToName}"; ws2.Cell(r, 3).Value = s.Amount; ws2.Cell(r, 3).Style.NumberFormat.Format = money; r++; }

        // 3. By category, 4. By month
        var ws3 = wb.Worksheets.Add("By category");
        Header(ws3, "Category", "Entries", "Total", "Partner-paid", "Company-paid");
        r = 2;
        foreach (var g in list.GroupBy(e => e.Category).OrderByDescending(g => g.Sum(e => e.Amount)))
        {
            ws3.Cell(r, 1).Value = g.Key; ws3.Cell(r, 2).Value = g.Count(); ws3.Cell(r, 3).Value = g.Sum(e => e.Amount);
            ws3.Cell(r, 4).Value = g.Where(e => e.PayerType == CompanyPayer.Partner).Sum(e => e.Amount);
            ws3.Cell(r, 5).Value = g.Where(e => e.PayerType != CompanyPayer.Partner).Sum(e => e.Amount);
            r++;
        }
        ws3.Range(2, 3, Math.Max(2, r), 5).Style.NumberFormat.Format = money;

        var ws4 = wb.Worksheets.Add("By month");
        Header(ws4, "Month", "Entries", "Total", "Partner-paid", "Company-paid");
        r = 2;
        foreach (var g in list.GroupBy(e => new DateTime(e.Date.Year, e.Date.Month, 1)).OrderBy(g => g.Key))
        {
            ws4.Cell(r, 1).Value = g.Key.ToString("MMM yyyy"); ws4.Cell(r, 2).Value = g.Count(); ws4.Cell(r, 3).Value = g.Sum(e => e.Amount);
            ws4.Cell(r, 4).Value = g.Where(e => e.PayerType == CompanyPayer.Partner).Sum(e => e.Amount);
            ws4.Cell(r, 5).Value = g.Where(e => e.PayerType != CompanyPayer.Partner).Sum(e => e.Amount);
            r++;
        }
        ws4.Range(2, 3, Math.Max(2, r), 5).Style.NumberFormat.Format = money;

        // 5. Assets register
        var assets = await _db.CompanyAssets.AsNoTracking().Where(a => !a.IsDeleted).OrderBy(a => a.Category).ThenBy(a => a.Name).ToListAsync();
        var ws5 = wb.Worksheets.Add("Assets");
        Header(ws5, "Name", "Category", "Purchase date", "Cost", "Vendor", "Bill no.", "Serial no.", "Location", "Assigned to", "Warranty till", "Status", "Disposal date", "Disposal value");
        r = 2;
        foreach (var a in assets)
        {
            ws5.Cell(r, 1).Value = a.Name; ws5.Cell(r, 2).Value = a.Category;
            ws5.Cell(r, 3).Value = a.PurchaseDate; ws5.Cell(r, 3).Style.DateFormat.Format = "dd-mmm-yyyy";
            ws5.Cell(r, 4).Value = a.Cost; ws5.Cell(r, 5).Value = a.Vendor ?? ""; ws5.Cell(r, 6).Value = a.InvoiceNumber ?? "";
            ws5.Cell(r, 7).Value = a.SerialNumber ?? ""; ws5.Cell(r, 8).Value = a.Location ?? ""; ws5.Cell(r, 9).Value = a.AssignedTo ?? "";
            if (a.WarrantyExpiry != null) { ws5.Cell(r, 10).Value = a.WarrantyExpiry.Value; ws5.Cell(r, 10).Style.DateFormat.Format = "dd-mmm-yyyy"; }
            ws5.Cell(r, 11).Value = a.Status;
            if (a.DisposalDate != null) { ws5.Cell(r, 12).Value = a.DisposalDate.Value; ws5.Cell(r, 12).Style.DateFormat.Format = "dd-mmm-yyyy"; }
            if (a.DisposalValue != null) ws5.Cell(r, 13).Value = a.DisposalValue.Value;
            r++;
        }
        ws5.Cell(r, 3).Value = "In-use value";
        ws5.Cell(r, 4).Value = assets.Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.Lost).Sum(a => a.Cost);
        ws5.Row(r).Style.Font.Bold = true;
        ws5.Column(4).Style.NumberFormat.Format = money; ws5.Column(13).Style.NumberFormat.Format = money;

        // 6. Licences
        var today = CompanyFinance.TodayIst();
        var lic = await _db.CompanyLicences.AsNoTracking().Where(l => !l.IsDeleted).OrderBy(l => l.LicenceType).ThenBy(l => l.StartDate).ToListAsync();
        var ws6 = wb.Worksheets.Add("Licences");
        Header(ws6, "Type", "Name", "Number", "Issuing authority", "Start", "Expiry", "Status", "Fee", "Reminder days");
        r = 2;
        foreach (var l in lic)
        {
            ws6.Cell(r, 1).Value = l.LicenceType; ws6.Cell(r, 2).Value = l.Title; ws6.Cell(r, 3).Value = l.Number ?? ""; ws6.Cell(r, 4).Value = l.IssuingAuthority ?? "";
            if (l.StartDate != null) { ws6.Cell(r, 5).Value = l.StartDate.Value; ws6.Cell(r, 5).Style.DateFormat.Format = "dd-mmm-yyyy"; }
            if (l.ExpiryDate != null) { ws6.Cell(r, 6).Value = l.ExpiryDate.Value; ws6.Cell(r, 6).Style.DateFormat.Format = "dd-mmm-yyyy"; } else ws6.Cell(r, 6).Value = "Lifetime";
            ws6.Cell(r, 7).Value = CompanyFinance.LicenceStatus(l, today);
            ws6.Cell(r, 8).Value = l.Fee; ws6.Cell(r, 9).Value = l.ReminderDays;
            r++;
        }
        ws6.Cell(r, 7).Value = "Total fees"; ws6.Cell(r, 8).Value = lic.Sum(l => l.Fee); ws6.Row(r).Style.Font.Bold = true;
        ws6.Column(8).Style.NumberFormat.Format = money;

        foreach (var ws in wb.Worksheets) ws.Columns().AdjustToContents(8, 60);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Company_finance_{f:yyyyMMdd}_{t:yyyyMMdd}.xlsx");
    }
}
