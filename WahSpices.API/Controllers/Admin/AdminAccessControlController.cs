using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Application.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin")]
[RequireModule("AccessControl")]
public class AdminAccessControlController : AdminBaseController
{
    private readonly AppDbContext _db;

    public AdminAccessControlController(AppDbContext db) => _db = db;

    // ──────────────────────────────────────────────────────────────────────
    //  USERS
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    [RequireModule("AccessControl")]
    public async Task<IActionResult> GetUsers()
    {
        var raw = await _db.AdminUsers
            .Include(u => u.AdminRole)
            .Where(u => !u.IsDeleted)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var users = raw.Select(u => ToDto(u)).ToList();
        return Ok(ApiResponse<object>.Ok(users));
    }

    [HttpPost("users")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> CreateUser([FromBody] CreateAdminUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest(ApiResponse<object>.Fail("WEAK_PASSWORD", "Password must be at least 8 characters."));

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Name and email are required."));
        var email = request.Email.Trim().ToLower();
        if (await _db.AdminUsers.AnyAsync(u => u.Email == email && !u.IsDeleted))
            return Conflict(ApiResponse<object>.Fail("CONFLICT", "Email already exists."));

        var role = await _db.AdminRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId && !r.IsDeleted);
        if (role == null) return BadRequest(ApiResponse<object>.Fail("INVALID_ROLE", "Role not found."));

        var pwError = AdminPasswordError(request.Password, email, request.Name);
        if (pwError != null) return BadRequest(ApiResponse<object>.Fail("WEAK_PASSWORD", pwError, "password"));

        var user = new AdminUser
        {
            Name         = request.Name.Trim(),
            Email        = request.Email.Trim().ToLower(),
            Role         = role.Name,
            AdminRoleId  = role.Id,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive     = true
        };
        _db.AdminUsers.Add(user);
        await _db.SaveChangesAsync();

        user.AdminRole = role;
        await WriteAuditLog("AccessControl", $"Created admin user {user.Email} with role {role.Name}");
        return StatusCode(201, ApiResponse<object>.Ok(ToDto(user), "User created."));
    }

    [HttpPatch("users/{id:int}")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateAdminUserRequest request)
    {
        var user = await _db.AdminUsers
            .Include(u => u.AdminRole)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        var me = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var isSelf = user.Id == me;
        if (AdminAuthController.IsSuperAdminRole(user.Role) && !isSelf)
            return StatusCode(403, ApiResponse<object>.Fail("FORBIDDEN", "Super Admin accounts can only be edited by themselves."));
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Name is required."));

        var role = await _db.AdminRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId && !r.IsDeleted);
        // a legacy account without an AdminRole (e.g. the bootstrap "Admin") may keep its role when none is chosen
        var keepRole = role == null && request.RoleId == 0 && user.AdminRoleId == null;
        if (role == null && !keepRole) return BadRequest(ApiResponse<object>.Fail("INVALID_ROLE", "Role not found."));

        if (isSelf && !request.IsActive)
            return BadRequest(ApiResponse<object>.Fail("SELF_DEACTIVATE", "You cannot deactivate your own account."));
        if (isSelf && role != null && AdminAuthController.IsSuperAdminRole(user.Role) && !AdminAuthController.IsSuperAdminRole(role.Name))
            return BadRequest(ApiResponse<object>.Fail("SELF_DEMOTE", "You cannot remove your own Super Admin role."));

        // never leave the system without an active Super Admin
        var wasSuper  = AdminPermissions.IsSuperAdmin(user);
        var staysSuper = role == null ? wasSuper : AdminAuthController.IsSuperAdminRole(role.Name);
        if (wasSuper && (!staysSuper || !request.IsActive) && !await OtherActiveSuperAdminExists(user.Id))
            return BadRequest(ApiResponse<object>.Fail("LAST_SUPER_ADMIN", "This is the last active Super Admin — assign another Super Admin first."));

        user.Name       = request.Name.Trim();
        if (role != null)
        {
            user.Role        = role.Name;
            user.AdminRoleId = role.Id;
            user.AdminRole   = role;
        }
        user.IsActive   = request.IsActive;
        user.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog("AccessControl", $"Updated user {user.Email}: role={role?.Name ?? user.Role}, active={user.IsActive}");
        return Ok(ApiResponse<object>.Ok(ToDto(user), "User updated."));
    }

    [HttpDelete("users/{id:int}")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        var me = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        if (user.Id == me) return BadRequest(ApiResponse<object>.Fail("SELF_DELETE", "You cannot delete yourself."));
        if (AdminAuthController.IsSuperAdminRole(user.Role) && !await OtherActiveSuperAdminExists(user.Id))
            return BadRequest(ApiResponse<object>.Fail("LAST_SUPER_ADMIN", "This is the last active Super Admin and cannot be deleted."));
        if (AdminAuthController.IsSuperAdminRole(user.Role))
            return StatusCode(403, ApiResponse<object>.Fail("FORBIDDEN", "Super Admin accounts cannot be deleted."));

        user.IsDeleted = true;
        user.IsActive  = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog("AccessControl", $"Deleted admin user {user.Email}");
        return Ok(ApiResponse<bool>.Ok(true, "User deleted."));
    }

    [HttpPost("users/{id:int}/reset-password")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var user = await _db.AdminUsers.Include(u => u.AdminRole).FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));
        // A Super Admin can't take over another Super Admin's account by resetting its password.
        if (AdminPermissions.IsSuperAdmin(user) && user.Id != int.Parse(User.FindFirst("admin_id")?.Value ?? "0"))
            return StatusCode(403, ApiResponse<object>.Fail("FORBIDDEN", "Another Super Admin's password can't be reset. They can change it themselves."));

        var chars   = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#";
        var random  = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var tempPwd = new string(random.Select(b => chars[b % chars.Length]).ToArray());

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPwd);
        user.UpdatedAt    = DateTime.UtcNow;
        await WahSpices.API.Security.AdminSessionRevoker.RevokeAllAsync(_db, user, exceptSessionId: null);   // every session ends
        await _db.SaveChangesAsync();

        await WriteAuditLog("AccessControl", $"Password reset for user {user.Email}");
        return Ok(ApiResponse<object>.Ok(new { TempPassword = tempPwd },
            "Password has been reset. Share this temp password securely."));
    }

    /// Admin accounts guard the whole business: the customer policy (letters + digits, no common or
    /// personal passwords) plus a 10-character minimum.
    private static string? AdminPasswordError(string? password, string? email, string? name)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            return "Admin passwords must be at least 10 characters.";
        return WahSpices.Infrastructure.Security.PasswordPolicy.Validate(password, email, name);
    }

    [HttpPost("users/{id:int}/change-password")]
    [RequireModule(AdminModules.AnyAdmin)]   // own account
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangeAdminPasswordRequest request)
    {
        var me = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        if (me != id) return Forbid();

        var user = await _db.AdminUsers.FindAsync(id);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(ApiResponse<object>.Fail("WRONG_PASSWORD", "Current password is incorrect."));

        var pwError = AdminPasswordError(request.NewPassword, user.Email, user.Name);
        if (pwError != null) return BadRequest(ApiResponse<object>.Fail("WEAK_PASSWORD", pwError, "newPassword"));
        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            return BadRequest(ApiResponse<object>.Fail("WEAK_PASSWORD", "The new password must be different from the current one.", "newPassword"));

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt    = DateTime.UtcNow;
        // other devices are signed out; this session stays
        int? current = int.TryParse(User.FindFirst("wsid")?.Value, out var sid) ? sid : null;
        await WahSpices.API.Security.AdminSessionRevoker.RevokeAllAsync(_db, user, current);
        await _db.SaveChangesAsync();

        await WriteAuditLog("AccessControl", "Changed own password");
        return Ok(ApiResponse<bool>.Ok(true, "Password changed."));
    }

    [HttpPut("users/{id:int}/permissions")]
    [RequireModule(AdminModules.SuperAdminOnly)]
    public async Task<IActionResult> UpdatePermissions(int id, [FromBody] UpdatePermissionsRequest request)
    {
        var user = await _db.AdminUsers
            .Include(u => u.AdminRole)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        if (AdminPermissions.IsSuperAdmin(user))
            return BadRequest(ApiResponse<object>.Fail("SUPER_ADMIN", "Super Admins always have every module — overrides don't apply."));

        // overrides are sparse: only modules whose value differs from the role are stored, so later role
        // changes still flow through for everything else. Unknown module keys are dropped.
        var rolePerms = AdminPermissions.Parse(user.AdminRole is { IsDeleted: false, IsActive: true } ? user.AdminRole.Permissions : null)
                        ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, bool>();
        foreach (var kv in request.Permissions ?? new Dictionary<string, bool>())
        {
            var key = DefaultRoles.Modules.FirstOrDefault(m => string.Equals(m, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            var roleValue = rolePerms.TryGetValue(key, out var rv) && rv;
            if (kv.Value != roleValue) overrides[key] = kv.Value;
        }
        user.Permissions = overrides.Count == 0 ? null : JsonSerializer.Serialize(overrides);
        user.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog("AccessControl", $"Updated permissions for user {user.Email}");
        return Ok(ApiResponse<object>.Ok(ToDto(user), "Permissions saved."));
    }

    [HttpPost("users/{id:int}/unlock")]
    [RequireModule("AccessControl")]
    public async Task<IActionResult> UnlockUser(int id)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        user.IsLocked            = false;
        user.LockedUntil         = null;
        user.FailedLoginAttempts = 0;
        user.UpdatedAt           = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog("AccessControl", $"Unlocked account for user {user.Email}");
        return Ok(ApiResponse<bool>.Ok(true, "Account unlocked."));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  SESSIONS
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("sessions")]
    [RequireModule(AdminModules.AnyAdmin)]   // own account
    public async Task<IActionResult> GetSessions()
    {
        var me  = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";

        var sessions = await _db.AdminSessions
            .Where(s => s.AdminUserId == me && s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.LastActiveAt)
            .Select(s => new AdminSessionDto(
                s.Id, s.IpAddress, s.UserAgent, s.Location,
                s.CreatedAt, s.ExpiresAt, s.LastActiveAt,
                s.JwtJti == jti))
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(sessions));
    }

    [HttpDelete("sessions/{sessionId:int}")]
    [RequireModule(AdminModules.AnyAdmin)]   // own account
    public async Task<IActionResult> RevokeSession(int sessionId)
    {
        var me = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var session = await _db.AdminSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.AdminUserId == me);

        if (session == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Session not found."));

        session.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog("Session", $"Revoked session from {session.IpAddress}");
        return Ok(ApiResponse<bool>.Ok(true, "Session revoked."));
    }

    [HttpDelete("sessions")]
    [RequireModule(AdminModules.AnyAdmin)]   // own account
    public async Task<IActionResult> RevokeOtherSessions()
    {
        var me  = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";

        var others = await _db.AdminSessions
            .Where(s => s.AdminUserId == me && s.JwtJti != jti && s.RevokedAt == null)
            .ToListAsync();

        foreach (var s in others) s.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog("Session", $"Revoked {others.Count} other session(s)");
        return Ok(ApiResponse<object>.Ok(new { Revoked = others.Count }, $"Revoked {others.Count} session(s)."));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  AUDIT LOG
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("audit-log")]
    [RequireModule("AccessControl")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] string? userId,
        [FromQuery] string? module,
        [FromQuery] string? action,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page  = 1,
        [FromQuery] int limit = 25)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(l => l.UserId == userId);
        if (!string.IsNullOrWhiteSpace(module)) query = query.Where(l => l.Module.Contains(module));
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action.Contains(action));
        if (dateFrom.HasValue) query = query.Where(l => l.CreatedAt >= dateFrom);
        if (dateTo.HasValue)   query = query.Where(l => l.CreatedAt < dateTo.Value.Date.AddDays(1));

        var total = await query.CountAsync();
        var logs  = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(l => new AuditLogDto(l.Id, l.UserId, l.UserName, l.Module, l.Action, l.Details, l.IpAddress, l.CreatedAt))
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Items      = logs,
            Total      = total,
            Page       = page,
            Limit      = limit,
            TotalPages = (int)Math.Ceiling((double)total / limit)
        }));
    }

    /// <summary>Distinct audit-log modules (drives the audit filter dropdown).</summary>
    [HttpGet("audit-log/modules")]
    [RequireModule("AccessControl")]
    public async Task<IActionResult> GetAuditModules()
    {
        var modules = await _db.AuditLogs.Select(l => l.Module).Distinct().OrderBy(m => m).ToListAsync();
        return Ok(ApiResponse<object>.Ok(modules));
    }

    // Settings endpoints live in AdminSettingsController (DB-backed).

    [RequireModule(AdminModules.SuperAdminOnly)]
    [HttpPost("backup")]
    public IActionResult Backup() =>
        Ok(ApiResponse<object>.Ok(new { BackupId = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}", Status = "Initiated" }));

    // ──────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────────

    private static AdminUserDetailDto ToDto(AdminUser u) => new(
        u.Id, u.Name, u.Email, u.Role, u.AdminRoleId,
        u.IsActive, u.IsTwoFactorEnabled, u.IsLocked,
        u.LastLoginAt, u.CreatedAt,
        AdminPermissions.Parse(u.Permissions),      // the user's own overrides only
        AdminPermissions.Effective(u));            // every module: role + overrides (Super Admin → all true)

    private Task<bool> OtherActiveSuperAdminExists(int exceptId) =>
        _db.AdminUsers.AnyAsync(a => a.Id != exceptId && !a.IsDeleted && a.IsActive &&
            ((a.AdminRole != null && (a.AdminRole.Name == "Super Admin" || a.AdminRole.Name == "Admin")) ||
             (a.AdminRole == null && (a.Role == "Super Admin" || a.Role == "Admin" || a.Role == "SuperAdmin"))));

    private async Task WriteAuditLog(string module, string action)
    {
        var adminId   = User.FindFirst("admin_id")?.Value ?? "0";
        var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        var ip        = HttpContext.Connection.RemoteIpAddress?.ToString();

        _db.AuditLogs.Add(new AuditLog
        {
            UserId    = adminId,
            UserName  = adminName,
            Module    = module,
            Action    = action,
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
