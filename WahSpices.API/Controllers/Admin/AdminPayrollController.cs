using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/payroll")]
[RequireModule("Payroll")]
public class AdminPayrollController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly WahSpices.Infrastructure.Email.IEmailComposer _email;
    private readonly ILogger<AdminPayrollController> _logger;

    public AdminPayrollController(AppDbContext db, WahSpices.Infrastructure.Email.IEmailComposer email, ILogger<AdminPayrollController> logger)
    {
        _db = db; _email = email; _logger = logger;
    }

    private static string StatusOf(PayrollEntry p) => p.IsDisbursed ? "Paid" : (p.IsApproved ? "Approved" : "Calculated");
    private static bool ValidPeriod(int month, int year) => month is >= 1 and <= 12 && year is >= 2000 and <= 2100;
    private static string MonthLabel(int month, int year) => new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Statutory rates used by payroll (single source for the UI)</summary>
    [HttpGet("statutory")]
    public IActionResult GetStatutory() => Ok(ApiResponse<object>.Ok(PayrollCalculator.Rates()));

    /// <summary>Get payroll entries for a month, with backend-computed EPF / ESIC / PT challan figures</summary>
    [HttpGet]
    public async Task<IActionResult> GetPayroll([FromQuery] int? month, [FromQuery] int? year)
    {
        var m = month ?? DateTime.UtcNow.Month;
        var y = year  ?? DateTime.UtcNow.Year;
        if (!ValidPeriod(m, y)) return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "Invalid month/year."));

        var rows = await _db.PayrollEntries
            .Include(p => p.Employee)
            .Where(p => p.Month == m && p.Year == y && !p.IsDeleted)
            .OrderBy(p => p.Employee.Department).ThenBy(p => p.Employee.Name)
            .ToListAsync();

        var entries = rows.Select(p => new
        {
            p.Id,
            EmployeeId   = p.EmployeeId,
            EmpId        = p.Employee.EmployeeId,
            p.Employee.Name,
            p.Employee.Department,
            p.Employee.Designation,
            HasEmail     = !string.IsNullOrWhiteSpace(p.Employee.Email),
            HasBank      = !string.IsNullOrWhiteSpace(p.Employee.BankAccountNo) && !string.IsNullOrWhiteSpace(p.Employee.BankIfsc),
            p.DaysWorked, p.LossOfPayDays,
            p.BasicSalary, p.Hra, p.Ta, p.Da, SpecialAllowance = p.Bonus,
            p.GrossSalary, p.Epf, p.Esic, p.ProfessionalTax, p.Tds, p.OtherDeductions,
            TotalDeductions = p.Epf + p.Esic + p.ProfessionalTax + p.Tds + p.OtherDeductions,
            p.NetSalary,
            p.IsApproved, p.IsDisbursed, p.DisbursedAt,
            Status = StatusOf(p),
        }).ToList();

        var employer = rows.Select(PayrollCalculator.Employer).ToList();
        var due      = PayrollCalculator.DueDate(m, y);
        var empEpf   = rows.Sum(e => e.Epf);
        var empEsic  = rows.Sum(e => e.Esic);
        var erEpf    = employer.Sum(x => x.EpfTotal);
        var erExtras = employer.Sum(x => x.EpfChallanExtras);
        var erEsic   = employer.Sum(x => x.Esic);

        return Ok(ApiResponse<object>.Ok(new
        {
            Month      = m,
            Year       = y,
            Employees  = rows.Count,
            TotalGross = rows.Sum(e => e.GrossSalary),
            TotalEPF   = empEpf,
            TotalESIC  = empEsic,
            TotalPt    = rows.Sum(e => e.ProfessionalTax),
            TotalTds   = rows.Sum(e => e.Tds),
            TotalDeductions = rows.Sum(e => e.Epf + e.Esic + e.ProfessionalTax + e.Tds + e.OtherDeductions),
            TotalNet   = rows.Sum(e => e.NetSalary),
            Disbursed  = rows.Count(e => e.IsDisbursed),
            Approved   = rows.Count(e => e.IsApproved),
            Statutory  = new
            {
                Rates = PayrollCalculator.Rates(),
                DueDate = due.ToString("yyyy-MM-dd"),
                Epf = new
                {
                    Employee        = empEpf,
                    EmployerEpf     = employer.Sum(x => x.EpfAccount),
                    EmployerEps     = employer.Sum(x => x.Eps),
                    EmployerTotal   = erEpf,
                    Edli            = employer.Sum(x => x.Edli),
                    AdminCharges    = employer.Sum(x => x.Admin),
                    ChallanTotal    = empEpf + erEpf + erExtras,
                    Covered         = rows.Count(e => e.Epf > 0),
                },
                Esic = new
                {
                    Employee      = empEsic,
                    Employer      = erEsic,
                    ChallanTotal  = empEsic + erEsic,
                    Covered       = rows.Count(e => e.Esic > 0),
                    Exempt        = rows.Count(e => e.Esic == 0),
                },
                Pt = new
                {
                    Total   = rows.Sum(e => e.ProfessionalTax),
                    Covered = rows.Count(e => e.ProfessionalTax > 0),
                },
                Tds = rows.Sum(e => e.Tds),
                EmployerCost = rows.Sum(e => e.GrossSalary) + erEpf + erExtras + erEsic,
            },
            Entries    = entries
        }));
    }

    /// <summary>Run (or re-run, while nothing is approved) the payroll calculation for a month</summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunPayroll([FromBody] RunPayrollRequest request)
    {
        if (!ValidPeriod(request.Month, request.Year))
            return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "Invalid month/year."));

        var existing = await _db.PayrollEntries.Where(p => p.Month == request.Month && p.Year == request.Year && !p.IsDeleted).ToListAsync();
        if (existing.Any(p => p.IsApproved || p.IsDisbursed))
            return BadRequest(ApiResponse<object>.Fail("ALREADY_APPROVED", "Payroll for this month is already approved — it can no longer be recalculated."));

        var monthStart  = new DateTime(request.Year, request.Month, 1);
        var monthEnd    = monthStart.AddMonths(1).AddDays(-1);
        var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);

        var employees = await _db.Employees.Where(e => e.IsActive && !e.IsDeleted && e.JoinedAt <= monthEnd).ToListAsync();
        if (employees.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("NO_EMPLOYEES", "No active employees for this month."));

        var empIds = employees.Select(e => e.Id).ToList();
        var structures = await _db.SalaryStructures
            .Where(s => empIds.Contains(s.EmployeeId) && s.IsActive && !s.IsDeleted)
            .ToListAsync();
        var attendance = await _db.AttendanceRecords
            .Where(a => empIds.Contains(a.EmployeeId) && !a.IsDeleted && a.Date >= monthStart && a.Date <= monthEnd)
            .Select(a => new { a.EmployeeId, a.Status })
            .ToListAsync();

        if (existing.Count > 0) _db.PayrollEntries.RemoveRange(existing);

        foreach (var emp in employees)
        {
            var recs   = attendance.Where(a => a.EmployeeId == emp.Id).ToList();
            // Loss of pay = explicit absences (+ half days) + days before joining
            decimal lop = recs.Count(a => a.Status == AttendanceStatus.Absent)
                        + recs.Count(a => a.Status == AttendanceStatus.HalfDay) * 0.5m;
            if (emp.JoinedAt.Date > monthStart) lop += (emp.JoinedAt.Date - monthStart).Days;

            var structure = structures.Where(s => s.EmployeeId == emp.Id).OrderByDescending(s => s.EffectiveFrom).FirstOrDefault();
            var entry = PayrollCalculator.Calculate(emp, structure, request.Month, request.Year, Math.Min(lop, daysInMonth));
            entry.CreatedBy = AdminName;
            _db.PayrollEntries.Add(entry);
        }

        await _db.SaveChangesAsync();

        var verb = existing.Count > 0 ? "recalculated" : "calculated";
        return Ok(ApiResponse<object>.Ok(new { Employees = employees.Count, Status = "Calculated", Recalculated = existing.Count > 0 },
            $"Payroll {verb} for {employees.Count} employees."));
    }

    /// <summary>Approve payroll for the month</summary>
    [HttpPut("approve")]
    public async Task<IActionResult> Approve([FromBody] RunPayrollRequest request)
    {
        var entries = await _db.PayrollEntries
            .Where(p => p.Month == request.Month && p.Year == request.Year && !p.IsApproved && !p.IsDeleted)
            .ToListAsync();
        if (entries.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("NOTHING_TO_APPROVE", "No calculated payroll pending approval for this month."));

        entries.ForEach(e => { e.IsApproved = true; e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = AdminName; });
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { Approved = entries.Count }, "Payroll approved."));
    }

    /// <summary>Mark one employee's (approved) salary as paid</summary>
    [HttpPost("disburse/{entryId:int}")]
    public async Task<IActionResult> Disburse(int entryId)
    {
        var entry = await _db.PayrollEntries.FirstOrDefaultAsync(p => p.Id == entryId && !p.IsDeleted);
        if (entry == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Payroll entry not found."));
        if (entry.IsDisbursed) return BadRequest(ApiResponse<object>.Fail("ALREADY_PAID", "Salary already disbursed."));
        if (!entry.IsApproved) return BadRequest(ApiResponse<object>.Fail("NOT_APPROVED", "Approve the payroll before disbursing salary."));
        entry.IsDisbursed = true;
        entry.DisbursedAt = DateTime.UtcNow;
        entry.UpdatedAt   = DateTime.UtcNow;
        entry.UpdatedBy   = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { entry.Id, Status = StatusOf(entry), entry.DisbursedAt }, "Salary disbursed."));
    }

    /// <summary>Mark all approved salaries for the month as paid</summary>
    [HttpPost("disburse-all")]
    public async Task<IActionResult> DisburseAll([FromBody] RunPayrollRequest request)
    {
        var entries = await _db.PayrollEntries
            .Where(p => p.Month == request.Month && p.Year == request.Year && !p.IsDisbursed && !p.IsDeleted)
            .ToListAsync();
        if (entries.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("NOTHING_TO_DISBURSE", "No pending salaries for this month."));
        if (entries.Any(e => !e.IsApproved))
            return BadRequest(ApiResponse<object>.Fail("NOT_APPROVED", "Approve the payroll before disbursing salaries."));

        var now = DateTime.UtcNow;
        entries.ForEach(e => { e.IsDisbursed = true; e.DisbursedAt = now; e.UpdatedAt = now; e.UpdatedBy = AdminName; });
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { Disbursed = entries.Count }, $"{entries.Count} salaries disbursed."));
    }

    /// <summary>Download the NEFT bulk-payment file (XLSX or CSV) for approved payroll</summary>
    [HttpGet("neft-file")]
    public async Task<IActionResult> GetNeftFile([FromQuery] int month, [FromQuery] int year, [FromQuery] string format = "xlsx")
    {
        if (!ValidPeriod(month, year)) return BadRequest(ApiResponse<object>.Fail("INVALID_PERIOD", "Select a valid month and year."));

        var entries = await _db.PayrollEntries
            .Include(p => p.Employee)
            .Where(p => p.Month == month && p.Year == year && p.IsApproved && !p.IsDeleted)
            .OrderBy(p => p.Employee.Name)
            .ToListAsync();

        if (!entries.Any())
            return BadRequest(ApiResponse<object>.Fail("NO_DATA", "No approved payroll for this period. Approve payroll first."));

        var narration = $"SALARY {new DateTime(year, month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()}";
        var headers = new[] { "Sr No", "Beneficiary Name", "Beneficiary Account No", "IFSC Code", "Amount", "Payment Mode", "Narration", "Employee Code", "Email", "Remarks" };
        var rows = entries.Select((e, i) => new object[]
        {
            i + 1, e.Employee.Name, e.Employee.BankAccountNo ?? "", (e.Employee.BankIfsc ?? "").ToUpperInvariant(),
            e.NetSalary, "NEFT", narration, e.Employee.EmployeeId, e.Employee.Email ?? "",
            string.IsNullOrWhiteSpace(e.Employee.BankAccountNo) || string.IsNullOrWhiteSpace(e.Employee.BankIfsc) ? "MISSING BANK DETAILS"
                : e.IsDisbursed ? "Already marked paid" : "",
        }).ToList();
        var baseName = $"NEFT_Salary_{year}_{month:D2}";

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(Csv)));
            foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(v => Csv(v is decimal d ? d.ToString("0.00", CultureInfo.InvariantCulture) : v?.ToString()))));
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"{baseName}.csv");
        }

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("NEFT");
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                switch (rows[r][c])
                {
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "0.00"; break;
                    case int n: cell.Value = n; break;
                    default: cell.Value = rows[r][c]?.ToString() ?? ""; cell.Style.NumberFormat.Format = "@"; break;
                }
            }
        var totalRow = rows.Count + 2;
        ws.Cell(totalRow, 2).Value = "TOTAL";
        ws.Cell(totalRow, 5).Value = entries.Sum(e => e.NetSalary);
        ws.Cell(totalRow, 5).Style.NumberFormat.Format = "0.00";
        ws.Row(totalRow).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{baseName}.xlsx");
    }

    /// <summary>Printable salary slip (HTML)</summary>
    [HttpGet("slip/{entryId:int}")]
    public async Task<IActionResult> GetSlip(int entryId)
    {
        var entry = await _db.PayrollEntries.Include(p => p.Employee).FirstOrDefaultAsync(p => p.Id == entryId && !p.IsDeleted);
        if (entry == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Payroll entry not found."));
        var html = await BuildSlipHtml(entry, printable: true);
        return Content(html, "text/html", Encoding.UTF8);
    }

    /// <summary>Email the salary slip to the employee</summary>
    [HttpPost("slip/{entryId:int}/send")]
    public async Task<IActionResult> SendSlip(int entryId)
    {
        var entry = await _db.PayrollEntries.Include(p => p.Employee).FirstOrDefaultAsync(p => p.Id == entryId && !p.IsDeleted);
        if (entry == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Payroll entry not found."));
        var emp = entry.Employee;
        if (string.IsNullOrWhiteSpace(emp.Email))
            return BadRequest(ApiResponse<object>.Fail("NO_EMAIL", $"{emp.Name} has no email address. Add one in Staff first."));

        var slip = BuildSlipEmailFragment(entry);
        var month = MonthLabel(entry.Month, entry.Year);
        try
        {
            await _email.SendNowAsync(emp.Email!, emp.Name, "payroll-slip",
                b => WahSpices.Infrastructure.Email.EmailTemplates.SalarySlip(b, emp.Name, month, slip));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send salary slip {EntryId}", entryId);
            return StatusCode(502, ApiResponse<object>.Fail("EMAIL_FAILED", "Could not send the email. Check the mail settings."));
        }
        entry.SlipUrl   = $"/api/admin/payroll/slip/{entry.Id}";
        entry.UpdatedAt = DateTime.UtcNow;
        entry.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { SentTo = emp.Email }, $"Salary slip sent to {emp.Email}."));
    }

    /// <summary>The salary slip content (same rows as the printable slip) with inline styles, for the branded email layout.</summary>
    internal static string BuildSlipEmailFragment(PayrollEntry e)
    {
        var emp = e.Employee;
        string H(string? v) => WebUtility.HtmlEncode(v ?? "");
        string M(decimal v) => "₹" + v.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        const string font = "font-family:'Poppins',Arial,Helvetica,sans-serif;";
        var earnings = new (string, decimal)[] { ("Basic", e.BasicSalary), ("HRA", e.Hra), ("Conveyance (TA)", e.Ta), ("Dearness Allowance", e.Da), ("Special Allowance", e.Bonus), ("Overtime", e.OvertimePay) }
            .Where(x => x.Item2 != 0 || x.Item1 == "Basic").ToList();
        var deductions = new (string, decimal)[] { ("Provident Fund (EPF)", e.Epf), ("ESIC", e.Esic), ("Professional Tax", e.ProfessionalTax), ("TDS", e.Tds), ("Other", e.OtherDeductions) }
            .Where(x => x.Item2 != 0).ToList();
        var totalDed = e.Epf + e.Esic + e.ProfessionalTax + e.Tds + e.OtherDeductions;
        var acct = string.IsNullOrWhiteSpace(emp.BankAccountNo) ? null : "XXXX" + emp.BankAccountNo[^Math.Min(4, emp.BankAccountNo.Length)..];
        var td = $"{font}font-size:12px;line-height:17px;color:#1d2a20;padding:7px 8px;border-bottom:1px solid #efe3cf";
        var th = $"{font}font-size:11px;line-height:15px;color:#ffffff;background:#1a5c2e;padding:8px;text-align:left";
        string Info(string k, string? v) => $"<tr><td style=\"{font}font-size:12px;color:#6d736b;padding:3px 8px 3px 0;width:45%\">{H(k)}</td><td style=\"{font}font-size:12px;color:#1d2a20;font-weight:600;padding:3px 0\">{H(string.IsNullOrWhiteSpace(v) ? "—" : v)}</td></tr>";
        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:#fff9f0;border:1px solid #efe3cf;border-radius:12px\"><tr><td style=\"padding:12px 14px\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td class=\"stack\" valign=\"top\" width=\"50%\"><table role=\"presentation\" width=\"100%\">");
        sb.Append(Info("Employee", emp.Name)).Append(Info("Employee code", emp.EmployeeId)).Append(Info("Department", emp.Department)).Append(Info("Designation", emp.Designation));
        sb.Append(Info("Date of joining", emp.JoinedAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
        sb.Append("</table></td><td class=\"stack\" valign=\"top\" width=\"50%\"><table role=\"presentation\" width=\"100%\">");
        sb.Append(Info("Paid days", e.DaysWorked.ToString())).Append(Info("Loss of pay days", e.LossOfPayDays.ToString())).Append(Info("PAN", emp.PanNumber))
          .Append(Info("PF no.", emp.PfNumber)).Append(Info("ESIC no.", emp.EsicNumber)).Append(Info("Bank A/c", acct));
        sb.Append("</table></td></tr></table></td></tr></table>");
        sb.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin-top:14px\"><tr><th style=\"{th}\">Earnings</th><th style=\"{th};text-align:right\">Amount</th><th style=\"{th}\">Deductions</th><th style=\"{th};text-align:right\">Amount</th></tr>");
        for (var i = 0; i < Math.Max(earnings.Count, deductions.Count); i++)
        {
            sb.Append("<tr>");
            sb.Append(i < earnings.Count ? $"<td style=\"{td}\">{H(earnings[i].Item1)}</td><td style=\"{td};text-align:right;white-space:nowrap\">{M(earnings[i].Item2)}</td>" : $"<td style=\"{td}\"></td><td style=\"{td}\"></td>");
            sb.Append(i < deductions.Count ? $"<td style=\"{td}\">{H(deductions[i].Item1)}</td><td style=\"{td};text-align:right;white-space:nowrap\">{M(deductions[i].Item2)}</td>" : $"<td style=\"{td}\"></td><td style=\"{td}\"></td>");
            sb.Append("</tr>");
        }
        sb.Append($"<tr><td style=\"{td};font-weight:700\">Gross earnings</td><td style=\"{td};text-align:right;font-weight:700;white-space:nowrap\">{M(e.GrossSalary)}</td><td style=\"{td};font-weight:700\">Total deductions</td><td style=\"{td};text-align:right;font-weight:700;white-space:nowrap\">{M(totalDed)}</td></tr></table>");
        sb.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin-top:14px;background:#e8f3ec;border-radius:10px\"><tr><td style=\"{font}padding:12px 14px;font-size:15px;font-weight:700;color:#0f3d1e\">Net pay</td><td align=\"right\" style=\"{font}padding:12px 14px;font-size:20px;font-weight:700;color:#0f3d1e;white-space:nowrap\">{M(e.NetSalary)}</td></tr></table>");
        sb.Append($"<p style=\"{font}font-size:12px;color:#6d736b;margin:10px 0 0\">Status: {StatusOf(e)}{(e.DisbursedAt.HasValue ? " on " + e.DisbursedAt.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "")}.</p>");
        return sb.ToString();
    }

    private async Task<string> BuildSlipHtml(PayrollEntry e, bool printable)
    {
        var s   = await _db.SiteSettings.AsNoTracking().FirstOrDefaultAsync() ?? new SiteSettings();
        var emp = e.Employee;
        string H(string? v) => WebUtility.HtmlEncode(v ?? "");
        string M(decimal v) => "₹" + v.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        var earnings = new (string, decimal)[] { ("Basic", e.BasicSalary), ("HRA", e.Hra), ("Conveyance (TA)", e.Ta), ("Dearness Allowance", e.Da), ("Special Allowance", e.Bonus), ("Overtime", e.OvertimePay) }
            .Where(x => x.Item2 != 0 || x.Item1 == "Basic").ToList();
        var deductions = new (string, decimal)[] { ("Provident Fund (EPF)", e.Epf), ("ESIC", e.Esic), ("Professional Tax", e.ProfessionalTax), ("TDS", e.Tds), ("Other", e.OtherDeductions) }
            .Where(x => x.Item2 != 0).ToList();
        var totalDed = e.Epf + e.Esic + e.ProfessionalTax + e.Tds + e.OtherDeductions;
        var n = Math.Max(earnings.Count, deductions.Count);
        var body = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            body.Append("<tr>");
            body.Append(i < earnings.Count ? $"<td>{H(earnings[i].Item1)}</td><td class='r'>{M(earnings[i].Item2)}</td>" : "<td></td><td></td>");
            body.Append(i < deductions.Count ? $"<td>{H(deductions[i].Item1)}</td><td class='r'>{M(deductions[i].Item2)}</td>" : "<td></td><td></td>");
            body.Append("</tr>");
        }
        string Info(string k, string? v) => $"<tr><td class='k'>{H(k)}</td><td>{H(string.IsNullOrWhiteSpace(v) ? "—" : v)}</td></tr>";
        var acct = string.IsNullOrWhiteSpace(emp.BankAccountNo) ? null : "XXXX" + emp.BankAccountNo[^Math.Min(4, emp.BankAccountNo.Length)..];

        return $@"<!doctype html><html><head><meta charset='utf-8'><title>Salary slip — {H(emp.Name)} — {MonthLabel(e.Month, e.Year)}</title>
<style>
body{{font-family:Arial,Helvetica,sans-serif;color:#222;margin:24px;font-size:13px}}
.wrap{{max-width:760px;margin:0 auto;border:1px solid #ccc;padding:20px}}
h1{{font-size:20px;margin:0}} h2{{font-size:15px;margin:14px 0 8px;text-align:center;letter-spacing:.5px}}
.muted{{color:#666;font-size:12px}} table{{width:100%;border-collapse:collapse}}
.info td{{padding:3px 6px}} .info .k{{color:#666;width:30%}}
.pay th,.pay td{{border:1px solid #ddd;padding:6px 8px;text-align:left}} .pay th{{background:#f4f4f4}}
.r{{text-align:right!important}} .net{{margin-top:14px;padding:10px;background:#f4f4f4;font-size:15px;font-weight:bold;display:flex;justify-content:space-between}}
@media print{{.noprint{{display:none}} body{{margin:0}} .wrap{{border:0}}}}
</style></head><body><div class='wrap'>
{(printable ? "<div class='noprint' style='text-align:right;margin-bottom:8px'><button onclick='window.print()'>Print / Save as PDF</button></div>" : "")}
<h1>{H(s.SiteName)}</h1>
<div class='muted'>{H(s.Address)}{(string.IsNullOrWhiteSpace(s.Gstin) ? "" : " • GSTIN " + H(s.Gstin))}{(string.IsNullOrWhiteSpace(s.Phone) ? "" : " • " + H(s.Phone))}</div>
<h2>SALARY SLIP — {MonthLabel(e.Month, e.Year).ToUpperInvariant()}</h2>
<table class='info'><tr><td style='width:50%;vertical-align:top'><table>
{Info("Employee", emp.Name)}{Info("Employee code", emp.EmployeeId)}{Info("Department", emp.Department)}{Info("Designation", emp.Designation)}
</table></td><td style='vertical-align:top'><table>
{Info("Date of joining", emp.JoinedAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))}{Info("Paid days", e.DaysWorked.ToString())}{Info("Loss of pay days", e.LossOfPayDays.ToString())}
{Info("PAN", emp.PanNumber)}{Info("PF no.", emp.PfNumber)}{Info("ESIC no.", emp.EsicNumber)}{Info("Bank A/c", acct)}
</table></td></tr></table>
<table class='pay' style='margin-top:12px'><thead><tr><th>Earnings</th><th class='r'>Amount</th><th>Deductions</th><th class='r'>Amount</th></tr></thead>
<tbody>{body}</tbody>
<tfoot><tr><th>Gross earnings</th><th class='r'>{M(e.GrossSalary)}</th><th>Total deductions</th><th class='r'>{M(totalDed)}</th></tr></tfoot></table>
<div class='net'><span>Net pay</span><span>{M(e.NetSalary)}</span></div>
<p class='muted'>Status: {StatusOf(e)}{(e.DisbursedAt.HasValue ? " on " + e.DisbursedAt.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "")}. This is a computer-generated salary slip and does not require a signature.</p>
</div></body></html>";
    }

    private static string Csv(string? v)
    {
        v ??= "";
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    /// <summary>Get salary structures for all active employees</summary>
    [HttpGet("salary-structures")]
    public async Task<IActionResult> GetSalaryStructures()
    {
        var employees = await _db.Employees
            .Where(e => e.IsActive && !e.IsDeleted)
            .OrderBy(e => e.Department).ThenBy(e => e.Name)
            .ToListAsync();

        var empIds    = employees.Select(e => e.Id).ToList();
        var structures = await _db.SalaryStructures
            .Where(s => empIds.Contains(s.EmployeeId) && s.IsActive && !s.IsDeleted)
            .ToListAsync();

        var result = employees.Select(emp =>
        {
            var s = structures.Where(x => x.EmployeeId == emp.Id).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
            var c = PayrollCalculator.MonthlyComponents(emp, s);
            var d = PayrollCalculator.EmployeeDeductions(c, c.Gross, s);
            return new
            {
                EmployeeId      = emp.Id,
                EmpCode         = emp.EmployeeId,
                EmployeeName    = emp.Name,
                Department      = emp.Department,
                Designation     = emp.Designation,
                BasicSalary     = c.Basic,
                Hra             = c.Hra,
                Ta              = c.Ta,
                Da              = c.Da,
                SpecialAllowance= c.Special,
                GrossSalary     = c.Gross,
                EpfPercent      = s?.PfPercent ?? PayrollCalculator.EpfEmployeeRate,
                EsicPercent     = s?.EsicPercent ?? PayrollCalculator.EsicEmployeeRate,
                Epf             = d.Epf,
                Esic            = d.Esic,
                ProfessionalTax = d.ProfessionalTax,
                EsicApplicable  = d.EsicCovered,
                NetSalary       = c.Gross - d.Total,
                EffectiveFrom   = s?.EffectiveFrom,
                HasStructure    = s != null,
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(new { Structures = result, Employees = result.Count }));
    }

    /// <summary>Get payroll summary for past months</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int months = 12)
    {
        var all = await _db.PayrollEntries.Where(p => !p.IsDeleted).ToListAsync();

        var history = all
            .GroupBy(p => new { p.Year, p.Month })
            .Select(g => new
            {
                Year       = g.Key.Year,
                Month      = g.Key.Month,
                MonthLabel = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture),
                Employees  = g.Count(),
                TotalGross = g.Sum(p => p.GrossSalary),
                TotalEpf   = g.Sum(p => p.Epf),
                TotalEsic  = g.Sum(p => p.Esic),
                TotalPt    = g.Sum(p => p.ProfessionalTax),
                TotalNet   = g.Sum(p => p.NetSalary),
                Disbursed  = g.Count(p => p.IsDisbursed),
                Status     = g.All(p => p.IsDisbursed) ? "Paid"
                           : g.All(p => p.IsApproved)  ? "Approved"
                           : g.Any(p => p.IsApproved)  ? "Partly approved"
                           :                              "Calculated",
            })
            .OrderByDescending(g => g.Year).ThenByDescending(g => g.Month)
            .Take(months)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new { History = history }));
    }
}
