using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Auth;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Security;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// Customer authentication. Security properties:
///  • uniform answers on register / login / forgot / resend (no account enumeration), dummy hashing for unknown e-mails;
///  • escalating lockout after 5 failed logins (DB for real accounts, memory twin for unknown e-mails);
///  • OTPs: 6 random digits, HMAC-hashed at rest, 5-minute expiry, 5 attempts, 60 s resend cooldown, 10/day per identifier;
///  • short-lived access tokens (15 min) carrying TokenVersion ("tv") + session key ("sid"), validated per request;
///  • refresh tokens rotated on every use, stored only as SHA-256, reuse of a rotated token revokes every session;
///  • password reset tokens stored as SHA-256, 30-minute expiry, single use.
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;
    private readonly IEmailComposer _emails;
    private readonly IMemoryCache _cache;

    private const string EmailVerifyPurpose = "email-verify";
    private const int OtpMinutes = 5;
    private const int OtpCooldownSeconds = 60;
    private const int OtpDailyCap = 10;
    private const string GenericLoginError = "Invalid email or password";
    private const string GenericSentMessage = "If this email can be used, we've sent a 6-digit code to it.";

    public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger, IEmailComposer emails, IMemoryCache cache)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _emails = emails;
        _cache = cache;
    }

    // ── Register ──────────────────────────────────────────────────────────
    public async Task<ApiResponse<RegisterPendingResponse>> RegisterAsync(RegisterRequest request)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var phone = (request.Phone ?? "").Trim();
        var name  = (request.Name ?? "").Trim();

        if (name.Length is < 2 or > 128)
            return ApiResponse<RegisterPendingResponse>.Fail("VALIDATION", "Please enter your name (2–128 characters).", "name");
        if (!IsValidEmail(email))
            return ApiResponse<RegisterPendingResponse>.Fail("VALIDATION", "Please enter a valid email address.", "email");
        if (!IsValidPhone(phone))
            return ApiResponse<RegisterPendingResponse>.Fail("VALIDATION", "Enter a valid 10-digit mobile number.", "phone");
        var pwError = PasswordPolicy.Validate(request.Password, email, name);
        if (pwError != null)
            return ApiResponse<RegisterPendingResponse>.Fail("WEAK_PASSWORD", pwError, "password");

        var pending = ApiResponse<RegisterPendingResponse>.Ok(new RegisterPendingResponse(email, MaskEmail(email)), "OTP_SENT");
        var passwordHash = HashPassword(request.Password!);   // computed on every path → uniform timing

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);
        if (existing != null)
        {
            if (!existing.IsEmailVerified)
            {
                // Unverified account: whoever proves ownership of the mailbox (by entering the code) gets it,
                // with the details from the latest sign-up attempt.
                if (OtpGate(email, out _))
                {
                    existing.Name = name;
                    existing.PasswordHash = passwordHash;
                    if (!await _db.Users.AnyAsync(u => u.Phone == phone && u.Id != existing.Id)) existing.Phone = phone;
                    await _db.SaveChangesAsync();
                    await SendEmailOtpAsync(email, existing.Name, EmailTemplates.OtpContext.Register);
                }
            }
            else if (NoticeGate("exists:" + email))
            {
                await _emails.QueueAsync(email, existing.Name, "auth-account-exists", b => EmailTemplates.AccountExists(b, existing.Name));
            }
            return pending;   // same answer whether or not the account exists
        }

        if (await _db.Users.AnyAsync(u => u.Phone == phone))
        {
            // The mobile number belongs to another account: tell the mailbox owner, answer uniformly.
            if (NoticeGate("phone:" + email))
                await _emails.QueueAsync(email, name, "auth-mobile-in-use", b => EmailTemplates.MobileInUse(b, name));
            return pending;
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Phone = phone,
            PasswordHash = passwordHash,
            Role = UserRole.Customer,
            IsActive = false,         // not active until email verified
            IsEmailVerified = false,
            CreatedBy = "self"
        };

        _db.Users.Add(user);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Registration race for {Email}", email);
            return pending;
        }

        if (OtpGate(email, out _)) await SendEmailOtpAsync(email, user.Name, EmailTemplates.OtpContext.Register);

        _logger.LogInformation("New user registered (pending verification): {Email}", email);
        return pending;
    }

    // ── Verify Email OTP ──────────────────────────────────────────────────
    public async Task<ApiResponse<AuthResponse>> VerifyEmailOtpAsync(VerifyEmailOtpRequest request)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var check = await ConsumeOtpAsync(email, EmailVerifyPurpose, request.Otp);
        if (check != null) return ApiResponse<AuthResponse>.Fail("INVALID_OTP", check);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);
        if (user == null)
            return ApiResponse<AuthResponse>.Fail("INVALID_OTP", "Invalid or expired code. Please request a new one.");
        if (IsBlocked(user) || (user.IsEmailVerified && !user.IsActive))
            return ApiResponse<AuthResponse>.Fail("ACCOUNT_DISABLED", "Your account has been disabled");

        var firstVerification = !user.IsEmailVerified;
        user.IsEmailVerified = true;
        user.IsActive = true;
        user.LastLoginAt = DateTime.UtcNow;
        ResetLockout(user);

        var auth = await IssueSessionAsync(user);
        _logger.LogInformation("Email verified and account activated: {Email}", email);
        if (firstVerification)
            await _emails.QueueAsync(user.Email, user.Name, "auth-welcome", b => EmailTemplates.Welcome(b, user.Name));
        return ApiResponse<AuthResponse>.Ok(auth, "Email verified! Welcome to Wah Spices.");
    }

    // ── Resend Email OTP ──────────────────────────────────────────────────
    public async Task<ApiResponse<bool>> ResendEmailOtpAsync(ResendEmailOtpRequest request)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return ApiResponse<bool>.Fail("VALIDATION", "Please enter a valid email address.", "email");

        // Cooldown + daily cap are tracked for every address (known or not) so the answer never reveals existence.
        if (!OtpGate(email, out var reason))
            return ApiResponse<bool>.Fail("OTP_THROTTLED", reason!);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);
        if (user != null && !user.IsEmailVerified)
            await SendEmailOtpAsync(email, user.Name, EmailTemplates.OtpContext.Resend);

        return ApiResponse<bool>.Ok(true, GenericSentMessage);
    }

    // ── Login ─────────────────────────────────────────────────────────────
    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var password = request.Password ?? "";

        var user = email.Length == 0 ? null : await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);

        if (user == null)
        {
            // Unknown e-mail: same work, same answers, same lockout schedule (kept in memory).
            var key = "login-fail:" + email;
            var state = _cache.Get<(int fails, DateTime? until)>(key);
            if (state.until > DateTime.UtcNow) return LockedResponse(state.until.Value);
            VerifyPassword(password, DummyHash);
            state.fails++;
            var mins = LoginLockout.LockMinutes(state.fails);
            state.until = mins > 0 ? DateTime.UtcNow.AddMinutes(mins) : null;
            _cache.Set(key, state, TimeSpan.FromHours(24));
            return mins > 0 ? LockedResponse(state.until!.Value) : ApiResponse<AuthResponse>.Fail("INVALID_CREDENTIALS", GenericLoginError);
        }

        if (user.LockedUntil > DateTime.UtcNow)
            return LockedResponse(user.LockedUntil!.Value);

        if (!VerifyPassword(password, user.PasswordHash))
        {
            if (IsBlocked(user)) return ApiResponse<AuthResponse>.Fail("INVALID_CREDENTIALS", GenericLoginError);   // keep the admin block intact
            user.FailedLoginAttempts++;
            var mins = LoginLockout.LockMinutes(user.FailedLoginAttempts);
            if (mins > 0)
            {
                user.IsLocked = true;
                user.LockedUntil = DateTime.UtcNow.AddMinutes(mins);
                _logger.LogWarning("Customer {UserId} locked for {Min} min after {N} failed logins", user.Id, mins, user.FailedLoginAttempts);
            }
            await _db.SaveChangesAsync();
            return mins > 0 ? LockedResponse(user.LockedUntil!.Value) : ApiResponse<AuthResponse>.Fail("INVALID_CREDENTIALS", GenericLoginError);
        }

        // Correct password from here on — it's safe to be specific.
        if (IsBlocked(user))
            return ApiResponse<AuthResponse>.Fail("ACCOUNT_DISABLED", "Your account has been disabled");
        if (!user.IsEmailVerified && !user.IsActive)
        {
            if (OtpGate(email, out _)) await SendEmailOtpAsync(email, user.Name, EmailTemplates.OtpContext.Login);
            return ApiResponse<AuthResponse>.Fail("EMAIL_NOT_VERIFIED",
                "Your email isn't verified yet — we've sent you a 6-digit code. Enter it to finish signing in.", "email");
        }
        if (!user.IsActive)
            return ApiResponse<AuthResponse>.Fail("ACCOUNT_DISABLED", "Your account has been disabled");

        ResetLockout(user);
        user.LastLoginAt = DateTime.UtcNow;
        var auth = await IssueSessionAsync(user);
        return ApiResponse<AuthResponse>.Ok(auth, "Login successful");
    }

    // ── OTP (mobile) ──────────────────────────────────────────────────────
    public async Task<ApiResponse<bool>> SendOtpAsync(SendOtpRequest request)
    {
        var phone = (request.Phone ?? "").Trim();
        if (!IsValidPhone(phone))
            return ApiResponse<bool>.Fail("VALIDATION", "Enter a valid 10-digit mobile number.", "phone");
        var purpose = request.Purpose is "login" or "register" or "forgot-password" ? request.Purpose : "login";

        if (!OtpGate(phone, out var reason))
            return ApiResponse<bool>.Fail("OTP_THROTTLED", reason!);

        var otp = await CreateOtpAsync(phone, purpose);

        // TODO: integrate SMS provider (MSG91). Never log codes outside Development.
        if (IsDevelopment) _logger.LogInformation("[DEV ONLY] OTP for {Phone}: {Otp}", phone, otp);

        return ApiResponse<bool>.Ok(true, "OTP sent successfully");
    }

    public async Task<ApiResponse<AuthResponse>> VerifyOtpAsync(VerifyOtpRequest request)
    {
        var phone = (request.Phone ?? "").Trim();
        if (!IsValidPhone(phone))
            return ApiResponse<AuthResponse>.Fail("INVALID_OTP", "Invalid or expired code. Please request a new one.");

        var check = await ConsumeOtpAsync(phone, null, request.Otp);
        if (check != null) return ApiResponse<AuthResponse>.Fail("INVALID_OTP", check);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == phone && !u.IsDeleted);
        if (user == null)
        {
            user = new User
            {
                Name = "User",
                Email = $"{phone}@wahspices.com",
                Phone = phone,
                Role = UserRole.Customer,
                IsActive = true,
                IsPhoneVerified = true,
                CreatedBy = "otp"
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }
        else
        {
            if (IsBlocked(user) || (!user.IsActive && user.IsEmailVerified))
                return ApiResponse<AuthResponse>.Fail("ACCOUNT_DISABLED", "Your account has been disabled");
            user.IsPhoneVerified = true;
            user.LastLoginAt = DateTime.UtcNow;
            ResetLockout(user);
        }

        var auth = await IssueSessionAsync(user);
        return ApiResponse<AuthResponse>.Ok(auth, "OTP verified");
    }

    // ── Google OAuth ──────────────────────────────────────────────────────
    public async Task<ApiResponse<AuthResponse>> GoogleLoginAsync(string googleToken)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(googleToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _config["Google:ClientId"] }
                });
        }
        catch (Exception ex) when (ex is InvalidJwtException or ArgumentException)
        {
            _logger.LogWarning("Google token validation failed: {Msg}", ex.Message);
            return ApiResponse<AuthResponse>.Fail("INVALID_GOOGLE_TOKEN", "Google sign-in failed. Please try again.");
        }

        if (!payload.EmailVerified)
            return ApiResponse<AuthResponse>.Fail("INVALID_GOOGLE_TOKEN", "Your Google email address isn't verified.");

        var email = payload.Email.Trim().ToLowerInvariant();
        var googleId = payload.Subject;

        // Find by Google ID first, then fall back to email
        var user = await _db.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId && !u.IsDeleted)
                ?? await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);

        if (user == null)
        {
            user = new User
            {
                Name = payload.Name ?? email,
                Email = email,
                Phone = string.Empty,
                GoogleId = googleId,
                AvatarUrl = payload.Picture,
                Role = UserRole.Customer,
                IsActive = true,
                IsEmailVerified = true,
                CreatedBy = "google"
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            var welcomeName = user.Name;
            await _emails.QueueAsync(email, welcomeName, "auth-welcome", b => EmailTemplates.Welcome(b, welcomeName));
        }
        else
        {
            if (IsBlocked(user) || (!user.IsActive && user.IsEmailVerified))
                return ApiResponse<AuthResponse>.Fail("ACCOUNT_DISABLED", "Your account has been disabled");
            if (user.GoogleId == null) user.GoogleId = googleId;
            if (user.AvatarUrl == null && payload.Picture != null) user.AvatarUrl = payload.Picture;
            user.IsEmailVerified = true;
            user.IsActive = true;
            user.LastLoginAt = DateTime.UtcNow;
            ResetLockout(user);
        }

        var auth = await IssueSessionAsync(user);
        _logger.LogInformation("Google login: {UserId}", user.Id);
        return ApiResponse<AuthResponse>.Ok(auth, "Google login successful");
    }

    // ── Password reset ────────────────────────────────────────────────────
    public async Task<ApiResponse<bool>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        const string uniform = "If an account exists for this email, we've sent a password reset link to it.";
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return ApiResponse<bool>.Fail("VALIDATION", "Please enter a valid email address.", "email");
        if (!NoticeGate("reset:" + email))
            return ApiResponse<bool>.Ok(true, uniform);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);
        if (user == null || (!user.IsActive && user.IsEmailVerified)) return ApiResponse<bool>.Ok(true, uniform);

        var raw = SecretHash.RandomToken(32);
        user.PasswordResetToken = SecretHash.Sha256Hex(raw);     // only the hash is stored
        user.PasswordResetExpiry = DateTime.UtcNow.AddMinutes(30);
        await _db.SaveChangesAsync();

        var baseUrl = (_config["App:FrontendUrl"] ?? "https://wahspices.com").TrimEnd('/');
        var link = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(raw)}";
        var resetName = user.Name;
        await _emails.QueueAsync(email, resetName, "auth-password-reset", b => EmailTemplates.PasswordReset(b, resetName, link, 30));
        return ApiResponse<bool>.Ok(true, uniform);
    }

    public async Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var token = (request.Token ?? "").Trim();
        if (token.Length is < 20 or > 200)
            return ApiResponse<bool>.Fail("INVALID_TOKEN", "Reset link is invalid or has expired");
        var hash = SecretHash.Sha256Hex(token);
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.PasswordResetToken == hash && u.PasswordResetExpiry > DateTime.UtcNow && !u.IsDeleted);

        if (user == null)
            return ApiResponse<bool>.Fail("INVALID_TOKEN", "Reset link is invalid or has expired");

        var pwError = PasswordPolicy.Validate(request.NewPassword, user.Email, user.Name);
        if (pwError != null) return ApiResponse<bool>.Fail("WEAK_PASSWORD", pwError, "newPassword");

        using (await KeyedLock.AcquireAsync("user:" + user.Id))
        {
            user.PasswordHash = HashPassword(request.NewPassword!);
            user.PasswordResetToken = null;
            user.PasswordResetExpiry = null;
            user.RefreshToken = null;          // every refresh session ends
            user.RefreshTokenExpiry = null;
            user.TokenVersion++;               // every access token dies
            ResetLockout(user);
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var changedAt = DateTime.UtcNow;
        await _emails.QueueAsync(user.Email, user.Name, "auth-password-changed", b => EmailTemplates.PasswordChanged(b, user.Name, changedAt, viaReset: true));
        return ApiResponse<bool>.Ok(true, "Password reset successfully. Please sign in with your new password.");
    }

    // ── Profile ───────────────────────────────────────────────────────────
    public async Task<ApiResponse<UserDto>> GetProfileAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Orders)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);

        if (user == null) return ApiResponse<UserDto>.Fail("NOT_FOUND", "User not found");
        return ApiResponse<UserDto>.Ok(MapUserDto(user));
    }

    public async Task<ApiResponse<UserDto>> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.IsDeleted)
            return ApiResponse<UserDto>.Fail("NOT_FOUND", "User not found");

        if (request.Name != null) user.Name = request.Name.Trim();
        if (request.Phone != null) user.Phone = request.Phone.Trim();
        var newEmail = request.Email?.Trim().ToLowerInvariant();
        if (newEmail != null && newEmail != user.Email)
        {
            if (await _db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId && !u.IsDeleted))
                return ApiResponse<UserDto>.Fail("EMAIL_TAKEN", "Email already in use", "email");
            user.Email = request.Email.Trim().ToLower();
        }
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ApiResponse<UserDto>.Ok(MapUserDto(user), "Profile updated");
    }

    public async Task<ApiResponse<AuthResponse>> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.IsDeleted)
            return ApiResponse<AuthResponse>.Fail("NOT_FOUND", "User not found");

        if (!VerifyPassword(request.CurrentPassword ?? "", user.PasswordHash))
            return ApiResponse<AuthResponse>.Fail("WRONG_PASSWORD", "Current password is incorrect", "currentPassword");

        var pwError = PasswordPolicy.Validate(request.NewPassword, user.Email, user.Name);
        if (pwError != null) return ApiResponse<AuthResponse>.Fail("WEAK_PASSWORD", pwError, "newPassword");

        using (await KeyedLock.AcquireAsync("user:" + user.Id))
        {
            user.PasswordHash = HashPassword(request.NewPassword!);
            user.UpdatedAt = DateTime.UtcNow;
            user.TokenVersion++;          // signs out every other device
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
            await _db.SaveChangesAsync();
        }
        var changedAt = DateTime.UtcNow;
        await _emails.QueueAsync(user.Email, user.Name, "auth-password-changed", b => EmailTemplates.PasswordChanged(b, user.Name, changedAt, viaReset: false));
        // …and keeps THIS device signed in with a fresh session.
        var auth = await IssueSessionAsync(user);
        return ApiResponse<AuthResponse>.Ok(auth, "Password changed successfully. Other devices have been signed out.");
    }

    // ── Refresh / Logout ──────────────────────────────────────────────────
    public async Task<ApiResponse<AuthResponse>> RefreshTokenAsync(string refreshToken)
    {
        var invalid = ApiResponse<AuthResponse>.Fail("INVALID_TOKEN", "Your session has expired. Please sign in again.");
        if (!RefreshSessions.TryParseToken(refreshToken, out var owner, out var key) || !int.TryParse(owner, out var userId))
            return invalid;

        using var _ = await KeyedLock.AcquireAsync("user:" + userId);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
        if (user == null || !user.IsActive) return invalid;

        var sessions = RefreshSessions.Parse(user.RefreshToken);
        switch (RefreshSessions.Check(sessions, key, refreshToken, out var session))
        {
            case RefreshCheck.Valid:
                var next = RefreshSessions.Rotate(session!, owner, RefreshSliding, RefreshAbsolute);
                user.RefreshToken = RefreshSessions.Serialize(sessions);
                user.RefreshTokenExpiry = DateTimeOffset.FromUnixTimeSeconds(sessions.Max(s => s.ExpiresAt)).UtcDateTime;
                await _db.SaveChangesAsync();
                return ApiResponse<AuthResponse>.Ok(BuildAuthResponse(user, GenerateAccessToken(user, key), next));

            case RefreshCheck.Grace:
                // Another tab rotated this token a moment ago: hand out an access token, keep the newer cookie.
                return ApiResponse<AuthResponse>.Ok(BuildAuthResponse(user, GenerateAccessToken(user, key), ""));

            case RefreshCheck.Reused:
                // A rotated-out token came back → it was stolen (or replayed). Kill every session + access token.
                _logger.LogWarning("Refresh token REUSE detected for user {UserId} — all sessions revoked", user.Id);
                user.RefreshToken = null;
                user.RefreshTokenExpiry = null;
                user.TokenVersion++;
                await _db.SaveChangesAsync();
                return ApiResponse<AuthResponse>.Fail("REFRESH_REUSED", "For your security you've been signed out everywhere. Please sign in again.");

            default:
                return invalid;
        }
    }

    public async Task<ApiResponse<bool>> LogoutAsync(int userId, string? sessionKey, bool allDevices)
    {
        using var _ = await KeyedLock.AcquireAsync("user:" + userId);
        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            if (allDevices || string.IsNullOrEmpty(sessionKey))
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiry = null;
                if (allDevices) user.TokenVersion++;
            }
            else
            {
                var sessions = RefreshSessions.Parse(user.RefreshToken);
                sessions.RemoveAll(s => s.Key == sessionKey);
                user.RefreshToken = RefreshSessions.Serialize(sessions);
            }
            await _db.SaveChangesAsync();
        }
        return ApiResponse<bool>.Ok(true, allDevices ? "Signed out on all devices" : "Logged out successfully");
    }

    // ── Addresses ─────────────────────────────────────────────────────────
    public async Task<ApiResponse<List<AddressDto>>> GetAddressesAsync(int userId)
    {
        var addresses = await _db.Addresses
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();

        return ApiResponse<List<AddressDto>>.Ok(addresses.Select(MapAddressDto).ToList());
    }

    public async Task<ApiResponse<AddressDto>> AddAddressAsync(int userId, AddAddressRequest request)
    {
        if (request.IsDefault)
            await ClearDefaultAddresses(userId);

        var address = new Address
        {
            UserId = userId,
            Name = request.Name,
            Phone = request.Phone,
            AddressLine = request.AddressLine,
            City = request.City,
            State = request.State,
            Pincode = request.Pincode,
            Landmark = request.Landmark,
            Type = Enum.TryParse<AddressType>(request.Type, true, out var t) ? t : AddressType.Home,
            IsDefault = request.IsDefault,
            CreatedBy = userId.ToString()
        };

        _db.Addresses.Add(address);
        await _db.SaveChangesAsync();
        return ApiResponse<AddressDto>.Ok(MapAddressDto(address), "Address added");
    }

    public async Task<ApiResponse<AddressDto>> UpdateAddressAsync(int userId, int addressId, AddAddressRequest request)
    {
        var address = await _db.Addresses.FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId && !a.IsDeleted);
        if (address == null) return ApiResponse<AddressDto>.Fail("NOT_FOUND", "Address not found");

        if (request.IsDefault) await ClearDefaultAddresses(userId);

        address.Name = request.Name;
        address.Phone = request.Phone;
        address.AddressLine = request.AddressLine;
        address.City = request.City;
        address.State = request.State;
        address.Pincode = request.Pincode;
        address.Landmark = request.Landmark;
        address.Type = Enum.TryParse<AddressType>(request.Type, true, out var t) ? t : AddressType.Home;
        address.IsDefault = request.IsDefault;
        address.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ApiResponse<AddressDto>.Ok(MapAddressDto(address), "Address updated");
    }

    public async Task<ApiResponse<bool>> DeleteAddressAsync(int userId, int addressId)
    {
        var address = await _db.Addresses.FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId && !a.IsDeleted);
        if (address == null) return ApiResponse<bool>.Fail("NOT_FOUND", "Address not found");

        address.IsDeleted = true;
        address.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ApiResponse<bool>.Ok(true, "Address deleted");
    }


    // ── Helpers ───────────────────────────────────────────────────────────
    private int AccessMinutes => Math.Clamp(_config.GetValue<int>("Jwt:AccessTokenMinutes", 15), 1, 60);
    private TimeSpan RefreshSliding => TimeSpan.FromDays(_config.GetValue<int>("Jwt:RefreshExpiryDays", 30));
    private TimeSpan RefreshAbsolute => TimeSpan.FromDays(Math.Max(_config.GetValue<int>("Jwt:RefreshAbsoluteDays", 90), 1));
    private bool IsDevelopment => string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
        "Development", StringComparison.OrdinalIgnoreCase);

    // A valid PBKDF2 hash of a random string: unknown e-mails cost the same CPU as real ones.
    private static readonly string DummyHash = HashPassword(Guid.NewGuid().ToString("N"));

    private static bool IsValidEmail(string email) =>
        email.Length is > 3 and <= 256 && System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    private static bool IsValidPhone(string phone) =>
        System.Text.RegularExpressions.Regex.IsMatch(phone, @"^[6-9]\d{9}$");

    private static ApiResponse<AuthResponse> LockedResponse(DateTime until)
    {
        var mins = Math.Max(1, (int)Math.Ceiling((until - DateTime.UtcNow).TotalMinutes));
        return ApiResponse<AuthResponse>.Fail("ACCOUNT_LOCKED", $"Too many failed attempts. Please try again in {mins} minute(s).");
    }

    /// <summary>Blocked by an admin (Website Users → Block): inactive + locked with no end date.</summary>
    private static bool IsBlocked(User user) => !user.IsActive && user.IsLocked && user.LockedUntil == null;

    private static void ResetLockout(User user)
    {
        user.FailedLoginAttempts = 0;
        user.IsLocked = false;
        user.LockedUntil = null;
    }

    /// <summary>Creates a refresh session + access token for the user and saves.</summary>
    private async Task<AuthResponse> IssueSessionAsync(User user)
    {
        using var _ = await KeyedLock.AcquireAsync("user:" + user.Id);
        if (user.Id == 0) await _db.SaveChangesAsync();
        var key = SecretHash.RandomToken(12);
        var sessions = RefreshSessions.Parse(user.RefreshToken);
        var refresh = RefreshSessions.Create(sessions, user.Id.ToString(), key, RefreshSliding);
        user.RefreshToken = RefreshSessions.Serialize(sessions);
        user.RefreshTokenExpiry = DateTime.UtcNow.Add(RefreshSliding);
        await _db.SaveChangesAsync();
        return BuildAuthResponse(user, GenerateAccessToken(user, key), refresh);
    }

    private string GenerateAccessToken(User user, string sessionKey)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tv", user.TokenVersion.ToString()),
            new Claim("wsid", sessionKey),
            new Claim("tt", "access"),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(AccessMinutes),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── OTP plumbing ──────────────────────────────────────────────────────

    /// <summary>
    /// Resend cooldown (60 s) + daily cap (10 / 24 h) per phone/e-mail. Tracked in memory for every identifier
    /// (so unknown addresses behave exactly like known ones) and double-checked against the OTP table.
    /// Records the attempt when allowed.
    /// </summary>
    private bool OtpGate(string identifier, out string? reason)
    {
        reason = null;
        var id = identifier.ToLowerInvariant();
        var now = DateTime.UtcNow;
        var lastKey = "otp-last:" + id;
        var dayKey = "otp-day:" + id;

        var dbSince = now.AddHours(-24);
        var dbRecent = _db.OtpRecords.AsNoTracking().Where(o => o.Phone == id && o.CreatedAt > dbSince)
            .Select(o => o.CreatedAt).ToList();
        var last = new[] { _cache.Get<DateTime?>(lastKey), dbRecent.Count > 0 ? dbRecent.Max() : (DateTime?)null }.Max();
        var count = Math.Max(_cache.Get<int>(dayKey), dbRecent.Count);

        if (last.HasValue && (now - last.Value).TotalSeconds < OtpCooldownSeconds)
        {
            var wait = OtpCooldownSeconds - (int)(now - last.Value).TotalSeconds;
            reason = $"Please wait {Math.Max(wait, 1)} seconds before requesting another code.";
            return false;
        }
        if (count >= OtpDailyCap)
        {
            reason = "Too many codes requested today. Please try again tomorrow.";
            return false;
        }
        _cache.Set(lastKey, (DateTime?)now, TimeSpan.FromMinutes(5));
        var dayCount = _cache.Get<int>(dayKey) + 1;
        _cache.Set(dayKey, dayCount, TimeSpan.FromHours(24));
        return true;
    }

    /// <summary>Simple per-key limiter for notification e-mails (reset link / "account exists"): 1 per 2 min, 5 per day.</summary>
    private bool NoticeGate(string key)
    {
        var now = DateTime.UtcNow;
        var last = _cache.Get<DateTime?>("notice-last:" + key);
        var day = _cache.Get<int>("notice-day:" + key);
        if ((last.HasValue && (now - last.Value).TotalSeconds < 120) || day >= 5) return false;
        _cache.Set("notice-last:" + key, (DateTime?)now, TimeSpan.FromMinutes(5));
        _cache.Set("notice-day:" + key, day + 1, TimeSpan.FromHours(24));
        return true;
    }

    private async Task<string> CreateOtpAsync(string identifier, string purpose)
    {
        var id = identifier.ToLowerInvariant();
        var otp = OtpProtector.NewCode();

        var old = await _db.OtpRecords.Where(o => o.Phone == id && !o.IsUsed).ToListAsync();
        old.ForEach(o => o.IsUsed = true);

        _db.OtpRecords.Add(new OtpRecord
        {
            Phone = id,
            Otp = OtpProtector.Protect(_config, id, purpose, otp),   // HMAC only — never the code itself
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpMinutes),
            CreatedBy = "system"
        });
        await _db.SaveChangesAsync();
        return otp;
    }

    /// <summary>Checks a code against the newest live OTP for the identifier. Returns null on success (code consumed), else a message.</summary>
    private async Task<string?> ConsumeOtpAsync(string identifier, string? purpose, string? code)
    {
        const string invalid = "Invalid or expired code. Please request a new one.";
        var id = identifier.ToLowerInvariant();
        code = (code ?? "").Trim();

        var record = await _db.OtpRecords
            .Where(o => o.Phone == id && !o.IsUsed && (purpose == null || o.Purpose == purpose))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (record == null || record.ExpiresAt < DateTime.UtcNow || !OtpProtector.IsProtected(record.Otp))
            return invalid;

        var attempts = OtpProtector.Attempts(record.Otp);
        if (attempts >= OtpProtector.MaxAttempts)
        {
            record.IsUsed = true;
            await _db.SaveChangesAsync();
            return "Too many incorrect attempts. Please request a new code.";
        }

        if (code.Length != 6 || !OtpProtector.Matches(_config, record.Otp, id, record.Purpose, code))
        {
            attempts++;
            record.Otp = OtpProtector.WithAttempts(record.Otp, attempts);
            if (attempts >= OtpProtector.MaxAttempts) record.IsUsed = true;
            await _db.SaveChangesAsync();
            var left = OtpProtector.MaxAttempts - attempts;
            return left > 0 ? $"Incorrect code. {left} attempt(s) left." : "Too many incorrect attempts. Please request a new code.";
        }

        record.IsUsed = true;
        await _db.SaveChangesAsync();
        return null;
    }

    private async Task SendEmailOtpAsync(string email, string name, EmailTemplates.OtpContext context)
    {
        var otp = await CreateOtpAsync(email, EmailVerifyPurpose);
        // queued: the request never waits on SMTP; the code itself is never logged
        await _emails.QueueAsync(email, name, "auth-verification-code", b => EmailTemplates.VerificationCode(b, name, otp, OtpMinutes, context));
    }

    private AuthResponse BuildAuthResponse(User user, string token, string refresh) =>
        new(MapUserDto(user), token, refresh, AccessMinutes * 60);

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        var combined = new byte[48];
        Buffer.BlockCopy(salt, 0, combined, 0, 16);
        Buffer.BlockCopy(hash, 0, combined, 16, 32);
        return Convert.ToBase64String(combined);
    }

    private static bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try
        {
            var combined = Convert.FromBase64String(hash);
            if (combined.Length != 48) return false;
            var salt = combined[..16];
            var stored = combined[16..];
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            var computed = pbkdf2.GetBytes(32);
            return CryptographicOperations.FixedTimeEquals(stored, computed);
        }
        catch { return false; }
    }



    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        var local = email[..at];
        var masked = local[0] + new string('*', Math.Min(local.Length - 1, 4)) + email[at..];
        return masked;
    }

    private static UserDto MapUserDto(User user) =>
        new(user.Id, user.Name, user.Email, user.Phone, user.AvatarUrl,
            user.LoyaltyPoints, user.Orders?.Count ?? 0, user.CreatedAt);

    private static AddressDto MapAddressDto(Address a) =>
        new(a.Id, a.Name, a.Phone, a.AddressLine, a.City, a.State, a.Pincode,
            a.Landmark, a.Type.ToString(), a.IsDefault);

    private async Task ClearDefaultAddresses(int userId)
    {
        var defaults = await _db.Addresses
            .Where(a => a.UserId == userId && a.IsDefault && !a.IsDeleted)
            .ToListAsync();
        defaults.ForEach(a => a.IsDefault = false);
    }
}
