using Microsoft.AspNetCore.Mvc;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Controllers.Admin;

// ─────────────────────────────────────────────────────────────────────────────
// STAFF
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/staff")]
[RequireModule("Staff")]
public class AdminStaffController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminStaffController(AppDbContext db) => _db = db;
    private static string? NullIfBlank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    [RequireModule("Staff", "Attendance", "Leave", "Payroll")]   // employee picker
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? dept, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.Employees.AsQueryable();
        if (!string.IsNullOrWhiteSpace(dept)) q = q.Where(e => e.Department == dept);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => e.Name.Contains(search) || e.Designation.Contains(search));

        var total = await q.CountAsync();
        var list = await q.OrderBy(e => e.Name).Skip((page - 1) * limit).Take(limit)
            .Select(e => new EmployeeDto(e.Id, e.EmployeeId, e.Name, e.Email, e.Phone, e.Department, e.Designation, e.EmploymentType.ToString(), e.Salary, e.JoinedAt, e.IsActive, e.ConfirmationDate, e.NoticePeriodDays, e.Branch, SensitiveMask.Aadhaar(e.AadharNumber), SensitiveMask.Tail(e.PanNumber), SensitiveMask.Tail(e.BankAccountNo), e.BankIfsc, SensitiveMask.Tail(e.PfNumber), SensitiveMask.Tail(e.EsicNumber), e.ReportingManagerId, e.PhotoUrl))
            .ToListAsync();

        // The list doubles as the employee picker for Attendance/Leave — only Staff/Payroll users see pay & ID fields
        if (!await CanSeePayAsync())
            list = list.Select(d => d with { Salary = 0, AadharNumber = null, PanNumber = null, BankAccountNo = null, BankIfsc = null, PfNumber = null, EsicNumber = null }).ToList();

        return Ok(ApiResponse<object>.Ok(new { Employees = list, Pagination = new { total, page, limit } }));
    }

    private async Task<bool> CanSeePayAsync()
    {
        if (!int.TryParse(AdminId, out var adminId)) return false;
        var admin = await _db.AdminUsers.AsNoTracking().Include(a => a.AdminRole).FirstOrDefaultAsync(a => a.Id == adminId && !a.IsDeleted);
        if (admin == null) return false;
        var perms = AdminPermissions.Effective(admin);
        return perms.GetValueOrDefault("Staff") || perms.GetValueOrDefault("Payroll");
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var e = await _db.Employees.FindAsync(id);
        if (e == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        return Ok(ApiResponse<EmployeeDto>.Ok(new EmployeeDto(e.Id, e.EmployeeId, e.Name, e.Email, e.Phone, e.Department, e.Designation, e.EmploymentType.ToString(), e.Salary, e.JoinedAt, e.IsActive, e.ConfirmationDate, e.NoticePeriodDays, e.Branch, SensitiveMask.Aadhaar(e.AadharNumber), SensitiveMask.Tail(e.PanNumber), SensitiveMask.Tail(e.BankAccountNo), e.BankIfsc, SensitiveMask.Tail(e.PfNumber), SensitiveMask.Tail(e.EsicNumber), e.ReportingManagerId, e.PhotoUrl)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Name is required.", "name"));
        if (string.IsNullOrWhiteSpace(request.Phone)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Phone is required.", "phone"));
        if (string.IsNullOrWhiteSpace(request.Department)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Department is required.", "department"));
        if (!Enum.TryParse<EmploymentType>(request.EmploymentType?.Replace(" ", ""), true, out var empType))
            return BadRequest(ApiResponse<object>.Fail("INVALID", $"Invalid employment type '{request.EmploymentType}'.", "employmentType"));
        if (request.Salary < 0) return BadRequest(ApiResponse<object>.Fail("INVALID", "Salary cannot be negative.", "salary"));

        // Next code = highest existing EMP-### + 1 (count+1 collides once any row is removed)
        var codes = await _db.Employees.IgnoreQueryFilters().Where(e => e.EmployeeId.StartsWith("EMP-")).Select(e => e.EmployeeId).ToListAsync();
        var next = codes.Select(c => int.TryParse(c[4..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var emp = new Employee
        {
            EmployeeId         = $"EMP-{next:D3}",
            Name               = request.Name.Trim(),
            Email              = request.Email,
            Phone              = request.Phone,
            Department         = request.Department,
            Designation        = request.Designation,
            EmploymentType     = empType,
            Salary             = request.Salary,
            JoinedAt           = request.JoinedAt,
            ConfirmationDate   = request.ConfirmationDate,
            NoticePeriodDays   = request.NoticePeriodDays,
            Branch             = request.Branch,
            AadharNumber       = request.AadharNumber,
            PanNumber          = request.PanNumber,
            BankAccountNo      = request.BankAccountNo,
            BankIfsc           = request.BankIfsc,
            PfNumber           = request.PfNumber,
            EsicNumber         = request.EsicNumber,
            ReportingManagerId = request.ReportingManagerId,
            PhotoUrl           = request.PhotoUrl,
            CreatedBy          = AdminName,
        };
        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { emp.Id, emp.EmployeeId }, "Employee added."));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FullUpdateEmployeeRequest request)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        if (request.Name != null) emp.Name = request.Name;
        if (request.Email != null) emp.Email = request.Email;
        if (request.Phone != null) emp.Phone = request.Phone;
        if (request.Department != null) emp.Department = request.Department;
        if (request.Designation != null) emp.Designation = request.Designation;
        if (request.EmploymentType != null)
        {
            if (!Enum.TryParse<EmploymentType>(request.EmploymentType.Replace(" ", ""), true, out var et))
                return BadRequest(ApiResponse<object>.Fail("INVALID", $"Invalid employment type '{request.EmploymentType}'.", "employmentType"));
            emp.EmploymentType = et;
        }
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Name is required.", "name"));
        if (request.Salary.HasValue) emp.Salary = request.Salary.Value;
        if (request.JoinedAt.HasValue) emp.JoinedAt = request.JoinedAt.Value;
        if (request.ConfirmationDate.HasValue) emp.ConfirmationDate = request.ConfirmationDate;
        if (request.NoticePeriodDays.HasValue) emp.NoticePeriodDays = request.NoticePeriodDays.Value;
        if (request.Branch != null) emp.Branch = request.Branch;
        // The edit form shows MASKED values; an unchanged masked value echoed back must not overwrite the real one.
        if (request.AadharNumber != null && !SensitiveMask.IsEchoOfMask(request.AadharNumber, emp.AadharNumber, SensitiveMask.Aadhaar)) emp.AadharNumber = NullIfBlank(request.AadharNumber);
        if (request.PanNumber != null && !SensitiveMask.IsEchoOfMask(request.PanNumber, emp.PanNumber, v => SensitiveMask.Tail(v))) emp.PanNumber = NullIfBlank(request.PanNumber)?.ToUpperInvariant();
        if (request.BankAccountNo != null && !SensitiveMask.IsEchoOfMask(request.BankAccountNo, emp.BankAccountNo, v => SensitiveMask.Tail(v))) emp.BankAccountNo = NullIfBlank(request.BankAccountNo);
        if (request.BankIfsc != null) emp.BankIfsc = NullIfBlank(request.BankIfsc)?.ToUpperInvariant();
        if (request.PfNumber != null && !SensitiveMask.IsEchoOfMask(request.PfNumber, emp.PfNumber, v => SensitiveMask.Tail(v))) emp.PfNumber = NullIfBlank(request.PfNumber);
        if (request.EsicNumber != null && !SensitiveMask.IsEchoOfMask(request.EsicNumber, emp.EsicNumber, v => SensitiveMask.Tail(v))) emp.EsicNumber = NullIfBlank(request.EsicNumber);
        if (request.ReportingManagerId != null) emp.ReportingManagerId = request.ReportingManagerId == "" ? null : request.ReportingManagerId;
        if (request.PhotoUrl != null) emp.PhotoUrl = request.PhotoUrl;
        if (request.IsActive.HasValue) emp.IsActive = request.IsActive.Value;
        emp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        emp.IsActive = false;
        emp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Employee deactivated."));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ATTENDANCE
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/attendance")]
[RequireModule("Attendance")]
public class AdminAttendanceController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminAttendanceController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetForMonth([FromQuery] int? month, [FromQuery] int? year, [FromQuery] int? empId)
    {
        var m = month ?? DateTime.UtcNow.Month;
        var y = year  ?? DateTime.UtcNow.Year;
        var query = _db.AttendanceRecords.Where(a => !a.IsDeleted && a.Date.Month == m && a.Date.Year == y);
        if (empId.HasValue) query = query.Where(a => a.EmployeeId == empId.Value);
        var records = await query.Include(a => a.Employee).OrderBy(a => a.EmployeeId).ThenBy(a => a.Date).ToListAsync();
        return Ok(ApiResponse<object>.Ok(records.Select(a => new {
            a.Id, a.EmployeeId, a.Employee.Name,
            Date = a.Date.Date.ToString("yyyy-MM-dd"),
            Status = a.Status.ToString(),
            InTime = a.InTime.HasValue ? a.InTime.Value.ToString(@"hh\:mm") : null,
            OutTime = a.OutTime.HasValue ? a.OutTime.Value.ToString(@"hh\:mm") : null,
            a.OvertimeHours, a.Remark
        })));
    }

    [HttpPost("mark")]
    public async Task<IActionResult> Mark([FromBody] List<MarkRecordRequest> entries)
    {
        if (entries == null || entries.Count == 0) return BadRequest(ApiResponse<object>.Fail("INVALID", "No attendance entries supplied."));
        var bad = entries.FirstOrDefault(e => !Enum.TryParse<AttendanceStatus>(e.Status, true, out _));
        if (bad != null) return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Invalid attendance status '{bad.Status}'."));
        var marked = 0;
        foreach (var entry in entries)
        {
            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == entry.EmployeeId && !e.IsDeleted);
            if (emp == null) continue;
            var date = entry.Date.Date;
            var status = Enum.Parse<AttendanceStatus>(entry.Status, true);
            TimeSpan? inTime  = entry.InTime  != null && TimeSpan.TryParse(entry.InTime,  out var it)  ? it  : null;
            TimeSpan? outTime = entry.OutTime != null && TimeSpan.TryParse(entry.OutTime, out var ot) ? ot : null;

            var existing = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.Date == date && !a.IsDeleted);
            if (existing != null)
            {
                existing.Status = status; existing.InTime = inTime; existing.OutTime = outTime;
                existing.OvertimeHours = entry.OvertimeHours ?? 0;
                if (entry.Remark != null) existing.Remark = entry.Remark;
                existing.MarkedBy = AdminName; existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.AttendanceRecords.Add(new AttendanceRecord {
                    EmployeeId = emp.Id, Date = date, Status = status,
                    InTime = inTime, OutTime = outTime,
                    OvertimeHours = entry.OvertimeHours ?? 0,
                    Remark = entry.Remark, MarkedBy = AdminName, CreatedBy = AdminName
                });
            }
            marked++;
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Marked = marked }));
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> BulkMark([FromBody] MarkAttendanceRequest request)
    {
        var badBulk = request.Entries?.FirstOrDefault(e => !Enum.TryParse<AttendanceStatus>(e.Status, true, out _));
        if (badBulk != null) return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Invalid attendance status '{badBulk.Status}'."));
        var date = request.Date.Date;
        var marked = 0;
        foreach (var entry in request.Entries)
        {
            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == entry.EmpId && !e.IsDeleted);
            if (emp == null) continue;
            var existing = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.Date == date && !a.IsDeleted);
            if (existing != null) { existing.Status = Enum.Parse<AttendanceStatus>(entry.Status, true); existing.Remark = entry.Reason; existing.UpdatedAt = DateTime.UtcNow; }
            else { _db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp.Id, Date = date, Status = Enum.Parse<AttendanceStatus>(entry.Status, true), Remark = entry.Reason, MarkedBy = AdminName, CreatedBy = AdminName }); }
            marked++;
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Date = date, Marked = marked }));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Correct(int id, [FromBody] CorrectAttendanceRequest request)
    {
        var rec = await _db.AttendanceRecords.FindAsync(id);
        if (rec == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Record not found."));
        if (!Enum.TryParse<AttendanceStatus>(request.Status, true, out var corrected))
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Invalid attendance status '{request.Status}'."));
        rec.Status = corrected;
        if (request.Remark != null) rec.Remark = request.Remark;
        rec.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Shifts ──────────────────────────────────────────────────────────────
    [HttpGet("shifts")]
    public async Task<IActionResult> GetShifts()
    {
        var list = await _db.Shifts.Where(s => !s.IsDeleted).OrderBy(s => s.StartTime)
            .Select(s => new { s.Id, s.Name, StartTime = s.StartTime.ToString(), EndTime = s.EndTime.ToString(), s.LateThresholdMinutes, s.OvertimeThresholdMinutes, s.IsActive })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    private static string? ValidateShift(CreateShiftRequest req, out TimeSpan start, out TimeSpan end)
    {
        end = default;
        if (!TimeSpan.TryParse(req.StartTime, out start)) return "Invalid start time.";
        if (!TimeSpan.TryParse(req.EndTime, out end)) return "Invalid end time.";
        if (string.IsNullOrWhiteSpace(req.Name)) return "Shift name is required.";
        if (req.LateThresholdMinutes < 0 || req.OvertimeThresholdMinutes < 0) return "Thresholds cannot be negative.";
        return null;
    }

    [HttpPost("shifts")]
    public async Task<IActionResult> CreateShift([FromBody] CreateShiftRequest req)
    {
        var err = ValidateShift(req, out var start, out var end);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("INVALID", err));
        var s = new ShiftMaster {
            Name = req.Name.Trim(), StartTime = start, EndTime = end,
            LateThresholdMinutes = req.LateThresholdMinutes ?? 15, OvertimeThresholdMinutes = req.OvertimeThresholdMinutes ?? 30,
            CreatedBy = AdminName
        };
        _db.Shifts.Add(s);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { s.Id, s.Name, StartTime = s.StartTime.ToString(), EndTime = s.EndTime.ToString(), s.LateThresholdMinutes, s.OvertimeThresholdMinutes, s.IsActive }));
    }

    [HttpPut("shifts/{id:int}")]
    public async Task<IActionResult> UpdateShift(int id, [FromBody] CreateShiftRequest req)
    {
        var shift = await _db.Shifts.FindAsync(id);
        if (shift == null || shift.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shift not found."));
        var err = ValidateShift(req, out var start, out var end);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("INVALID", err));
        shift.Name = req.Name.Trim(); shift.StartTime = start; shift.EndTime = end;
        if (req.LateThresholdMinutes.HasValue) shift.LateThresholdMinutes = req.LateThresholdMinutes.Value;
        if (req.OvertimeThresholdMinutes.HasValue) shift.OvertimeThresholdMinutes = req.OvertimeThresholdMinutes.Value;
        shift.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("shifts/{id:int}")]
    public async Task<IActionResult> DeleteShift(int id)
    {
        var shift = await _db.Shifts.FindAsync(id);
        if (shift == null || shift.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shift not found."));
        if (await _db.EmployeeShifts.AnyAsync(es => es.ShiftId == id && es.EffectiveTo == null && !es.IsDeleted))
            return BadRequest(ApiResponse<object>.Fail("IN_USE", "This shift is assigned to employees. Reassign them first."));
        shift.IsDeleted = true; shift.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("shifts/assignments")]
    public async Task<IActionResult> GetAssignments()
    {
        var list = await _db.EmployeeShifts.Where(es => !es.IsDeleted)
            .Include(es => es.Employee).Include(es => es.Shift)
            .OrderBy(es => es.Employee.Name)
            .Select(es => new {
                es.Id, es.EmployeeId, EmployeeName = es.Employee.Name, Department = es.Employee.Department,
                es.ShiftId, ShiftName = es.Shift.Name,
                StartTime = es.Shift.StartTime.ToString(), EndTime = es.Shift.EndTime.ToString(),
                EffectiveFrom = es.EffectiveFrom.ToString("yyyy-MM-dd"),
                EffectiveTo = es.EffectiveTo.HasValue ? es.EffectiveTo.Value.ToString("yyyy-MM-dd") : null
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpPost("shifts/assign")]
    public async Task<IActionResult> AssignShift([FromBody] AssignShiftRequest req)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == req.EmployeeId && !e.IsDeleted)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Employee not found."));
        if (!await _db.Shifts.AnyAsync(s => s.Id == req.ShiftId && !s.IsDeleted)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Shift not found."));
        var active = await _db.EmployeeShifts.FirstOrDefaultAsync(es => es.EmployeeId == req.EmployeeId && es.EffectiveTo == null && !es.IsDeleted);
        if (active != null) { active.EffectiveTo = req.EffectiveFrom.Date.AddDays(-1); active.UpdatedAt = DateTime.UtcNow; }
        _db.EmployeeShifts.Add(new EmployeeShift { EmployeeId = req.EmployeeId, ShiftId = req.ShiftId, EffectiveFrom = req.EffectiveFrom.Date, CreatedBy = AdminName });
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<bool>.Ok(true));
    }

    // ── Regularizations ─────────────────────────────────────────────────────
    [HttpGet("regularizations")]
    public async Task<IActionResult> GetRegularizations([FromQuery] string? status, [FromQuery] int? empId, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.AttendanceRegularizations.Include(r => r.Employee).Where(r => !r.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.RegStatus == status);
        if (empId.HasValue) q = q.Where(r => r.EmployeeId == empId.Value);
        var total = await q.CountAsync();
        var list = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * limit).Take(limit)
            .Select(r => new {
                r.Id, r.EmployeeId, EmployeeName = r.Employee.Name, Department = r.Employee.Department,
                AttendanceDate = r.AttendanceDate.ToString("yyyy-MM-dd"),
                r.RequestedStatus, r.RequestedInTime, r.RequestedOutTime,
                r.Reason, r.RegStatus, r.ReviewedBy, r.ReviewRemarks,
                ReviewedAt = r.ReviewedAt.HasValue ? r.ReviewedAt.Value.ToString("yyyy-MM-dd") : null,
                CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd")
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items = list, total }));
    }

    [HttpPost("regularizations")]
    public async Task<IActionResult> SubmitRegularization([FromBody] RegularizationRequest req)
    {
        if (!Enum.TryParse<AttendanceStatus>(req.RequestedStatus, true, out _))
            return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Invalid attendance status '{req.RequestedStatus}'."));
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Reason is required."));
        if (!await _db.Employees.AnyAsync(e => e.Id == req.EmployeeId && !e.IsDeleted)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Employee not found."));
        _db.AttendanceRegularizations.Add(new AttendanceRegularization {
            EmployeeId = req.EmployeeId, AttendanceDate = req.AttendanceDate.Date,
            RequestedStatus = req.RequestedStatus, RequestedInTime = req.RequestedInTime,
            RequestedOutTime = req.RequestedOutTime, Reason = req.Reason,
            RegStatus = "Pending", CreatedBy = AdminName
        });
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<bool>.Ok(true));
    }

    [HttpPut("regularizations/{id:int}/approve")]
    public async Task<IActionResult> ApproveReg(int id, [FromBody] ReviewRegRequest? req)
    {
        var reg = await _db.AttendanceRegularizations.FirstOrDefaultAsync(r => r.Id == id);
        if (reg == null || reg.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Not found."));
        if (reg.RegStatus != "Pending") return BadRequest(ApiResponse<object>.Fail("ALREADY_REVIEWED", $"Request is already {reg.RegStatus.ToLower()}."));
        reg.RegStatus = "Approved"; reg.ReviewedBy = AdminName; reg.ReviewRemarks = req?.Remarks; reg.ReviewedAt = DateTime.UtcNow; reg.UpdatedAt = DateTime.UtcNow;
        var status = Enum.Parse<AttendanceStatus>(reg.RequestedStatus, true);
        var existing = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == reg.EmployeeId && a.Date == reg.AttendanceDate && !a.IsDeleted);
        if (existing != null)
        {
            existing.Status = status;
            if (reg.RequestedInTime != null && TimeSpan.TryParse(reg.RequestedInTime, out var it)) existing.InTime = it;
            if (reg.RequestedOutTime != null && TimeSpan.TryParse(reg.RequestedOutTime, out var ot)) existing.OutTime = ot;
            existing.Remark = $"Regularized: {reg.Reason}"; existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.AttendanceRecords.Add(new AttendanceRecord {
                EmployeeId = reg.EmployeeId, Date = reg.AttendanceDate, Status = status,
                InTime = reg.RequestedInTime != null && TimeSpan.TryParse(reg.RequestedInTime, out var it2) ? it2 : null,
                OutTime = reg.RequestedOutTime != null && TimeSpan.TryParse(reg.RequestedOutTime, out var ot2) ? ot2 : null,
                Remark = $"Regularized: {reg.Reason}", MarkedBy = AdminName, CreatedBy = AdminName
            });
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("regularizations/{id:int}/reject")]
    public async Task<IActionResult> RejectReg(int id, [FromBody] ReviewRegRequest? req)
    {
        var reg = await _db.AttendanceRegularizations.FindAsync(id);
        if (reg == null || reg.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Not found."));
        if (reg.RegStatus != "Pending") return BadRequest(ApiResponse<object>.Fail("ALREADY_REVIEWED", $"Request is already {reg.RegStatus.ToLower()}."));
        reg.RegStatus = "Rejected"; reg.ReviewedBy = AdminName; reg.ReviewRemarks = req?.Remarks; reg.ReviewedAt = DateTime.UtcNow; reg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LEAVE
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/leave")]
[RequireModule("Leave")]
public class AdminLeaveController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminLeaveController(AppDbContext db) => _db = db;

    [HttpGet("applications")]
    public async Task<IActionResult> GetApplications([FromQuery] string? status, [FromQuery] int? empId, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.LeaveApplications.Include(l => l.Employee).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LeaveStatus>(status, true, out var s)) q = q.Where(l => l.Status == s);
        if (empId.HasValue) q = q.Where(l => l.EmployeeId == empId.Value);
        var total = await q.CountAsync();
        var list = await q.OrderByDescending(l => l.CreatedAt).Skip((page - 1) * limit).Take(limit)
            .Select(l => new LeaveApplicationDto(l.Id, l.Employee.Name, l.LeaveType.ToString(), l.FromDate, l.ToDate, l.TotalDays, l.Reason, l.Status.ToString(), l.CreatedAt, l.EmployeeId, l.ApprovedBy, l.RejectionReason, l.ActionAt))
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { Applications = list, Pagination = new { total, page, limit } }));
    }

    [HttpPut("applications/{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] LeaveActionRequest? request)
    {
        var app = await _db.LeaveApplications.FindAsync(id);
        if (app == null || app.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Application not found."));
        if (app.Status != LeaveStatus.Pending) return BadRequest(ApiResponse<object>.Fail("ALREADY_REVIEWED", $"Application is already {app.Status.ToString().ToLower()}."));
        app.Status = LeaveStatus.Approved;
        app.ApprovedBy = AdminName;
        app.ActionAt = DateTime.UtcNow;
        app.UpdatedAt = DateTime.UtcNow;
        app.UpdatedBy = AdminName;

        // Consume the balance of the year the leave starts in
        var bal = await _db.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == app.EmployeeId && b.Year == app.FromDate.Year && !b.IsDeleted);
        if (bal == null) { bal = new LeaveBalance { EmployeeId = app.EmployeeId, Year = app.FromDate.Year, CreatedBy = AdminName }; _db.LeaveBalances.Add(bal); }
        switch (app.LeaveType)
        {
            case LeaveType.CasualLeave: bal.UsedCasual += app.TotalDays; break;
            case LeaveType.EarnedLeave: bal.UsedEarned += app.TotalDays; break;
            case LeaveType.SickLeave:   bal.UsedSick   += app.TotalDays; break;
            case LeaveType.MaternityLeave: bal.MaternityLeaveBalance = Math.Max(0, bal.MaternityLeaveBalance - app.TotalDays); break;
            case LeaveType.PaternityLeave: bal.PaternityLeaveBalance = Math.Max(0, bal.PaternityLeaveBalance - app.TotalDays); break;
        }
        bal.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Leave approved."));
    }

    [HttpPut("applications/{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] LeaveActionRequest? request)
    {
        var app = await _db.LeaveApplications.FindAsync(id);
        if (app == null || app.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Application not found."));
        if (app.Status != LeaveStatus.Pending) return BadRequest(ApiResponse<object>.Fail("ALREADY_REVIEWED", $"Application is already {app.Status.ToString().ToLower()}."));
        app.Status = LeaveStatus.Rejected;
        app.RejectionReason = string.IsNullOrWhiteSpace(request?.Reason) ? (string.IsNullOrWhiteSpace(request?.Remarks) ? null : request!.Remarks) : request!.Reason;
        app.ApprovedBy = AdminName;
        app.ActionAt = DateTime.UtcNow;
        app.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Leave rejected."));
    }

    [HttpGet("holidays")]
    public async Task<IActionResult> GetHolidays([FromQuery] int? year)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var holidays = await _db.Holidays.Where(h => h.Year == y && !h.IsDeleted).OrderBy(h => h.Date).ToListAsync();
        return Ok(ApiResponse<object>.Ok(holidays.Select(h => new { h.Id, h.Date, h.Name, h.Type })));
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> AddHoliday([FromBody] AddHolidayRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Holiday name is required."));
        if (request.Date == default) return BadRequest(ApiResponse<object>.Fail("INVALID", "Holiday date is required."));
        if (string.IsNullOrWhiteSpace(request.Type)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Holiday type is required."));
        if (await _db.Holidays.AnyAsync(h => h.Date == request.Date.Date && !h.IsDeleted))
            return BadRequest(ApiResponse<object>.Fail("DUPLICATE", "A holiday already exists on this date."));
        var holiday = new Holiday { Date = request.Date.Date, Name = request.Name.Trim(), Type = request.Type, Year = request.Date.Year, CreatedBy = AdminName };
        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { holiday.Id, holiday.Date, holiday.Name, holiday.Type }));
    }

    [HttpDelete("holidays/{id:int}")]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        var h = await _db.Holidays.FindAsync(id);
        if (h == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Holiday not found."));
        _db.Holidays.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances([FromQuery] int? year)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var employees = await _db.Employees.Where(e => e.IsActive)
            .OrderBy(e => e.Department).ThenBy(e => e.Name)
            .ToListAsync();
        var existingBalances = await _db.LeaveBalances.Where(b => b.Year == y).ToListAsync();

        // Auto-create missing LeaveBalance records for active employees
        var newBalances = new List<LeaveBalance>();
        foreach (var emp in employees)
        {
            if (!existingBalances.Any(b => b.EmployeeId == emp.Id))
            {
                var nb = new LeaveBalance { EmployeeId = emp.Id, Year = y };
                _db.LeaveBalances.Add(nb);
                newBalances.Add(nb);
            }
        }
        if (newBalances.Any()) { await _db.SaveChangesAsync(); existingBalances.AddRange(newBalances); }

        var result = employees.Select(emp =>
        {
            var b = existingBalances.FirstOrDefault(x => x.EmployeeId == emp.Id) ?? new LeaveBalance { EmployeeId = emp.Id, Year = y };
            return new
            {
                EmployeeId  = emp.Id,
                EmployeeCode = emp.EmployeeId,
                EmployeeName = emp.Name,
                Department  = emp.Department,
                CasualLeave = b.CasualLeaveBalance,
                EarnedLeave = b.EarnedLeaveBalance,
                SickLeave   = b.SickLeaveBalance,
                MaternityLeave = b.MaternityLeaveBalance,
                PaternityLeave = b.PaternityLeaveBalance,
                UsedCasual  = b.UsedCasual,
                UsedEarned  = b.UsedEarned,
                UsedSick    = b.UsedSick,
                TotalUsed   = b.UsedCasual + b.UsedEarned + b.UsedSick,
                TotalAvailable = b.CasualLeaveBalance + b.EarnedLeaveBalance + b.SickLeaveBalance
                               - b.UsedCasual - b.UsedEarned - b.UsedSick,
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(new { Balances = result, Year = y, Employees = result.Count }));
    }

    [HttpPost("applications")]
    public async Task<IActionResult> SubmitApplication([FromBody] CreateLeaveApplicationRequest request)
    {
        var employee = await _db.Employees.FindAsync(request.EmployeeId);
        if (employee == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        if (!employee.IsActive) return BadRequest(ApiResponse<object>.Fail("INACTIVE", "Employee is inactive."));
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Reason is required."));
        if (!Enum.TryParse<LeaveType>(request.LeaveType, true, out var lt))
            return BadRequest(ApiResponse<object>.Fail("INVALID_TYPE", "Invalid leave type."));

        var from = request.FromDate.Date;
        var to   = request.ToDate.Date;
        if (to < from) return BadRequest(ApiResponse<object>.Fail("INVALID_DATES", "To date must be >= From date."));
        var totalDays = (int)(to - from).TotalDays + 1;
        if (await _db.LeaveApplications.AnyAsync(l => l.EmployeeId == request.EmployeeId && !l.IsDeleted
                && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved) && l.FromDate <= to && l.ToDate >= from))
            return BadRequest(ApiResponse<object>.Fail("OVERLAP", "This employee already has a leave application for these dates."));

        var app = new LeaveApplication
        {
            EmployeeId = request.EmployeeId,
            LeaveType  = lt,
            FromDate   = from,
            ToDate     = to,
            TotalDays  = totalDays,
            Reason     = request.Reason.Trim(),
            Status     = LeaveStatus.Pending,
            CreatedBy  = AdminName,
        };
        _db.LeaveApplications.Add(app);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(
            new LeaveApplicationDto(app.Id, employee.Name, app.LeaveType.ToString(), app.FromDate, app.ToDate, app.TotalDays, app.Reason, app.Status.ToString(), app.CreatedAt, app.EmployeeId, null, null, null),
            "Leave application submitted."));
    }
}

// Records
public record UpdateEmployeeRequest(string? Department, string? Designation, decimal? Salary);
public record FullUpdateEmployeeRequest(
    string? Name, string? Email, string? Phone, string? Department, string? Designation,
    string? EmploymentType, decimal? Salary, DateTime? JoinedAt, DateTime? ConfirmationDate,
    int? NoticePeriodDays, string? Branch, string? AadharNumber, string? PanNumber,
    string? BankAccountNo, string? BankIfsc, string? PfNumber, string? EsicNumber,
    string? ReportingManagerId, string? PhotoUrl, bool? IsActive
);
public record CorrectAttendanceRequest(string Status, string? Remark);
public record AddHolidayRequest(DateTime Date, string Name, string Type);
public record CreateLeaveApplicationRequest(int EmployeeId, string LeaveType, DateTime FromDate, DateTime ToDate, string Reason);
public record MarkRecordRequest(int EmployeeId, DateTime Date, string Status, string? InTime, string? OutTime, decimal? OvertimeHours, string? Remark);
public record CreateShiftRequest(string Name, string StartTime, string EndTime, int? LateThresholdMinutes, int? OvertimeThresholdMinutes);
public record AssignShiftRequest(int EmployeeId, int ShiftId, DateTime EffectiveFrom);
public record RegularizationRequest(int EmployeeId, DateTime AttendanceDate, string RequestedStatus, string? RequestedInTime, string? RequestedOutTime, string Reason);
public record ReviewRegRequest(string? Remarks);
public record CreateDepartmentRequest(string Name, string? Code, string? Description);
public record CreateDesignationRequest(string Name, string? Department, int Level = 1);
public record AddDocumentRequest(string DocumentType, string DocumentName, string? DocumentNumber, string? FileUrl, DateTime? ExpiryDate, string? Notes);
public record UpdateSalaryStructureRequest(decimal BasicSalary, decimal HraPercent, decimal TaAmount, decimal DaPercent, decimal SpecialAllowance, decimal PfPercent, decimal EsicPercent, decimal ProfessionalTax, DateTime? EffectiveFrom);
public record AddIncrementRequest(decimal NewSalary, string Reason, DateTime EffectiveDate);
public record EmployeeTransferRequest(string ToDepartment, string? ToBranch, string? ToDesignation, DateTime TransferDate, string Reason);

// ─────────────────────────────────────────────────────────────────────────────
// DEPARTMENT MASTER
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/departments")]
[RequireModule("Staff")]
public class AdminDepartmentsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminDepartmentsController(AppDbContext db) => _db = db;

    [RequireModule("Staff", "Attendance", "Leave", "Payroll")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Departments.Where(d => !d.IsDeleted).OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Code, d.Description, d.IsActive,
                EmployeeCount = _db.Employees.Count(e => e.Department == d.Name && !e.IsDeleted) })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Department name is required."));
        req = req with { Name = req.Name.Trim() };
        if (await _db.Departments.AnyAsync(d => d.Name == req.Name && !d.IsDeleted))
            return BadRequest(ApiResponse<object>.Fail("DUPLICATE", "Department already exists."));
        var dept = new DepartmentMaster { Name = req.Name, Code = req.Code ?? req.Name.ToUpper()[..Math.Min(4, req.Name.Length)], Description = req.Description, CreatedBy = AdminName };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { dept.Id, dept.Name, dept.Code, dept.Description, dept.IsActive, EmployeeCount = 0 }));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDepartmentRequest req)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null || dept.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Department not found."));
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Department name is required."));
        var newName = req.Name.Trim();
        if (newName != dept.Name)
        {
            if (await _db.Departments.AnyAsync(d => d.Id != id && d.Name == newName && !d.IsDeleted))
                return BadRequest(ApiResponse<object>.Fail("DUPLICATE", "Department already exists."));
            var oldName = dept.Name;
            await _db.Employees.Where(e => e.Department == oldName).ExecuteUpdateAsync(u => u.SetProperty(e => e.Department, newName));
            await _db.Designations.Where(d => d.Department == oldName).ExecuteUpdateAsync(u => u.SetProperty(d => d.Department, newName));
        }
        dept.Name = newName;
        if (req.Code != null) dept.Code = req.Code;
        dept.Description = req.Description;
        dept.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null || dept.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Department not found."));
        var inUse = await _db.Employees.CountAsync(e => e.Department == dept.Name && e.IsActive && !e.IsDeleted);
        if (inUse > 0) return BadRequest(ApiResponse<object>.Fail("IN_USE", $"{inUse} active employee(s) are in {dept.Name}. Transfer them first."));
        dept.IsDeleted = true;
        dept.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DESIGNATION MASTER
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/designations")]
[RequireModule("Staff")]
public class AdminDesignationsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminDesignationsController(AppDbContext db) => _db = db;

    [RequireModule("Staff", "Attendance", "Leave", "Payroll")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? department)
    {
        var q = _db.Designations.Where(d => !d.IsDeleted);
        if (!string.IsNullOrWhiteSpace(department)) q = q.Where(d => d.Department == department);
        var list = await q.OrderBy(d => d.Level).ThenBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Department, d.Level, d.IsActive })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDesignationRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Designation name is required."));
        var name = req.Name.Trim();
        var dept = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim();
        if (await _db.Designations.AnyAsync(d => d.Name == name && d.Department == dept && !d.IsDeleted))
            return BadRequest(ApiResponse<object>.Fail("DUPLICATE", "Designation already exists."));
        var desg = new DesignationMaster { Name = name, Department = dept, Level = Math.Max(1, req.Level), CreatedBy = AdminName };
        _db.Designations.Add(desg);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { desg.Id, desg.Name, desg.Department, desg.Level, desg.IsActive }));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var desg = await _db.Designations.FindAsync(id);
        if (desg == null || desg.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Designation not found."));
        desg.IsDeleted = true;
        desg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EMPLOYEE DOCUMENTS
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/staff/{empId:int}/documents")]
[RequireModule("Staff")]
public class AdminEmployeeDocumentsController : AdminBaseController
{
    private const string FilePrefix = "staffdoc:";
    private const long MaxBytes = 10L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExts = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx" };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public AdminEmployeeDocumentsController(AppDbContext db, IWebHostEnvironment env) { _db = db; _env = env; }

    // Staff documents (Aadhaar, PAN, …) are personal data: stored outside wwwroot and only served to admins
    private string StoreDir => Path.Combine(_env.ContentRootPath, "App_Data", "staff-docs");

    private static object Map(EmployeeDocument d) => new
    {
        d.Id, d.DocumentType, d.DocumentName,
        DocumentNumber = SensitiveMask.Tail(d.DocumentNumber),            // full value via audited reveal
        HasFile = !string.IsNullOrEmpty(d.FileUrl),
        FileUrl = d.FileUrl != null && d.FileUrl.StartsWith(FilePrefix) ? null : d.FileUrl,   // external links only
        d.ExpiryDate, d.IsVerified, d.Notes, d.CreatedAt,
    };

    [HttpGet]
    public async Task<IActionResult> GetDocs(int empId)
    {
        var docs = await _db.EmployeeDocuments.Where(d => d.EmployeeId == empId && !d.IsDeleted)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(docs.Select(Map)));
    }

    [HttpPost]
    public async Task<IActionResult> AddDoc(int empId, [FromBody] AddDocumentRequest req)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        if (string.IsNullOrWhiteSpace(req.DocumentType)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Document type is required."));
        if (string.IsNullOrWhiteSpace(req.DocumentName)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Document name is required."));
        if (req.DocumentNumber?.Trim().Length > 40) return BadRequest(ApiResponse<object>.Fail("INVALID", "Document number is too long (max 40 characters).", "documentNumber"));
        // Only external http(s) links may be attached here — never another stored file ("staffdoc:") or javascript:/data: URLs
        if (!string.IsNullOrWhiteSpace(req.FileUrl) &&
            !(Uri.TryCreate(req.FileUrl.Trim(), UriKind.Absolute, out var link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)))
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Document link must be an http(s) URL. Use Upload for files.", "fileUrl"));
        var doc = new EmployeeDocument
        {
            EmployeeId = empId, DocumentType = req.DocumentType, DocumentName = req.DocumentName.Trim(),
            DocumentNumber = req.DocumentNumber, FileUrl = string.IsNullOrWhiteSpace(req.FileUrl) ? null : req.FileUrl, ExpiryDate = req.ExpiryDate,
            Notes = req.Notes, CreatedBy = AdminName
        };
        _db.EmployeeDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(Map(doc)));
    }

    /// <summary>Upload a document file (multipart: file, documentType, documentName, documentNumber?, expiryDate?, notes?)</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int empId, IFormFile? file, [FromForm] string? documentType, [FromForm] string? documentName,
        [FromForm] string? documentNumber, [FromForm] DateTime? expiryDate, [FromForm] string? notes)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        if (file == null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail("NO_FILE", "Choose a file to upload."));
        if (string.IsNullOrWhiteSpace(documentType)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Document type is required."));
        var ext = Path.GetExtension(Path.GetFileName(file.FileName ?? "")).ToLowerInvariant();
        if (!AllowedExts.Contains(ext)) return BadRequest(ApiResponse<object>.Fail("INVALID_FILE", $"File type '{ext}' is not allowed. Use PDF, JPG, PNG, WEBP or Word."));
        if (file.Length > MaxBytes) return BadRequest(ApiResponse<object>.Fail("FILE_TOO_LARGE", "File must be 10 MB or smaller."));
        if (!await FileSignatures.MatchesAsync(file, ext))
            return BadRequest(ApiResponse<object>.Fail("INVALID_FILE", "The file's contents don't match its type. Upload a real PDF, image or Word file."));
        if (documentNumber?.Trim().Length > 40) return BadRequest(ApiResponse<object>.Fail("INVALID", "Document number is too long (max 40 characters).", "documentNumber"));

        Directory.CreateDirectory(StoreDir);
        var stored = $"{empId}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        await using (var fs = new FileStream(Path.Combine(StoreDir, stored), FileMode.CreateNew, FileAccess.Write))
            await file.CopyToAsync(fs);

        var doc = new EmployeeDocument
        {
            EmployeeId = empId, DocumentType = documentType,
            DocumentName = string.IsNullOrWhiteSpace(documentName) ? Path.GetFileNameWithoutExtension(file.FileName) : documentName.Trim(),
            DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber, ExpiryDate = expiryDate,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes, FileUrl = FilePrefix + stored, CreatedBy = AdminName,
        };
        _db.EmployeeDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(Map(doc), "Document uploaded."));
    }

    /// <summary>Download the uploaded file (admin only)</summary>
    [HttpGet("{docId:int}/file")]
    public async Task<IActionResult> Download(int empId, int docId)
    {
        var doc = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.EmployeeId == empId && !d.IsDeleted);
        if (doc == null || string.IsNullOrEmpty(doc.FileUrl) || !doc.FileUrl.StartsWith(FilePrefix))
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "No uploaded file for this document."));
        var name = Path.GetFileName(doc.FileUrl[FilePrefix.Length..]);
        var path = Path.Combine(StoreDir, name);
        if (!System.IO.File.Exists(path)) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "File is missing on the server."));
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var type = ext switch
        {
            ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
            ".doc" => "application/msword", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };
        var download = string.Concat($"{doc.DocumentName}{ext}".Split(Path.GetInvalidFileNameChars()));
        return PhysicalFile(path, type, download);
    }

    [HttpPut("{docId:int}/verify")]
    public async Task<IActionResult> Verify(int empId, int docId)
    {
        var doc = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.EmployeeId == empId && !d.IsDeleted);
        if (doc == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Document not found."));
        doc.IsVerified = true;
        doc.UpdatedAt = DateTime.UtcNow;
        doc.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{docId:int}")]
    public async Task<IActionResult> DeleteDoc(int empId, int docId)
    {
        var doc = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.EmployeeId == empId && !d.IsDeleted);
        if (doc == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Document not found."));
        doc.IsDeleted = true;
        doc.UpdatedAt = DateTime.UtcNow;
        doc.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        if (doc.FileUrl != null && doc.FileUrl.StartsWith(FilePrefix))
        {
            var path = Path.Combine(StoreDir, Path.GetFileName(doc.FileUrl[FilePrefix.Length..]));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        return Ok(ApiResponse<bool>.Ok(true));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SALARY STRUCTURE & INCREMENTS
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/staff/{empId:int}/salary")]
[RequireModule("Staff", "Payroll")]
public class AdminSalaryController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminSalaryController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetStructure(int empId)
    {
        var s = await _db.SalaryStructures.AsNoTracking().Where(x => x.EmployeeId == empId && x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync();
        if (s == null) return Ok(ApiResponse<object?>.Ok(null));
        return Ok(ApiResponse<object>.Ok(new { s.Id, s.EmployeeId, s.BasicSalary, s.HraPercent, s.TaAmount, s.DaPercent, s.SpecialAllowance,
            s.PfPercent, s.EsicPercent, s.ProfessionalTax, s.GrossSalary, s.NetSalary, s.EffectiveFrom, s.IsActive }));
    }

    /// <summary>Preview gross / deductions / net for a structure without saving (same rules as payroll)</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(int empId, [FromBody] UpdateSalaryStructureRequest req)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        var s = new SalaryStructure { BasicSalary = req.BasicSalary, HraPercent = req.HraPercent, TaAmount = req.TaAmount, DaPercent = req.DaPercent,
            SpecialAllowance = req.SpecialAllowance, PfPercent = req.PfPercent, EsicPercent = req.EsicPercent, ProfessionalTax = req.ProfessionalTax };
        var c = PayrollCalculator.MonthlyComponents(emp, s);
        var d = PayrollCalculator.EmployeeDeductions(c, c.Gross, s);
        return Ok(ApiResponse<object>.Ok(new { Gross = c.Gross, Net = c.Gross - d.Total, Pf = d.Epf, Esic = d.Esic, Pt = d.ProfessionalTax, EsicApplicable = d.EsicCovered }));
    }

    /// <summary>Default salary-structure values for an employee without one (statutory defaults)</summary>
    [HttpGet("defaults")]
    public async Task<IActionResult> Defaults(int empId)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        var c = PayrollCalculator.MonthlyComponents(emp, null);
        return Ok(ApiResponse<object>.Ok(new
        {
            BasicSalary = c.Basic,
            HraPercent = c.Basic > 0 ? Math.Round(c.Hra / c.Basic * 100m, 2) : 0,
            TaAmount = c.Ta,
            DaPercent = c.Basic > 0 ? Math.Round(c.Da / c.Basic * 100m, 2) : 0,
            SpecialAllowance = c.Special,
            PfPercent = PayrollCalculator.EpfEmployeeRate,
            EsicPercent = PayrollCalculator.EsicEmployeeRate,
            ProfessionalTax = PayrollCalculator.ProfessionalTaxFor(c.Gross),
        }));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateStructure(int empId, [FromBody] UpdateSalaryStructureRequest req)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));

        if (req.BasicSalary <= 0) return BadRequest(ApiResponse<object>.Fail("INVALID", "Basic salary must be greater than zero.", "basicSalary"));
        if (new[] { req.HraPercent, req.TaAmount, req.DaPercent, req.SpecialAllowance, req.PfPercent, req.EsicPercent, req.ProfessionalTax }.Any(v => v < 0))
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Salary components cannot be negative."));
        if (req.HraPercent > 100 || req.DaPercent > 100 || req.PfPercent > 100 || req.EsicPercent > 100)
            return BadRequest(ApiResponse<object>.Fail("INVALID", "Percentages must be between 0 and 100."));

        var old = await _db.SalaryStructures.Where(x => x.EmployeeId == empId && x.IsActive && !x.IsDeleted).ToListAsync();
        old.ForEach(x => { x.IsActive = false; x.UpdatedAt = DateTime.UtcNow; });

        var s = new SalaryStructure
        {
            EmployeeId = empId, BasicSalary = req.BasicSalary, HraPercent = req.HraPercent,
            TaAmount = req.TaAmount, DaPercent = req.DaPercent, SpecialAllowance = req.SpecialAllowance,
            PfPercent = req.PfPercent, EsicPercent = req.EsicPercent, ProfessionalTax = req.ProfessionalTax,
            EffectiveFrom = req.EffectiveFrom ?? DateTime.UtcNow,
            CreatedBy = AdminName
        };
        var c = PayrollCalculator.MonthlyComponents(emp, s);
        var d = PayrollCalculator.EmployeeDeductions(c, c.Gross, s);
        s.GrossSalary = c.Gross;
        s.NetSalary   = c.Gross - d.Total;
        _db.SalaryStructures.Add(s);
        emp.Salary = c.Gross;
        emp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { s.Id, s.GrossSalary, s.NetSalary, Epf = d.Epf, Esic = d.Esic, d.ProfessionalTax, EsicApplicable = d.EsicCovered }));
    }

    [HttpGet("increments")]
    public async Task<IActionResult> GetIncrements(int empId)
    {
        var list = await _db.IncrementHistories.Where(x => x.EmployeeId == empId && !x.IsDeleted)
            .OrderByDescending(x => x.EffectiveDate)
            .Select(x => new { x.Id, x.OldSalary, x.NewSalary, x.IncrementPercent, x.Reason, x.EffectiveDate, x.ApprovedBy, x.CreatedAt })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpPost("increments")]
    public async Task<IActionResult> AddIncrement(int empId, [FromBody] AddIncrementRequest req)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        if (req.NewSalary <= 0) return BadRequest(ApiResponse<object>.Fail("INVALID", "New salary must be greater than zero."));
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Reason is required."));

        var pct = emp.Salary > 0 ? Math.Round((req.NewSalary - emp.Salary) / emp.Salary * 100, 2) : 0;
        var inc = new IncrementHistory
        {
            EmployeeId = empId, OldSalary = emp.Salary, NewSalary = req.NewSalary,
            IncrementPercent = pct, Reason = req.Reason.Trim(), EffectiveDate = req.EffectiveDate,
            ApprovedBy = AdminName, CreatedBy = AdminName
        };
        _db.IncrementHistories.Add(inc);

        // Keep an existing salary structure in step: scale its fixed components so payroll uses the new salary
        var active = await _db.SalaryStructures.Where(x => x.EmployeeId == empId && x.IsActive && !x.IsDeleted).ToListAsync();
        var current = active.OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
        if (current != null && current.GrossSalary > 0)
        {
            var factor = req.NewSalary / current.GrossSalary;
            active.ForEach(x => { x.IsActive = false; x.UpdatedAt = DateTime.UtcNow; });
            var ns = new SalaryStructure
            {
                EmployeeId = empId, BasicSalary = Math.Round(current.BasicSalary * factor, 0), HraPercent = current.HraPercent,
                TaAmount = Math.Round(current.TaAmount * factor, 0), DaPercent = current.DaPercent,
                PfPercent = current.PfPercent, EsicPercent = current.EsicPercent, ProfessionalTax = current.ProfessionalTax,
                EffectiveFrom = req.EffectiveDate, CreatedBy = AdminName,
            };
            var c0 = PayrollCalculator.MonthlyComponents(emp, ns);
            ns.SpecialAllowance = Math.Max(0, req.NewSalary - (c0.Basic + c0.Hra + c0.Ta + c0.Da));   // absorb rounding
            var c1 = PayrollCalculator.MonthlyComponents(emp, ns);
            var d1 = PayrollCalculator.EmployeeDeductions(c1, c1.Gross, ns);
            ns.GrossSalary = c1.Gross; ns.NetSalary = c1.Gross - d1.Total;
            _db.SalaryStructures.Add(ns);
        }
        emp.Salary = req.NewSalary;
        emp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { inc.Id, inc.IncrementPercent }));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EMPLOYEE TRANSFERS & HIERARCHY
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/admin/staff")]
[RequireModule("Staff")]
public class AdminTransferController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminTransferController(AppDbContext db) => _db = db;

    [HttpGet("transfers")]
    public async Task<IActionResult> GetAllTransfers([FromQuery] int? empId, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.EmployeeTransfers.Include(t => t.Employee).Where(t => !t.IsDeleted);
        if (empId.HasValue) q = q.Where(t => t.EmployeeId == empId.Value);
        var total = await q.CountAsync();
        var list = await q.OrderByDescending(t => t.TransferDate).Skip((page - 1) * limit).Take(limit)
            .Select(t => new { t.Id, EmployeeName = t.Employee.Name, EmployeeCode = t.Employee.EmployeeId, t.FromDepartment, t.ToDepartment, t.FromBranch, t.ToBranch, t.FromDesignation, t.ToDesignation, t.TransferDate, t.Reason, t.ApprovedBy, t.CreatedAt })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { transfers = list, total }));
    }

    [HttpPost("{empId:int}/transfer")]
    public async Task<IActionResult> Transfer(int empId, [FromBody] EmployeeTransferRequest req)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        if (string.IsNullOrWhiteSpace(req.ToDepartment)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Target department is required."));
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(ApiResponse<object>.Fail("INVALID", "Reason is required."));

        var transfer = new EmployeeTransfer
        {
            EmployeeId = empId, FromDepartment = emp.Department, ToDepartment = req.ToDepartment,
            FromBranch = emp.Branch, ToBranch = req.ToBranch, FromDesignation = emp.Designation,
            ToDesignation = req.ToDesignation, TransferDate = req.TransferDate,
            Reason = req.Reason, ApprovedBy = AdminName, CreatedBy = AdminName
        };
        _db.EmployeeTransfers.Add(transfer);
        emp.Department = req.ToDepartment;
        if (req.ToBranch != null) emp.Branch = req.ToBranch;
        if (req.ToDesignation != null) emp.Designation = req.ToDesignation;
        emp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(new { transfer.Id }));
    }

    [HttpGet("hierarchy")]
    public async Task<IActionResult> GetHierarchy()
    {
        var employees = await _db.Employees.Where(e => e.IsActive && !e.IsDeleted)
            .Select(e => new { e.Id, e.EmployeeId, e.Name, e.Department, e.Designation, e.ReportingManagerId, e.PhotoUrl })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(employees));
    }
}
