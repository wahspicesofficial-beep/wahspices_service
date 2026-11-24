using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using WahSpices.API.Authorization;
using WahSpices.API.Security;
using WahSpices.Application.DTOs.Admin;
using WahSpices.Application.Services;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Security;
using WahSpices.Application.Common;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using WahSpices.Domain.Entities;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Admin sign-in. Access tokens live 15 minutes and always belong to an AdminSession row (checked per request:
/// revoke / idle timeout / IP allow-list). The refresh token (httpOnly cookie, rotated on use, stored only as
/// SHA-256 in AdminUser.RefreshToken keyed by session) keeps the session going until its absolute expiry.
/// Policy from Settings → Security: enforce 2FA (setup forced at next login), idle timeout, max sessions, IP allow-list.
/// </summary>
[ApiController]
[Route("api/admin/auth")]
[Produces("application/json")]
[RequireModule(AdminModules.AnyAdmin)]   // own profile / 2FA / logout — any admin
public class AdminAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminAuthController> _logger;
    private readonly IMemoryCache _cache;
    private readonly AdminSecuritySettingsService _security;

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes    = 15;
    private const string GenericError   = "Invalid email or password.";

    // A real BCrypt hash so unknown e-mails cost the same time as real ones.
    private static readonly string DummyBcrypt = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    public AdminAuthController(AppDbContext db, IConfiguration config, ILogger<AdminAuthController> logger,
                               IMemoryCache cache, AdminSecuritySettingsService security)
    {
        _db = db; _config = config; _logger = logger; _cache = cache; _security = security;
    }

    private int AccessMinutes => Math.Clamp(_config.GetValue<int>("Jwt:AdminAccessTokenMinutes", 15), 1, 60);
    private int SessionHours  => Math.Clamp(_config.GetValue<int>("Jwt:AdminExpiryHours", 8), 1, 72);

    // ── Login ─────────────────────────────────────────────────────────────
    [AllowAnonymous]
    [HttpPost("login")]
    [RequireHumanProof("admin-auth")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request)
    {
        var settings = await _security.GetAsync(_db);
        if (!AdminSecuritySettingsService.IpAllowed(settings, HttpContext))
        {
            _logger.LogWarning("Admin login blocked by IP allow-list from {Ip}", ClientIp.Get(HttpContext));
            return StatusCode(403, ApiResponse<object>.Fail("IP_NOT_ALLOWED", "Admin sign-in isn't allowed from this network."));
        }

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var admin = email.Length == 0 ? null : await _db.AdminUsers
            .Include(a => a.AdminRole)
            .FirstOrDefaultAsync(a => a.Email == email && !a.IsDeleted);

        if (admin == null)
        {
            // Unknown e-mail: same BCrypt cost and the same lockout behaviour (tracked in memory) → no enumeration.
            var key = "admin-login-fail:" + email;
            var state = _cache.Get<(int fails, DateTime? until)>(key);
            if (state.until > DateTime.UtcNow) return LockedResult(state.until.Value);
            BCrypt.Net.BCrypt.Verify(request.Password ?? "", DummyBcrypt);
            state.fails++;
            if (state.fails >= MaxFailedAttempts) { state.until = DateTime.UtcNow.AddMinutes(LockoutMinutes); state.fails = 0; }
            _cache.Set(key, state, TimeSpan.FromHours(1));
            return Unauthorized(ApiResponse<object>.Fail("INVALID_CREDENTIALS", GenericError));
        }

        // Account locked?
        if (admin.IsLocked && admin.LockedUntil > DateTime.UtcNow)
            return LockedResult(admin.LockedUntil!.Value);

        if (!BCrypt.Net.BCrypt.Verify(request.Password ?? "", admin.PasswordHash))
        {
            await RegisterFailure(admin);
            return Unauthorized(ApiResponse<object>.Fail("INVALID_CREDENTIALS", GenericError));
        }

        // Correct password from here on — specific messages are fine.
        if (!admin.IsActive)
            return Unauthorized(ApiResponse<object>.Fail("INACTIVE", "Account is deactivated."));

        // 2FA required?
        if (admin.IsTwoFactorEnabled)
        {
            return Ok(ApiResponse<object>.Ok(new
            {
                Requires2FA = true,
                TempToken   = GenerateTempToken(admin, "temp_2fa", 5)
            }, "2FA required."));
        }

        // Invited / reset account: choose a new password before any session exists.
        if (admin.MustChangePassword) return PasswordChangeStep(admin);

        // 2FA enforced by policy but not set up yet → walk the admin through setup before any session exists.
        if (settings.Enforce2FA)
        {
            return Ok(ApiResponse<object>.Ok(new
            {
                Requires2FASetup = true,
                SetupToken       = GenerateTempToken(admin, "temp_2fa_setup", 10)
            }, "Two-factor authentication is required. Set it up to continue."));
        }

        return await IssueTokenAndSession(admin);
    }

    // ── 2FA verify during login ───────────────────────────────────────────
    [AllowAnonymous]
    [HttpPost("2fa/verify-login")]
    [RequireHumanProof("admin-auth")]
    public async Task<IActionResult> VerifyLogin([FromBody] VerifyLoginRequest request)
    {
        // Validate temp token
        var principal = ValidateTempToken(request.TempToken, "temp_2fa");
        if (principal == null)
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));

        var adminId = int.Parse(principal.FindFirst("admin_id")?.Value ?? "0");
        var admin   = await _db.AdminUsers.Include(a => a.AdminRole).FirstOrDefaultAsync(a => a.Id == adminId && !a.IsDeleted);
        if (admin == null || !admin.IsActive)
            return Unauthorized(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));
        if (admin.IsLocked && admin.LockedUntil > DateTime.UtcNow)
            return LockedResult(admin.LockedUntil!.Value);
        if (!AdminSecuritySettingsService.IpAllowed(await _security.GetAsync(_db), HttpContext))
            return StatusCode(403, ApiResponse<object>.Fail("IP_NOT_ALLOWED", "Admin sign-in isn't allowed from this network."));

        var code = (request.Code ?? "").Trim();
        // Verify TOTP or backup code
        bool valid = false;
        if (code.Length == 6 && admin.TotpSecret != null)
        {
            valid = TotpService.Verify(admin.TotpSecret, code);
        }
        else if (code.Length == 8 && admin.TotpBackupCodes != null)
        {
            var codes = JsonSerializer.Deserialize<List<string>>(admin.TotpBackupCodes) ?? [];
            valid = TotpService.VerifyBackupCode(code, codes);
            if (valid)
            {
                // Consume the backup code
                var hash    = TotpService.HashBackupCode(code.ToUpperInvariant());
                var updated = codes.Where(c => c != hash).ToList();
                admin.TotpBackupCodes = JsonSerializer.Serialize(updated);
            }
        }

        if (!valid)
        {
            // 2FA guesses count toward the same lockout as wrong passwords (no unlimited TOTP brute force).
            await RegisterFailure(admin);
            return Unauthorized(ApiResponse<object>.Fail("INVALID_CODE", "Invalid or expired 2FA code."));
        }

        if (!ConsumeTempToken(principal))
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));

        if (admin.MustChangePassword)
        {
            await _db.SaveChangesAsync();   // keep a consumed backup code consumed
            return PasswordChangeStep(admin);
        }
        return await IssueTokenAndSession(admin);
    }

    // ── Forced password change at first sign-in (invited or reset accounts) ──
    private IActionResult PasswordChangeStep(AdminUser admin) =>
        Ok(ApiResponse<object>.Ok(new
        {
            RequiresPasswordChange = true,
            ChangeToken            = GenerateTempToken(admin, "temp_pwchange", 15)
        }, "Please choose a new password to finish signing in."));

    [AllowAnonymous]
    [HttpPost("first-password")]
    [RequireHumanProof("admin-auth")]
    public async Task<IActionResult> SetFirstPassword([FromBody] FirstPasswordRequest request)
    {
        var principal = ValidateTempToken(request.ChangeToken, "temp_pwchange");
        if (principal == null)
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));

        var adminId = int.Parse(principal.FindFirst("admin_id")?.Value ?? "0");
        var admin = await _db.AdminUsers.Include(a => a.AdminRole).FirstOrDefaultAsync(a => a.Id == adminId && !a.IsDeleted);
        if (admin == null || !admin.IsActive)
            return Unauthorized(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));
        if (admin.IsLocked && admin.LockedUntil > DateTime.UtcNow)
            return LockedResult(admin.LockedUntil!.Value);
        var settings = await _security.GetAsync(_db);
        if (!AdminSecuritySettingsService.IpAllowed(settings, HttpContext))
            return StatusCode(403, ApiResponse<object>.Fail("IP_NOT_ALLOWED", "Admin sign-in isn't allowed from this network."));
        if (!admin.MustChangePassword)
            return BadRequest(ApiResponse<object>.Fail("NOT_REQUIRED", "Your password is already set. Please sign in again."));

        var newPassword = request.NewPassword ?? "";
        var pwError = AdminAccessControlController.AdminPasswordError(newPassword, admin.Email, admin.Name);
        if (pwError != null)
            return BadRequest(ApiResponse<object>.Fail("WEAK_PASSWORD", pwError, "newPassword"));
        if (BCrypt.Net.BCrypt.Verify(newPassword, admin.PasswordHash))
            return BadRequest(ApiResponse<object>.Fail("SAME_PASSWORD", "Choose a new password — it can't be the temporary one.", "newPassword"));

        if (!ConsumeTempToken(principal))
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));

        admin.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(newPassword);
        admin.MustChangePassword = false;
        admin.UpdatedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await WriteAuditLog(admin.Id, admin.Name, "Set own password at first sign-in");

        // Password done — now the 2FA policy, exactly as a normal sign-in would continue.
        if (!admin.IsTwoFactorEnabled && settings.Enforce2FA)
            return Ok(ApiResponse<object>.Ok(new
            {
                Requires2FASetup = true,
                SetupToken       = GenerateTempToken(admin, "temp_2fa_setup", 10)
            }, "Password updated. Two-factor authentication is required — set it up to continue."));
        return await IssueTokenAndSession(admin);
    }

    // ── Forced 2FA setup during login (policy "enforce 2FA") ─────────────
    [AllowAnonymous]
    [HttpPost("2fa/setup-login")]
    public async Task<IActionResult> SetupLogin([FromBody] SetupLoginRequest request)
    {
        var principal = ValidateTempToken(request.SetupToken, "temp_2fa_setup");
        if (principal == null)
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));
        var admin = await _db.AdminUsers.FirstOrDefaultAsync(a => a.Id == int.Parse(principal.FindFirst("admin_id")!.Value) && !a.IsDeleted);
        if (admin == null || !admin.IsActive)
            return Unauthorized(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));
        if (admin.IsTwoFactorEnabled)
            return BadRequest(ApiResponse<object>.Fail("ALREADY_ENABLED", "2FA is already enabled — sign in again and enter your code."));

        var (secret, qrUri, backupCodes) = BeginTotpSetup(admin);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<Setup2FAResponse>.Ok(new Setup2FAResponse(secret, qrUri, backupCodes),
            "Scan the QR code with your authenticator app, then enter the 6-digit code."));
    }

    [AllowAnonymous]
    [HttpPost("2fa/enable-login")]
    [RequireHumanProof("admin-auth")]
    public async Task<IActionResult> EnableLogin([FromBody] EnableLoginRequest request)
    {
        var principal = ValidateTempToken(request.SetupToken, "temp_2fa_setup");
        if (principal == null)
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));
        var admin = await _db.AdminUsers.Include(a => a.AdminRole)
            .FirstOrDefaultAsync(a => a.Id == int.Parse(principal.FindFirst("admin_id")!.Value) && !a.IsDeleted);
        if (admin == null || !admin.IsActive)
            return Unauthorized(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));
        if (admin.IsLocked && admin.LockedUntil > DateTime.UtcNow)
            return LockedResult(admin.LockedUntil!.Value);
        if (admin.TotpSecret == null)
            return BadRequest(ApiResponse<object>.Fail("NO_SECRET", "Run setup first."));
        if (!TotpService.Verify(admin.TotpSecret, (request.Code ?? "").Trim()))
        {
            await RegisterFailure(admin);
            return BadRequest(ApiResponse<object>.Fail("INVALID_CODE", "Incorrect code. Try again."));
        }
        if (!ConsumeTempToken(principal))
            return Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your sign-in step expired. Please sign in again."));

        admin.IsTwoFactorEnabled = true;
        admin.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await WriteAuditLog(admin.Id, admin.Name, "2FA enabled (required by security policy)");
        return await IssueTokenAndSession(admin);
    }

    // ── Refresh (rotating refresh token in an httpOnly cookie) ───────────
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] AdminRefreshRequest? request)
    {
        var invalid = Unauthorized(ApiResponse<object>.Fail("INVALID_TOKEN", "Your session has expired. Please sign in again."));
        if (!RefreshCookies.OriginAllowed(HttpContext))
            return StatusCode(403, ApiResponse<object>.Fail("FORBIDDEN_ORIGIN", "Request origin not allowed."));
        var token = RefreshCookies.Read(HttpContext, RefreshCookies.Admin, request?.RefreshToken);
        if (!RefreshSessions.TryParseToken(token, out var owner, out var key) || !int.TryParse(owner, out var adminId) || !int.TryParse(key, out var sessionId))
        {
            RefreshCookies.Clear(HttpContext, RefreshCookies.Admin);
            return invalid;
        }

        using var _ = await KeyedLock.AcquireAsync("admin:" + adminId);
        var admin   = await _db.AdminUsers.Include(a => a.AdminRole).FirstOrDefaultAsync(a => a.Id == adminId && !a.IsDeleted);
        var session = await _db.AdminSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.AdminUserId == adminId);
        var settings = await _security.GetAsync(_db);

        if (admin == null || !admin.IsActive || session == null || session.RevokedAt != null || session.ExpiresAt < DateTime.UtcNow
            || (settings.IdleTimeoutMinutes > 0 && session.LastActiveAt < DateTime.UtcNow.AddMinutes(-settings.IdleTimeoutMinutes))
            || !AdminSecuritySettingsService.IpAllowed(settings, HttpContext))
        {
            if (session != null && session.RevokedAt == null && (session.ExpiresAt < DateTime.UtcNow ||
                (settings.IdleTimeoutMinutes > 0 && session.LastActiveAt < DateTime.UtcNow.AddMinutes(-settings.IdleTimeoutMinutes))))
            {
                session.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            RefreshCookies.Clear(HttpContext, RefreshCookies.Admin);
            return invalid;
        }

        var sessions = RefreshSessions.Parse(admin.RefreshToken);
        string? next = null;
        switch (RefreshSessions.Check(sessions, key, token!, out var rs))
        {
            case RefreshCheck.Valid:
                next = RefreshSessions.Rotate(rs!, owner, TimeSpan.FromHours(SessionHours), TimeSpan.FromHours(SessionHours));
                rs!.ExpiresAt = new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero).ToUnixTimeSeconds();
                break;
            case RefreshCheck.Grace:
                break;
            case RefreshCheck.Reused:
                _logger.LogWarning("Admin refresh token REUSE for admin {AdminId} — all sessions revoked", adminId);
                foreach (var s in await _db.AdminSessions.Where(s => s.AdminUserId == adminId && s.RevokedAt == null).ToListAsync())
                    s.RevokedAt = DateTime.UtcNow;
                admin.RefreshToken = null;
                admin.RefreshTokenExpiry = null;
                await _db.SaveChangesAsync();
                await WriteAuditLog(adminId, admin.Name, "Refresh token reuse detected — all sessions revoked");
                RefreshCookies.Clear(HttpContext, RefreshCookies.Admin);
                return Unauthorized(ApiResponse<object>.Fail("REFRESH_REUSED", "For your security all your sessions were signed out. Please sign in again."));
            default:
                RefreshCookies.Clear(HttpContext, RefreshCookies.Admin);
                return invalid;
        }

        var jti = Guid.NewGuid().ToString();
        session.JwtJti = jti;                       // "current session" markers in Access Control keep working
        session.LastActiveAt = DateTime.UtcNow;
        admin.RefreshToken = RefreshSessions.Serialize(sessions);
        await _db.SaveChangesAsync();

        if (next != null) RefreshCookies.Set(HttpContext, RefreshCookies.Admin, next, new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero));
        return Ok(ApiResponse<object>.Ok(new
        {
            Admin        = ProfilePayload(admin, includeActive: false),
            Token        = GenerateJwt(admin, jti, session.Id, DateTime.UtcNow.AddMinutes(AccessMinutes)),
            RefreshToken = RefreshCookies.ReturnInBody(HttpContext) ? next ?? "" : "",
            ExpiresIn    = AccessMinutes * 60
        }));
    }

    // ── Get my profile ────────────────────────────────────────────────────
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile()
    {
        var adminId = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var admin   = await _db.AdminUsers.Include(a => a.AdminRole).FirstOrDefaultAsync(a => a.Id == adminId);
        if (admin == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Admin not found."));

        await DefaultRoles.EnsureUpgradedAsync(_db);
        return Ok(ApiResponse<object>.Ok(ProfilePayload(admin, includeActive: true)));
    }


    // ── Logout ────────────────────────────────────────────────────────────
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var adminId = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        AdminSession? session = null;
        if (int.TryParse(User.FindFirst("wsid")?.Value, out var sid))
            session = await _db.AdminSessions.FirstOrDefaultAsync(s => s.Id == sid && s.AdminUserId == adminId);
        else if (jti != null)
            session = await _db.AdminSessions.FirstOrDefaultAsync(s => s.JwtJti == jti);
        if (session != null)
        {
            using var _ = await KeyedLock.AcquireAsync("admin:" + adminId);
            session.RevokedAt = DateTime.UtcNow;
            var admin = await _db.AdminUsers.FindAsync(adminId);
            if (admin != null)
            {
                var sessions = RefreshSessions.Parse(admin.RefreshToken);
                sessions.RemoveAll(s => s.Key == session.Id.ToString());
                admin.RefreshToken = RefreshSessions.Serialize(sessions);
            }
            await _db.SaveChangesAsync();
        }
        RefreshCookies.Clear(HttpContext, RefreshCookies.Admin);
        return Ok(ApiResponse<bool>.Ok(true, "Logged out."));
    }

    // ── 2FA Setup (generate secret + QR) ─────────────────────────────────
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("2fa/setup")]
    public async Task<IActionResult> Setup2FA()
    {
        var adminId = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var admin   = await _db.AdminUsers.FindAsync(adminId);
        if (admin == null) return NotFound();
        if (admin.IsTwoFactorEnabled)
            return BadRequest(ApiResponse<object>.Fail("ALREADY_ENABLED", "2FA is already enabled."));

        var secret      = TotpService.GenerateSecret();
        var qrUri       = TotpService.BuildQrUri(secret, admin.Email);
        var backupCodes = TotpService.GenerateBackupCodes(8);
        var hashed      = backupCodes.Select(TotpService.HashBackupCode).ToArray();

        // Store secret temporarily (not enabled yet until verified)
        admin.TotpSecret      = secret;
        admin.TotpBackupCodes = JsonSerializer.Serialize(hashed);
        admin.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Setup2FAResponse>.Ok(
            new Setup2FAResponse(secret, qrUri, backupCodes),
            "Scan the QR code and verify to enable 2FA."));
    }

    // ── 2FA Enable (verify code after scanning QR) ────────────────────────
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("2fa/enable")]
    public async Task<IActionResult> Enable2FA([FromBody] Confirm2FARequest request)
    {
        var adminId = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var admin   = await _db.AdminUsers.FindAsync(adminId);
        if (admin == null || admin.TotpSecret == null)
            return BadRequest(ApiResponse<object>.Fail("NO_SECRET", "Run setup first."));

        if (!TotpService.Verify(admin.TotpSecret, request.Code))
            return BadRequest(ApiResponse<object>.Fail("INVALID_CODE", "Incorrect code. Try again."));

        admin.IsTwoFactorEnabled = true;
        admin.UpdatedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog(adminId, User.FindFirst(ClaimTypes.Name)?.Value ?? "?", "2FA enabled");
        return Ok(ApiResponse<bool>.Ok(true, "2FA enabled successfully."));
    }

    // ── 2FA Disable ───────────────────────────────────────────────────────
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> Disable2FA([FromBody] ChangeAdminPasswordRequest request)
    {
        var adminId = int.Parse(User.FindFirst("admin_id")?.Value ?? "0");
        var admin   = await _db.AdminUsers.FindAsync(adminId);
        if (admin == null) return NotFound();

        if ((await _security.GetAsync(_db)).Enforce2FA)
            return BadRequest(ApiResponse<object>.Fail("2FA_REQUIRED", "Two-factor authentication is required for all admins by the security policy."));

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword ?? "", admin.PasswordHash))
            return BadRequest(ApiResponse<object>.Fail("WRONG_PASSWORD", "Password is incorrect."));

        admin.IsTwoFactorEnabled = false;
        admin.TotpSecret         = null;
        admin.TotpBackupCodes    = null;
        admin.UpdatedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await WriteAuditLog(adminId, User.FindFirst(ClaimTypes.Name)?.Value ?? "?", "2FA disabled");
        return Ok(ApiResponse<bool>.Ok(true, "2FA disabled."));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<IActionResult> IssueTokenAndSession(AdminUser admin)
    {
        var settings = await _security.GetAsync(_db);
        var ip = ClientIp.Get(HttpContext);
        var ua = Request.Headers.UserAgent.FirstOrDefault();
        var jti = Guid.NewGuid().ToString();
        var sessionExpiry = DateTime.UtcNow.AddHours(SessionHours);
        AdminSession session;
        string refresh;

        using (await KeyedLock.AcquireAsync("admin:" + admin.Id))
        {
            // Reset failed attempts on successful login
            admin.FailedLoginAttempts = 0;
            admin.IsLocked            = false;
            admin.LockedUntil         = null;
            admin.LastLoginAt         = DateTime.UtcNow;

            // Max concurrent sessions: sign the oldest out so the new one fits.
            if (settings.MaxConcurrentSessions > 0)
            {
                var active = await _db.AdminSessions
                    .Where(s => s.AdminUserId == admin.Id && s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(s => s.LastActiveAt).ToListAsync();
                foreach (var old in active.Skip(settings.MaxConcurrentSessions - 1))
                    old.RevokedAt = DateTime.UtcNow;
            }

            session = new AdminSession
            {
                AdminUserId  = admin.Id,
                JwtJti       = jti,
                IpAddress    = ip.Length > 64 ? ip[..64] : ip,
                UserAgent    = ua?[..Math.Min(ua.Length, 512)],
                ExpiresAt    = sessionExpiry,
                LastActiveAt = DateTime.UtcNow,
                CreatedAt    = DateTime.UtcNow
            };
            _db.AdminSessions.Add(session);
            await _db.SaveChangesAsync();   // need session.Id for the refresh token

            var sessions = RefreshSessions.Parse(admin.RefreshToken);
            var activeIds = (await _db.AdminSessions
                .Where(s => s.AdminUserId == admin.Id && s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow)
                .Select(s => s.Id).ToListAsync()).Select(i => i.ToString()).ToHashSet();
            sessions.RemoveAll(s => !activeIds.Contains(s.Key));    // drop refresh entries of revoked sessions
            refresh = RefreshSessions.Create(sessions, admin.Id.ToString(), session.Id.ToString(), TimeSpan.FromHours(SessionHours));
            admin.RefreshToken       = RefreshSessions.Serialize(sessions);
            admin.RefreshTokenExpiry = sessionExpiry;
            await _db.SaveChangesAsync();
        }

        await WriteAuditLog(admin.Id, admin.Name, $"Login from {ip}");

        await DefaultRoles.EnsureUpgradedAsync(_db);

        RefreshCookies.Set(HttpContext, RefreshCookies.Admin, refresh, new DateTimeOffset(sessionExpiry, TimeSpan.Zero));
        var result = new
        {
            Admin        = ProfilePayload(admin, includeActive: false),
            Token        = GenerateJwt(admin, jti, session.Id, DateTime.UtcNow.AddMinutes(AccessMinutes)),
            RefreshToken = RefreshCookies.ReturnInBody(HttpContext) ? refresh : "",
            ExpiresIn    = AccessMinutes * 60
        };

        _logger.LogInformation("Admin {Email} logged in", admin.Email);
        return Ok(ApiResponse<object>.Ok(result, "Login successful"));
    }

    private async Task RegisterFailure(AdminUser admin)
    {
        admin.FailedLoginAttempts++;
        if (admin.FailedLoginAttempts >= MaxFailedAttempts)
        {
            admin.IsLocked    = true;
            admin.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            admin.FailedLoginAttempts = 0;
            _logger.LogWarning("Admin {Email} locked for {Min} min after {N} failed attempts", admin.Email, LockoutMinutes, MaxFailedAttempts);
        }
        await _db.SaveChangesAsync();
    }

    private ObjectResult LockedResult(DateTime until)
    {
        var wait = Math.Max(1, (int)Math.Ceiling((until - DateTime.UtcNow).TotalMinutes));
        return Unauthorized(ApiResponse<object>.Fail("LOCKED", $"Too many failed attempts. Try again in {wait} minute(s)."));
    }

    private static (string secret, string qrUri, string[] backupCodes) BeginTotpSetup(AdminUser admin)
    {
        var secret      = TotpService.GenerateSecret();
        var qrUri       = TotpService.BuildQrUri(secret, admin.Email);
        var backupCodes = TotpService.GenerateBackupCodes(8);
        admin.TotpSecret      = secret;
        admin.TotpBackupCodes = JsonSerializer.Serialize(backupCodes.Select(TotpService.HashBackupCode).ToArray());
        admin.UpdatedAt       = DateTime.UtcNow;
        return (secret, qrUri, backupCodes);
    }

    /// <summary>
    /// Login / me payload. Permissions always cover EVERY module (true/false) so the sidebar filter works:
    /// Super Admin → all true; others → role permissions merged with per-user overrides, missing = false.
    /// </summary>
    private static object ProfilePayload(AdminUser admin, bool includeActive)
    {
        var isSuper = AdminPermissions.IsSuperAdmin(admin);
        var perms   = AdminPermissions.Effective(admin);
        var allowed = perms.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        if (includeActive)
            return new
            {
                admin.Id, admin.Name, admin.Email, admin.Role,
                RoleName     = admin.AdminRole?.Name ?? admin.Role,
                RoleId       = admin.AdminRoleId,
                IsSuperAdmin = isSuper,
                admin.IsActive, admin.IsTwoFactorEnabled,
                Permissions  = perms,
                Modules      = allowed
            };
        return new
        {
            admin.Id, admin.Name, admin.Email, admin.Role,
            RoleName     = admin.AdminRole?.Name ?? admin.Role,
            RoleId       = admin.AdminRoleId,
            IsSuperAdmin = isSuper,
            admin.IsTwoFactorEnabled,
            Permissions  = perms,
            Modules      = allowed
        };
    }


    private string GenerateJwt(AdminUser admin, string jti, int sessionId, DateTime expiry)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  admin.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier,    admin.Id.ToString()),
            new Claim(ClaimTypes.Name,              admin.Name),
            new Claim(ClaimTypes.Email,             admin.Email),
            new Claim(ClaimTypes.Role,              PolicyRole(admin.Role)),
            new Claim("admin_role",                 admin.Role),
            new Claim("admin_id",                   admin.Id.ToString()),
            new Claim("admin_name",                 admin.Name),
            new Claim("wsid",                       sessionId.ToString()),
            new Claim("tt",                         "access"),
            new Claim(JwtRegisteredClaimNames.Jti,  jti),
        };
        var jwt = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"], audience: _config["Jwt:Audience"],
            claims: claims, notBefore: DateTime.UtcNow, expires: expiry, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>
    /// Maps the stored role name (an AdminRole name such as "Super Admin", "Sales Exec", "HR Manager"
    /// or a custom role) onto the role claim the authorization policies understand
    /// ("Admin" = SuperAdmin policy; Manager/Accountant/Production/SalesExec/HRManager = AdminOnly).
    /// Without this, users created with a seeded/custom role got a claim no policy accepted.
    /// </summary>
    internal static string PolicyRole(string? roleName)
    {
        var compact = (roleName ?? "").Replace(" ", "").Trim();
        if (compact.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
            compact.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
            return "Admin";
        foreach (var known in new[] { "Manager", "Accountant", "Production", "SalesExec", "HRManager" })
            if (compact.Equals(known, StringComparison.OrdinalIgnoreCase)) return known;
        // custom roles: regular (non-super) admin access; module visibility is driven by permissions
        return "Manager";
    }

    internal static bool IsSuperAdminRole(string? roleName) => PolicyRole(roleName) == "Admin";

    /// <summary>Short-lived login-step token ("temp_2fa" = enter code, "temp_2fa_setup" = forced setup, "temp_pwchange" = first password). Rejected as a bearer token.</summary>
    private string GenerateTempToken(AdminUser admin, string purpose, int minutes)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim("admin_id",  admin.Id.ToString()),
            new Claim(purpose, "true"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var jwt = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"], audience: _config["Jwt:Audience"],
            claims: claims, notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private ClaimsPrincipal? ValidateTempToken(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
            var p   = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidAlgorithms          = new[] { SecurityAlgorithms.HmacSha256 },
                ValidIssuer              = _config["Jwt:Issuer"],
                ValidAudience            = _config["Jwt:Audience"],
                ValidateLifetime         = true,
                RequireExpirationTime    = true,
                ClockSkew                = TimeSpan.FromSeconds(30)
            }, out _);
            if (p.FindFirst(purpose)?.Value != "true") return null;
            var jti = p.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? p.FindFirst("jti")?.Value;
            if (jti == null || _cache.TryGetValue("temp-used:" + jti, out _)) return null;   // already used
            return p;
        }
        catch { return null; }
    }

    /// <summary>Login-step tokens are single use: mark as used after a successful step.</summary>
    private bool ConsumeTempToken(ClaimsPrincipal p)
    {
        var jti = p.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? p.FindFirst("jti")?.Value;
        if (jti == null) return false;
        lock (_cache)
        {
            if (_cache.TryGetValue("temp-used:" + jti, out _)) return false;
            _cache.Set("temp-used:" + jti, true, TimeSpan.FromMinutes(15));
        }
        return true;
    }

    private async Task WriteAuditLog(int adminId, string adminName, string action)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId    = adminId.ToString(),
            UserName  = adminName,
            Module    = "Auth",
            Action    = action,
            IpAddress = ClientIp.Get(HttpContext),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}

public record VerifyLoginRequest(string? TempToken, string? Code);
public record SetupLoginRequest(string? SetupToken);
public record EnableLoginRequest(string? SetupToken, string? Code);
public record FirstPasswordRequest(string? ChangeToken, string? NewPassword);
public record AdminRefreshRequest(string? RefreshToken);
