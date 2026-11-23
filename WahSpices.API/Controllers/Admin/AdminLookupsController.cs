using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// One place the admin UI reads every dropdown from:
///  • enums  — fixed values the backend logic depends on (read-only), straight from Domain.Enums
///  • lists  — admin-editable master data (locations, units, categories, payment modes …)
///  • years  — financial/calendar year options computed from today
/// Plus CRUD for the editable lists.
/// </summary>
[Route("api/admin")]
[RequireModule("Settings")]
public class AdminLookupsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminLookupsController(AppDbContext db) => _db = db;

    private static readonly Dictionary<string, object> EnumCache = BuildEnums();

    private static Dictionary<string, object> BuildEnums() =>
        typeof(WahSpices.Domain.Enums.OrderStatus).Assembly.GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "WahSpices.Domain.Enums")
            .OrderBy(t => t.Name)
            .ToDictionary(
                t => t.Name,
                t => (object)Enum.GetValues(t).Cast<object>()
                    .Select(v => new { value = v.ToString(), label = Humanize(v.ToString()!), number = Convert.ToInt32(v) })
                    .ToList());

    /// "OutForDelivery" → "Out For Delivery", "WorkFromHome" → "Work From Home"
    private static string Humanize(string s) => Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");

    [RequireModule(AdminModules.AnyAdmin)]   // every admin page reads its dropdowns here
    [HttpGet("lookups")]
    public async Task<IActionResult> GetLookups([FromQuery] bool includeInactive = false)
    {
        var q = _db.MasterDataValues.AsNoTracking();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        var values = await q.OrderBy(m => m.Type).ThenBy(m => m.SortOrder).ThenBy(m => m.Label).ToListAsync();

        var lists = values.GroupBy(v => v.Type).ToDictionary(
            g => g.Key,
            g => g.Select(v => new { v.Id, value = v.Value, label = v.Label, v.SortOrder, v.IsActive, v.IsSystem }).ToList());

        var now = DateTime.UtcNow;
        // Indian financial year (Apr–Mar)
        var fyStart = now.Month >= 4 ? now.Year : now.Year - 1;
        var financialYears = Enumerable.Range(0, 5).Select(i => $"{fyStart - i}-{(fyStart - i + 1) % 100:D2}").ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            enums = EnumCache,
            lists,
            types = MasterDataSeeder.Defaults.Keys.Union(lists.Keys).OrderBy(k => k).ToList(),
            years = Enumerable.Range(now.Year - 4, 6).Select(y => y).OrderByDescending(y => y).ToList(),
            financialYears,
            currentYear = now.Year,
            currentMonth = now.Month,
        }));
    }

    // ── Master data CRUD ──────────────────────────────────────────────────────

    public record MasterDataRequest(string Type, string Value, string? Label, int? SortOrder, bool IsActive = true);

    [RequireModule(AdminModules.AnyAdmin)]
    [HttpGet("master-data")]
    public async Task<IActionResult> List([FromQuery] string? type)
    {
        var q = _db.MasterDataValues.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(m => m.Type == type);
        var items = await q.OrderBy(m => m.Type).ThenBy(m => m.SortOrder).ThenBy(m => m.Label).ToListAsync();
        return Ok(ApiResponse<object>.Ok(items.Select(Map)));
    }

    [HttpPost("master-data")]
    public async Task<IActionResult> Create([FromBody] MasterDataRequest req)
    {
        var (type, value, label) = Clean(req);
        if (type == "" || value == "")
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Type and value are required."));
        if (await _db.MasterDataValues.AnyAsync(m => m.Type == type && m.Value == value))
            return Conflict(ApiResponse<object>.Fail("DUPLICATE", $"'{value}' already exists in this list."));

        var order = req.SortOrder ?? ((await _db.MasterDataValues.Where(m => m.Type == type).MaxAsync(m => (int?)m.SortOrder)) ?? -1) + 1;
        var item = new MasterDataValue { Type = type, Value = value, Label = label, SortOrder = order, IsActive = req.IsActive, CreatedBy = AdminName };
        _db.MasterDataValues.Add(item);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(Map(item), "Added"));
    }

    [HttpPut("master-data/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MasterDataRequest req)
    {
        var item = await _db.MasterDataValues.FirstOrDefaultAsync(m => m.Id == id);
        if (item == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Value not found."));
        var (_, value, label) = Clean(req);
        if (value == "") return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Value is required."));

        // System values back code paths — their stored value can't change, only label / order / visibility
        if (item.IsSystem && value != item.Value)
            return BadRequest(ApiResponse<object>.Fail("SYSTEM_VALUE", "This is a system value — you can rename its label or hide it, but not change the value."));
        if (value != item.Value && await _db.MasterDataValues.AnyAsync(m => m.Type == item.Type && m.Value == value && m.Id != id))
            return Conflict(ApiResponse<object>.Fail("DUPLICATE", $"'{value}' already exists in this list."));

        item.Value = value;
        item.Label = label;
        if (req.SortOrder.HasValue) item.SortOrder = req.SortOrder.Value;
        item.IsActive = req.IsActive;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(item), "Updated"));
    }

    [HttpDelete("master-data/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.MasterDataValues.FirstOrDefaultAsync(m => m.Id == id);
        if (item == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Value not found."));
        if (item.IsSystem)
            return BadRequest(ApiResponse<object>.Fail("SYSTEM_VALUE", "System values can't be deleted — hide them instead."));
        item.IsDeleted = true;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Deleted"));
    }

    private static (string type, string value, string label) Clean(MasterDataRequest r)
    {
        var type = (r.Type ?? "").Trim();
        var value = (r.Value ?? "").Trim();
        var label = string.IsNullOrWhiteSpace(r.Label) ? value : r.Label.Trim();
        return (type, value, label);
    }

    private static object Map(MasterDataValue m) =>
        new { m.Id, m.Type, value = m.Value, label = m.Label, m.SortOrder, m.IsActive, m.IsSystem };
}
