using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/expenses")]
[RequireModule("Expenses")]
public class AdminExpenseController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminExpenseController(AppDbContext db) => _db = db;

    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var cats = await _db.ExpenseCategoryConfigs.Where(c => !c.IsDeleted).OrderBy(c => c.Name).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new {
            categories = cats.Select(c => new { c.Id, c.Name, c.Color, c.IsActive })
        }));
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Name is required."));
        var exists = await _db.ExpenseCategoryConfigs.AnyAsync(c => c.Name == req.Name && !c.IsDeleted);
        if (exists) return BadRequest(ApiResponse<object>.Fail("DUPLICATE", "Category already exists."));
        var cat = new ExpenseCategoryConfig { Name = req.Name.Trim(), Color = req.Color ?? "gray", IsActive = true, CreatedBy = AdminName };
        _db.ExpenseCategoryConfigs.Add(cat);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { cat.Id, cat.Name, cat.Color, cat.IsActive }));
    }

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryRequest req)
    {
        var cat = await _db.ExpenseCategoryConfigs.FindAsync(id);
        if (cat == null || cat.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Category not found."));
        var newName = string.IsNullOrWhiteSpace(req.Name) ? cat.Name : req.Name.Trim();
        if (newName != cat.Name)
        {
            if (await _db.ExpenseCategoryConfigs.AnyAsync(c => c.Id != id && c.Name == newName && !c.IsDeleted))
                return BadRequest(ApiResponse<object>.Fail("DUPLICATE", "Category already exists."));
            // keep existing expenses and budgets attached to the renamed category
            var oldName = cat.Name;
            await _db.ExpenseEntries.Where(e => e.Category == oldName).ExecuteUpdateAsync(u => u.SetProperty(e => e.Category, newName));
            await _db.ExpenseBudgets.Where(b => b.Category == oldName).ExecuteUpdateAsync(u => u.SetProperty(b => b.Category, newName));
        }
        cat.Name = newName;
        cat.Color = req.Color ?? cat.Color;
        cat.IsActive = req.IsActive ?? cat.IsActive;
        cat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { cat.Id, cat.Name, cat.Color, cat.IsActive }));
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var cat = await _db.ExpenseCategoryConfigs.FindAsync(id);
        if (cat == null || cat.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Category not found."));
        cat.IsDeleted = true; cat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Expenses ──────────────────────────────────────────────────────────────

    private IQueryable<ExpenseEntry> Filtered(int m, int y, string? category, string? search, string? paidBy)
    {
        var q = _db.ExpenseEntries.Where(e => e.Date.Month == m && e.Date.Year == y && !e.IsDeleted);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(paidBy))   q = q.Where(e => e.PaidBy == paidBy);
        if (!string.IsNullOrWhiteSpace(search))   q = q.Where(e => e.Description.Contains(search) || (e.Notes != null && e.Notes.Contains(search)));
        return q;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? category, [FromQuery] string? search, [FromQuery] string? paidBy, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        var m = month ?? DateTime.UtcNow.Month;
        var y = year  ?? DateTime.UtcNow.Year;
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 2000);
        var q = Filtered(m, y, category, search, paidBy);
        var total = await q.CountAsync();
        var list = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Skip((page - 1) * limit).Take(limit).ToListAsync();
        var monthQ = _db.ExpenseEntries.Where(e => e.Date.Month == m && e.Date.Year == y && !e.IsDeleted);
        var summary = await monthQ
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync();
        var byPaidBy = await monthQ
            .GroupBy(e => e.PaidBy)
            .Select(g => new { PaidBy = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count(), Approved = g.Count(x => x.IsApproved) })
            .ToListAsync();
        var totals = new
        {
            Amount   = summary.Sum(x => x.Total),
            Count    = summary.Sum(x => x.Count),
            Pending  = await monthQ.CountAsync(e => !e.IsApproved),
            PendingAmount = await monthQ.Where(e => !e.IsApproved).SumAsync(e => (decimal?)e.Amount) ?? 0,
        };
        var partnerNames = await CompanyMap.PartnerNamesAsync(_db);   // company module: who paid (partner) + split
        return Ok(ApiResponse<object>.Ok(new {
            expenses = list.Select(e => new { e.Id, e.Date, e.Category, e.Description, e.Amount, e.PaidBy, e.IsApproved, e.ApprovedBy, e.Notes,
                e.PayerType, e.PaidByPartnerId, PaidByName = e.PayerType == CompanyPayer.Partner ? CompanyMap.PartyName(e.PaidByPartnerId, partnerNames) : null,
                e.SplitMethod, e.SourceType, HasReceipt = e.ReceiptUrl != null }),
            summary,
            byPaidBy,
            totals,
            pagination = new { total, page, limit }
        }));
    }

    /// <summary>Export the month's expenses (respecting filters) as XLSX or CSV</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? category, [FromQuery] string? search, [FromQuery] string? paidBy, [FromQuery] string format = "xlsx")
    {
        var m = month ?? DateTime.UtcNow.Month;
        var y = year  ?? DateTime.UtcNow.Year;
        if (m is < 1 or > 12 || y is < 2000 or > 2100) return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "Invalid month/year."));
        var list = await Filtered(m, y, category, search, paidBy).OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync();
        var paidByLabels = await _db.MasterDataValues.Where(v => v.Type == "ExpensePaidBy").ToDictionaryAsync(v => v.Value, v => v.Label);
        var partnerNames = await CompanyMap.PartnerNamesAsync(_db);
        string PaidByLabel(string v) => paidByLabels.TryGetValue(v, out var l) ? l : v;

        var headers = new[] { "Date", "Category", "Description", "Amount", "Paid By", "Status", "Approved By", "Notes" };
        var baseName = $"Expenses_{y}_{m:D2}" + (string.IsNullOrWhiteSpace(category) ? "" : "_" + string.Concat(category.Where(char.IsLetterOrDigit)));

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            static string Csv(string? v) { v ??= ""; return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v; }
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers));
            foreach (var e in list)
                sb.AppendLine(string.Join(",", new[] { e.Date.ToString("yyyy-MM-dd"), e.Category, e.Description, e.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    e.PayerType == CompanyPayer.Partner ? "Partner: " + CompanyMap.PartyName(e.PaidByPartnerId, partnerNames) : PaidByLabel(e.PaidBy), e.IsApproved ? "Approved" : "Pending", e.ApprovedBy, e.Notes }.Select(Csv)));
            sb.AppendLine(string.Join(",", new[] { "", "", "TOTAL", list.Sum(e => e.Amount).ToString("0.00", CultureInfo.InvariantCulture), "", "", "", "" }));
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", baseName + ".csv");
        }

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expenses");
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var e in list)
        {
            ws.Cell(r, 1).Value = e.Date.Date; ws.Cell(r, 1).Style.DateFormat.Format = "dd-mmm-yyyy";
            ws.Cell(r, 2).Value = e.Category;
            ws.Cell(r, 3).Value = e.Description;
            ws.Cell(r, 4).Value = e.Amount; ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 5).Value = e.PayerType == CompanyPayer.Partner ? "Partner: " + CompanyMap.PartyName(e.PaidByPartnerId, partnerNames) : PaidByLabel(e.PaidBy);
            ws.Cell(r, 6).Value = e.IsApproved ? "Approved" : "Pending";
            ws.Cell(r, 7).Value = e.ApprovedBy ?? "";
            ws.Cell(r, 8).Value = e.Notes ?? "";
            r++;
        }
        ws.Cell(r, 3).Value = "TOTAL";
        ws.Cell(r, 4).Value = list.Sum(e => e.Amount); ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Row(r).Style.Font.Bold = true;

        // Category summary sheet (budget vs actual)
        var budgets = await _db.ExpenseBudgets.Where(b => !b.IsDeleted).ToDictionaryAsync(b => b.Category, b => b.MonthlyLimit);
        var sum = wb.Worksheets.Add("By category");
        sum.Cell(1, 1).Value = "Category"; sum.Cell(1, 2).Value = "Entries"; sum.Cell(1, 3).Value = "Spent"; sum.Cell(1, 4).Value = "Monthly budget"; sum.Cell(1, 5).Value = "Variance";
        sum.Row(1).Style.Font.Bold = true;
        var sr = 2;
        foreach (var g in list.GroupBy(e => e.Category).OrderBy(g => g.Key))
        {
            var spent = g.Sum(e => e.Amount);
            sum.Cell(sr, 1).Value = g.Key; sum.Cell(sr, 2).Value = g.Count(); sum.Cell(sr, 3).Value = spent;
            if (budgets.TryGetValue(g.Key, out var lim)) { sum.Cell(sr, 4).Value = lim; sum.Cell(sr, 5).Value = lim - spent; }
            sr++;
        }
        sum.Range(2, 3, Math.Max(2, sr), 5).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents(); sum.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", baseName + ".xlsx");
    }

    private static string? Validate(ExpenseRequest req)
    {
        if (req.Date == default) return "Date is required.";
        if (string.IsNullOrWhiteSpace(req.Category)) return "Category is required.";
        if (string.IsNullOrWhiteSpace(req.Description)) return "Description is required.";
        if (req.Amount <= 0) return "Amount must be greater than zero.";
        if (string.IsNullOrWhiteSpace(req.PaidBy)) return "Paid by is required.";
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExpenseRequest req)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("INVALID", err));
        if (req.PaidBy == CompanyPayer.Partner) return BadRequest(ApiResponse<object>.Fail("INVALID", "Record partner-paid expenses in Company → Expenses & split."));
        var e = new ExpenseEntry { Date = req.Date, Category = req.Category, Description = req.Description, Amount = req.Amount, PaidBy = req.PaidBy, Notes = req.Notes, CreatedBy = AdminName };
        _db.ExpenseEntries.Add(e);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { e.Id, e.Date, e.Category, e.Description, e.Amount, e.PaidBy, e.IsApproved, e.Notes }));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExpenseRequest req)
    {
        var e = await _db.ExpenseEntries.FindAsync(id);
        if (e == null || e.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Expense not found."));
        var err = Validate(req);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("INVALID", err));
        // Partner-paid expenses keep their payer here (change it in Company → Expenses & split)
        if ((e.PayerType == CompanyPayer.Partner) != (req.PaidBy == CompanyPayer.Partner))
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Change who paid (partner / company) in Company → Expenses & split."));
        var amountChanged = e.Amount != req.Amount;
        e.Date = req.Date; e.Category = req.Category; e.Description = req.Description; e.Amount = req.Amount; e.PaidBy = req.PaidBy; e.Notes = req.Notes; e.UpdatedAt = DateTime.UtcNow;
        if (amountChanged) await WahSpices.Infrastructure.Services.CompanyFinance.ResplitAsync(_db, e);   // keep partner split rows in step
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { e.Id, e.Date, e.Category, e.Description, e.Amount, e.PaidBy, e.IsApproved, e.Notes }));
    }

    [HttpPut("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var e = await _db.ExpenseEntries.FindAsync(id);
        if (e == null || e.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Expense not found."));
        if (e.IsApproved) return BadRequest(ApiResponse<object>.Fail("ALREADY_APPROVED", "Expense is already approved."));
        e.IsApproved = true; e.ApprovedBy = AdminName; e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { e.Id, e.IsApproved, e.ApprovedBy }));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.ExpenseEntries.FindAsync(id);
        if (e == null || e.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Expense not found."));
        e.IsDeleted = true; e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Budgets ───────────────────────────────────────────────────────────────

    [HttpGet("budgets")]
    public async Task<IActionResult> GetBudgets()
    {
        var budgets = await _db.ExpenseBudgets.Where(b => !b.IsDeleted).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new {
            budgets = budgets.Select(b => new { b.Id, b.Category, b.MonthlyLimit })
        }));
    }

    [HttpPut("budgets/{category}")]
    public async Task<IActionResult> SetBudget(string category, [FromBody] SetBudgetRequest req)
    {
        if (req.MonthlyLimit < 0) return BadRequest(ApiResponse<object>.Fail("INVALID", "Budget cannot be negative."));
        var budget = await _db.ExpenseBudgets.FirstOrDefaultAsync(b => b.Category == category && !b.IsDeleted);
        if (budget == null)
        {
            budget = new ExpenseBudget { Category = category, MonthlyLimit = req.MonthlyLimit, CreatedBy = AdminName };
            _db.ExpenseBudgets.Add(budget);
        }
        else { budget.MonthlyLimit = req.MonthlyLimit; budget.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { budget.Id, budget.Category, budget.MonthlyLimit }));
    }
}

public record ExpenseRequest(DateTime Date, string Category, string Description, decimal Amount, string PaidBy, string? Notes);
public record SetBudgetRequest(decimal MonthlyLimit);
public record CategoryRequest(string? Name, string? Color, bool? IsActive);
