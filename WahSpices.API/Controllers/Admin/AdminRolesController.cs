using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/roles")]
[RequireModule("AccessControl")]
public class AdminRolesController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminRolesController(AppDbContext db) => _db = db;

    // ── GET all roles ─────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.AdminRoles
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.IsSystem ? 0 : 1).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.Color, r.IsSystem, r.IsActive,
                r.Permissions,
                UserCount = _db.AdminUsers.Count(u => u.AdminRoleId == r.Id && !u.IsDeleted)
            })
            .ToListAsync();

        var result = roles.Select(r => new AdminRoleDto(
            r.Id, r.Name, r.Description, r.Color, r.IsSystem, r.IsActive,
            r.UserCount,
            AdminPermissions.Parse(r.Permissions)
        )).ToList();

        return Ok(ApiResponse<object>.Ok(result));
    }

    // ── GET permission modules (single source of truth for the permission grid) ──
    [HttpGet("modules")]
    [RequireModule(AdminModules.AnyAdmin)]
    public IActionResult GetModules() => Ok(ApiResponse<object>.Ok(DefaultRoles.Modules));

    // ── GET permission modules grouped exactly like the admin sidebar (drives the permission matrix) ──
    [HttpGet("module-groups")]
    [RequireModule(AdminModules.AnyAdmin)]
    public IActionResult GetModuleGroups() => Ok(ApiResponse<object>.Ok(DefaultRoles.Groups.Select(g => new
    {
        g.Title,
        Modules = g.Modules.Select(m => new { Key = m.Key, Label = m.Label })
    })));

    // ── GET single role ───────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetRole(int id)
    {
        var r = await _db.AdminRoles.FindAsync(id);
        if (r == null || r.IsDeleted) return NotFound();

        var userCount = await _db.AdminUsers.CountAsync(u => u.AdminRoleId == id && !u.IsDeleted);
        var dto = new AdminRoleDto(r.Id, r.Name, r.Description, r.Color, r.IsSystem, r.IsActive, userCount,
            AdminPermissions.Parse(r.Permissions));

        return Ok(ApiResponse<AdminRoleDto>.Ok(dto));
    }

    // ── CREATE role ───────────────────────────────────────────────────────
    [HttpPost]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Role name is required."));
        if (await _db.AdminRoles.AnyAsync(r => r.Name == req.Name && !r.IsDeleted))
            return Conflict(ApiResponse<object>.Fail("CONFLICT", "Role name already exists."));

        if (AdminAuthController.IsSuperAdminRole(req.Name))
            return BadRequest(ApiResponse<object>.Fail("RESERVED", "That role name is reserved for the Super Admin role."));

        var perms = DefaultRoles.Normalize(req.Permissions);
        var role = new AdminRole
        {
            Name        = req.Name.Trim(),
            Description = req.Description?.Trim(),
            Color       = string.IsNullOrWhiteSpace(req.Color) ? "bg-gray-100 text-gray-700" : req.Color,
            Permissions = JsonSerializer.Serialize(perms),
            IsSystem    = false,
            IsActive    = true,
            CreatedAt   = DateTime.UtcNow
        };
        _db.AdminRoles.Add(role);
        await _db.SaveChangesAsync();

        await WriteAuditLog($"Created role '{role.Name}'");
        return StatusCode(201, ApiResponse<object>.Ok(new AdminRoleDto(
            role.Id, role.Name, role.Description, role.Color, false, true, 0, perms), "Role created."));
    }

    // ── UPDATE role ───────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] UpdateRoleRequest req)
    {
        var role = await _db.AdminRoles.FindAsync(id);
        if (role == null || role.IsDeleted) return NotFound();

        if (role.IsSystem && req.Name != role.Name)
            return BadRequest(ApiResponse<object>.Fail("SYSTEM_ROLE", "Cannot rename system roles."));

        // Check name uniqueness if changed
        if (req.Name != role.Name && await _db.AdminRoles.AnyAsync(r => r.Name == req.Name && r.Id != id && !r.IsDeleted))
            return Conflict(ApiResponse<object>.Fail("CONFLICT", "Role name already exists."));

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Role name is required."));

        var isSuperRole = AdminAuthController.IsSuperAdminRole(role.Name);
        if (!isSuperRole && AdminAuthController.IsSuperAdminRole(req.Name))
            return BadRequest(ApiResponse<object>.Fail("RESERVED", "That role name is reserved for the Super Admin role."));
        if (isSuperRole && !req.IsActive)
            return BadRequest(ApiResponse<object>.Fail("LAST_SUPER_ADMIN", "The Super Admin role cannot be deactivated."));

        // Super Admin always has every module; everyone else gets an explicit true/false for every module
        var perms = isSuperRole ? DefaultRoles.Modules.ToDictionary(m => m, _ => true) : DefaultRoles.Normalize(req.Permissions);

        var oldName      = role.Name;
        role.Name        = req.Name.Trim();
        role.Description = req.Description?.Trim();
        role.Color       = string.IsNullOrWhiteSpace(req.Color) ? role.Color : req.Color;
        role.Permissions = JsonSerializer.Serialize(perms);
        role.IsActive    = req.IsActive;
        role.UpdatedAt   = DateTime.UtcNow;

        // keep the denormalised role name on users in sync (it drives the JWT role claim)
        if (oldName != role.Name)
        {
            var members = await _db.AdminUsers.Where(u => u.AdminRoleId == id && !u.IsDeleted).ToListAsync();
            foreach (var u in members) u.Role = role.Name;
        }
        await _db.SaveChangesAsync();

        await WriteAuditLog($"Updated role '{role.Name}'");
        var dto = new AdminRoleDto(role.Id, role.Name, role.Description, role.Color, role.IsSystem, role.IsActive,
            await _db.AdminUsers.CountAsync(u => u.AdminRoleId == id && !u.IsDeleted), perms);
        return Ok(ApiResponse<AdminRoleDto>.Ok(dto, "Role updated."));
    }

    // ── DELETE role ───────────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> DeleteRole(int id)
    {
        var role = await _db.AdminRoles.FindAsync(id);
        if (role == null || role.IsDeleted) return NotFound();
        if (role.IsSystem) return BadRequest(ApiResponse<object>.Fail("SYSTEM_ROLE", "System roles cannot be deleted."));

        var userCount = await _db.AdminUsers.CountAsync(u => u.AdminRoleId == id && !u.IsDeleted);
        if (userCount > 0)
            return BadRequest(ApiResponse<object>.Fail("HAS_USERS", $"Cannot delete role with {userCount} active user(s). Reassign users first."));

        role.IsDeleted = true;
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog($"Deleted role '{role.Name}'");
        return Ok(ApiResponse<bool>.Ok(true, "Role deleted."));
    }

    // ── Seed default roles (idempotent) ───────────────────────────────────
    [HttpPost("seed")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> SeedDefaultRoles()
    {
        var defaults = DefaultRoles.All;
        int created = 0;
        foreach (var (name, desc, color, isSystem, perms) in defaults)
        {
            if (!await _db.AdminRoles.AnyAsync(r => r.Name == name))
            {
                _db.AdminRoles.Add(new AdminRole
                {
                    Name        = name,
                    Description = desc,
                    Color       = color,
                    Permissions = JsonSerializer.Serialize(perms),
                    IsSystem    = isSystem,
                    IsActive    = true,
                    CreatedAt   = DateTime.UtcNow
                });
                created++;
            }
        }
        await _db.SaveChangesAsync();
        var upgraded = await DefaultRoles.UpgradeAsync(_db);
        return Ok(ApiResponse<object>.Ok(new { Created = created, Upgraded = upgraded },
            $"Seeded {created} role(s); added new modules to {upgraded} existing role(s)."));
    }

    private async Task WriteAuditLog(string action)
    {
        var adminId   = User.FindFirst("admin_id")?.Value ?? "0";
        var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId    = adminId,
            UserName  = adminName,
            Module    = "AccessControl",
            Action    = action,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}

// ── Default role definitions ──────────────────────────────────────────────────
public static class DefaultRoles
{
    public static IReadOnlyList<string> Modules => AllModules;

    /// <summary>Permission modules grouped exactly like the admin sidebar (AdminLayout ALL_NAV_GROUPS).</summary>
    public static readonly IReadOnlyList<(string Title, (string Key, string Label)[] Modules)> Groups =
    [
        ("Overview",               [("Dashboard", "Dashboard")]),
        ("Sales & Billing",        [("Orders", "Orders"), ("Returns", "Returns"), ("Payments", "Payments"),
                                    ("WebsiteUsers", "Website Users"), ("Billing", "Billing & Invoice"), ("GST", "GST & Tax"),
                                    ("Customers", "Customers & CRM"), ("FieldSales", "Field Sales")]),
        ("Inventory & Production", [("Inventory", "Inventory"), ("Production", "Production"), ("Purchase", "Purchase"), ("Barcode", "Barcode")]),
        ("HR Management",          [("Staff", "Staff"), ("Attendance", "Attendance"), ("Leave", "Leave"), ("Payroll", "Payroll"), ("Expenses", "Expenses")]),
        ("Company",                [("Company", "Company finance & compliance")]),
        ("Products & Content",     [("Products", "Products"), ("Categories", "Categories"), ("Blog", "Blog"), ("Recipes", "Recipes"), ("Reviews", "Reviews")]),
        ("Website",                [("Website", "Website Content"), ("Feedback", "Feedback"), ("Messages", "Contact Messages")]),
        ("Marketing",              [("Marketing", "Campaigns & Broadcasts")]),
        ("Reports & System",       [("Analytics", "Analytics"), ("Reports", "Reports"), ("AccessControl", "Access Control"), ("Settings", "Settings")]),
    ];

    private static readonly string[] AllModules = Groups.SelectMany(g => g.Modules.Select(m => m.Key)).ToArray();

    private static Dictionary<string, bool> Only(params string[] allowed) =>
        AllModules.ToDictionary(m => m, m => allowed.Contains(m));

    /// <summary>An explicit true/false for every known module (unknown keys dropped, missing = false).</summary>
    public static Dictionary<string, bool> Normalize(Dictionary<string, bool>? perms)
    {
        var src = perms == null ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                                : new Dictionary<string, bool>(perms, StringComparer.OrdinalIgnoreCase);
        return AllModules.ToDictionary(m => m, m => src.TryGetValue(m, out var v) && v);
    }

    public static readonly List<(string Name, string Desc, string Color, bool IsSystem, Dictionary<string, bool> Perms)> All =
    [
        ("Super Admin",
         "Full access to all modules",
         "bg-red-50 text-red-700",
         true,
         AllModules.ToDictionary(m => m, _ => true)),

        ("Manager",
         "Manages orders, returns, payments, products, website, reports and staff",
         "bg-blue-50 text-blue-700",
         true,
         Only("Dashboard","Orders","Returns","Payments","WebsiteUsers","Billing","Customers","Inventory",
              "Products","Categories","Reviews","Website","Feedback","Messages","Analytics","Reports","Staff","Marketing")),

        ("Accountant",
         "Billing, payments, GST, expenses and payroll",
         "bg-green-50 text-green-700",
         true,
         Only("Dashboard","Billing","Payments","GST","Expenses","Payroll","Reports")),

        ("Production",
         "Inventory, production and barcode operations",
         "bg-orange-50 text-orange-700",
         true,
         Only("Dashboard","Inventory","Production","Purchase","Barcode")),

        ("Sales Exec",
         "Orders, returns, customers, website users and field sales",
         "bg-purple-50 text-purple-700",
         true,
         Only("Dashboard","Orders","Returns","Customers","WebsiteUsers","FieldSales","Billing")),

        ("HR Manager",
         "Staff, attendance, leave and payroll",
         "bg-pink-50 text-pink-700",
         true,
         Only("Dashboard","Staff","Attendance","Leave","Payroll","Expenses")),
    ];

    /// <summary>
    /// Idempotent upgrade: every existing role row gets an explicit value for modules added after it was
    /// created (seeded roles → that role's default above, custom roles → false). Existing keys are never changed.
    /// Returns the number of roles updated.
    /// </summary>
    public static async Task<int> UpgradeAsync(AppDbContext db)
    {
        var roles = await db.AdminRoles.Where(r => !r.IsDeleted).ToListAsync();
        var changed = 0;
        foreach (var role in roles)
        {
            var current = AdminPermissions.Parse(role.Permissions) ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var missing = AllModules.Where(m => !current.ContainsKey(m)).ToList();
            if (missing.Count == 0) continue;
            var isSuper  = AdminAuthController.IsSuperAdminRole(role.Name);
            var defaults = All.FirstOrDefault(d => string.Equals(d.Name, role.Name, StringComparison.OrdinalIgnoreCase)).Perms;
            var merged = new Dictionary<string, bool>();
            foreach (var m in AllModules)
                merged[m] = current.TryGetValue(m, out var v) ? v
                          : isSuper || (defaults != null && defaults.TryGetValue(m, out var d) && d);
            role.Permissions = JsonSerializer.Serialize(merged);
            role.UpdatedAt   = DateTime.UtcNow;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync();
        return changed;
    }

    private static volatile bool _upgraded;
    private static readonly SemaphoreSlim UpgradeLock = new(1, 1);

    /// <summary>Runs <see cref="UpgradeAsync"/> once per process (first admin request after start-up).</summary>
    public static async Task EnsureUpgradedAsync(AppDbContext db)
    {
        if (_upgraded) return;
        await UpgradeLock.WaitAsync();
        try
        {
            if (_upgraded) return;
            await UpgradeAsync(db);
            _upgraded = true;
        }
        finally { UpgradeLock.Release(); }
    }
}
