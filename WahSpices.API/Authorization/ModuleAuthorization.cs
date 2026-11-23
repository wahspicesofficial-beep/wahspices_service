using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Controllers.Admin;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Authorization;

/// <summary>
/// Tags an admin controller (or a single action) with the permission module(s) it belongs to.
/// A method-level attribute REPLACES the controller-level one. Several modules = any of them grants access.
///   [RequireModule("Orders")]                          — needs the Orders permission
///   [RequireModule("Analytics", "Dashboard")]          — either permission is enough
///   [RequireModule(AdminModules.AnyAdmin)]             — any signed-in admin (lookups, settings GET, auth/me …)
///   [RequireModule(AdminModules.SuperAdminOnly)]       — Super Admin only (privilege changes)
/// Every controller deriving from AdminBaseController MUST carry one (directly or on its actions);
/// an untagged admin API is Super-Admin-only (fail closed).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class RequireModuleAttribute : Attribute
{
    public string[] Modules { get; }
    public RequireModuleAttribute(params string[] modules) => Modules = modules ?? [];
}

public static class AdminModules
{
    /// <summary>Any authenticated admin, whatever their permissions.</summary>
    public const string AnyAdmin = "*";
    /// <summary>Only a Super Admin (checked against the DB, not the JWT claim).</summary>
    public const string SuperAdminOnly = "!SuperAdmin";

    /// <summary>Human-readable names used in 403 messages (mirror the sidebar labels).</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["WebsiteUsers"]  = "Website Users",
        ["FieldSales"]    = "Field Sales",
        ["AccessControl"] = "Access Control",
        ["Website"]       = "Website Content",
        ["Messages"]      = "Contact Messages",
        ["Billing"]       = "Billing & Invoice",
        ["GST"]           = "GST & Tax",
    };

    public static string Label(string module) => Labels.TryGetValue(module, out var l) ? l : module;
}

/// <summary>The caller's effective access, computed once per request (one DB read).</summary>
public sealed record AdminAccess(int AdminId, string RoleName, bool IsSuperAdmin, bool IsActive, Dictionary<string, bool> Permissions)
{
    public bool Can(string module) => IsSuperAdmin || (Permissions.TryGetValue(module, out var v) && v);
}

public static class AdminPermissions
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static Dictionary<string, bool>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, JsonOpts);
            return d == null ? null : new Dictionary<string, bool>(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Super Admin = role "Super Admin" (or the legacy bootstrap role "Admin" with no AdminRole row).</summary>
    public static bool IsSuperAdmin(AdminUser u) =>
        AdminAuthController.IsSuperAdminRole(u.AdminRole?.Name ?? u.Role);

    /// <summary>
    /// Effective permissions over ALL modules: Super Admin → all true; otherwise role permissions
    /// (ignored when the role is deactivated) overlaid with the user's own overrides; a missing key = false.
    /// </summary>
    public static Dictionary<string, bool> Effective(AdminUser u)
    {
        var all = DefaultRoles.Modules;
        if (IsSuperAdmin(u)) return all.ToDictionary(m => m, _ => true);

        var role = u.AdminRole is { IsDeleted: false, IsActive: true } ? Parse(u.AdminRole.Permissions) : null;
        var user = Parse(u.Permissions);
        var result = new Dictionary<string, bool>();
        foreach (var m in all)
        {
            var v = false;
            if (role != null && role.TryGetValue(m, out var rv)) v = rv;
            if (user != null && user.TryGetValue(m, out var uv)) v = uv;
            result[m] = v;
        }
        return result;
    }

    private const string ItemKey = "__wah_admin_access";

    /// <summary>Loads (and caches on the HttpContext) the current admin's effective access. Null = not an admin token.</summary>
    public static async Task<AdminAccess?> GetAccessAsync(HttpContext http)
    {
        if (http.Items.TryGetValue(ItemKey, out var cached)) return cached as AdminAccess;
        AdminAccess? access = null;
        if (int.TryParse(http.User.FindFirst("admin_id")?.Value, out var id) && http.User.FindFirst("temp_2fa") == null)
        {
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            await DefaultRoles.EnsureUpgradedAsync(db);
            var u = await db.AdminUsers.AsNoTracking().Include(a => a.AdminRole)
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
            if (u != null)
                access = new AdminAccess(u.Id, u.AdminRole?.Name ?? u.Role, IsSuperAdmin(u), u.IsActive, Effective(u));
        }
        http.Items[ItemKey] = access;
        return access;
    }
}

/// <summary>
/// Global MVC authorization filter (registered in ServiceExtensions.AddJwtAuthentication).
/// Applies to every controller deriving from AdminBaseController and to any controller/action tagged
/// with [RequireModule]. Reads permissions from the DB on each request, so role/override changes
/// take effect on the very next request — no re-login needed.
/// </summary>
public sealed class ModulePermissionFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor cad) return;

        var method = cad.MethodInfo;
        var ctrl   = cad.ControllerTypeInfo;
        if (method.GetCustomAttribute<AllowAnonymousAttribute>() != null) return;

        var attr = method.GetCustomAttribute<RequireModuleAttribute>(inherit: true)
                   ?? ctrl.GetCustomAttribute<RequireModuleAttribute>(inherit: true);
        var isAdminController = typeof(AdminBaseController).IsAssignableFrom(ctrl.AsType());
        if (attr == null && !isAdminController) return;   // public / customer APIs are not module-scoped

        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true) return;   // [Authorize] already produced the 401

        var access = await AdminPermissions.GetAccessAsync(http);
        if (access == null)
        {
            context.Result = Deny("FORBIDDEN", "Admin access required.");
            return;
        }
        if (!access.IsActive)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail("INACTIVE", "Your admin account is deactivated.")) { StatusCode = 401 };
            return;
        }
        if (access.IsSuperAdmin) return;

        var modules = attr?.Modules ?? [];
        if (modules.Contains(AdminModules.AnyAdmin)) return;

        if (modules.Length == 0 || modules.Contains(AdminModules.SuperAdminOnly))
        {
            context.Result = Deny("SUPER_ADMIN_ONLY", "Only a Super Admin can do this.");
            return;
        }
        if (modules.Any(access.Can)) return;

        var names = string.Join(" or ", modules.Select(AdminModules.Label));
        context.Result = Deny("MODULE_FORBIDDEN", $"Your role doesn't have access to {names}.");
    }

    private static ObjectResult Deny(string code, string message) =>
        new(ApiResponse<object>.Fail(code, message)) { StatusCode = StatusCodes.Status403Forbidden };
}
