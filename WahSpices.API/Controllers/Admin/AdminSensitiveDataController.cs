using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// "Reveal" for encrypted identifiers that every list/detail endpoint returns MASKED (XXXX-XXXX-1234).
/// Each reveal needs the owning module's permission and writes an AuditLog row (who, what record, which fields,
/// reason, IP) — visible in Access Control → Audit log. POST (not GET) so values never land in URLs/proxy logs,
/// and responses are marked no-store.
/// </summary>
[Route("api/admin/sensitive")]
public class AdminSensitiveDataController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminSensitiveDataController(AppDbContext db) => _db = db;

    [HttpPost("employees/{id:int}/reveal")]
    [RequireModule("Staff")]
    public async Task<IActionResult> RevealEmployee(int id, [FromBody] RevealRequest? req)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Employee not found."));
        await AuditAsync("Staff", $"Employee #{e.Id} ({e.EmployeeId})", "AadharNumber, PanNumber, BankAccountNo, BankIfsc, PfNumber, EsicNumber", req?.Reason);
        return Sensitive(new { e.Id, e.AadharNumber, e.PanNumber, e.BankAccountNo, e.BankIfsc, e.PfNumber, e.EsicNumber });
    }

    [HttpPost("employee-documents/{docId:int}/reveal")]
    [RequireModule("Staff")]
    public async Task<IActionResult> RevealEmployeeDocument(int docId, [FromBody] RevealRequest? req)
    {
        var d = await _db.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == docId && !x.IsDeleted);
        if (d == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Document not found."));
        await AuditAsync("Staff", $"Employee document #{d.Id} (employee #{d.EmployeeId}, {d.DocumentType})", "DocumentNumber", req?.Reason);
        return Sensitive(new { d.Id, d.DocumentNumber });
    }

    [HttpPost("vendors/{id:int}/reveal")]
    [RequireModule("Purchase")]
    public async Task<IActionResult> RevealVendor(int id, [FromBody] RevealRequest? req)
    {
        var v = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (v == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Vendor not found."));
        await AuditAsync("Purchase", $"Vendor #{v.Id} ({v.Name})", "BankAccountNo, BankIfsc", req?.Reason);
        return Sensitive(new { v.Id, v.BankAccountNo, v.BankIfsc, v.BankName });
    }

    [HttpPost("crm-customers/{id:int}/reveal")]
    [RequireModule("Customers")]
    public async Task<IActionResult> RevealCrmCustomer(int id, [FromBody] RevealRequest? req)
    {
        var c = await _db.CrmCustomers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
        await AuditAsync("Customers", $"CRM customer #{c.Id} ({c.Name})", "Pan", req?.Reason);
        return Sensitive(new { c.Id, c.Pan });
    }

    private IActionResult Sensitive(object data)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(ApiResponse<object>.Ok(data));
    }

    private async Task AuditAsync(string module, string record, string fields, string? reason)
    {
        var r = string.IsNullOrWhiteSpace(reason) ? "" : $" — reason: {reason.Trim()[..Math.Min(200, reason.Trim().Length)]}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = AdminId,
            UserName = AdminName,
            Module = module,
            Action = "reveal-sensitive",
            Details = $"Revealed {fields} of {record}{r}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(500, ua.Length)] : null,
            CreatedBy = AdminName,
        });
        await _db.SaveChangesAsync();
    }
}

public record RevealRequest(string? Reason);
