using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.DTOs.Auth;
using WahSpices.Application.Interfaces.Services;
using System.Security.Claims;
using WahSpices.API.Security;
using WahSpices.Application.Common;

namespace WahSpices.API.Controllers.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>Register a new customer — sends OTP to email for verification</summary>
    [HttpPost("register")]
    [RequireHumanProof("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Verify email OTP after registration — returns JWT on success</summary>
    [HttpPost("verify-email")]
    [RequireHumanProof("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailOtpRequest request)
    {
        var result = await _authService.VerifyEmailOtpAsync(request);
        return result.Success ? Ok(WithRefreshCookie(result)) : BadRequest(result);
    }

    /// <summary>Resend email verification OTP</summary>
    [HttpPost("resend-email-otp")]
    [RequireHumanProof("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendEmailOtp([FromBody] ResendEmailOtpRequest request)
    {
        var result = await _authService.ResendEmailOtpAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Login with email and password</summary>
    [HttpPost("login")]
    [RequireHumanProof("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        if (result.Success) return Ok(WithRefreshCookie(result));
        return result.Error?.Code == "EMAIL_NOT_VERIFIED" ? StatusCode(403, result) : Unauthorized(result);
    }

    /// <summary>Send OTP to mobile number</summary>
    [HttpPost("send-otp")]
    [RequireHumanProof("auth")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest request)
    {
        var result = await _authService.SendOtpAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Verify OTP and login / register</summary>
    [HttpPost("verify-otp")]
    [RequireHumanProof("auth")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var result = await _authService.VerifyOtpAsync(request);
        return result.Success ? Ok(WithRefreshCookie(result)) : BadRequest(result);
    }

    /// <summary>Google OAuth login</summary>
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        var result = await _authService.GoogleLoginAsync(request.GoogleToken);
        return result.Success ? Ok(WithRefreshCookie(result)) : BadRequest(result);
    }

    /// <summary>Send password reset email</summary>
    [HttpPost("forgot-password")]
    [RequireHumanProof("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var result = await _authService.ForgotPasswordAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Reset password with token from email</summary>
    [HttpPost("reset-password")]
    [RequireHumanProof("auth")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Get current user profile</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.GetProfileAsync(userId);
        return Ok(result);
    }

    /// <summary>Update user profile</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.UpdateProfileAsync(userId, request);
        return Ok(result);
    }

    /// <summary>Change password</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.ChangePasswordAsync(userId, request);
        return result.Success ? Ok(WithRefreshCookie(result)) : BadRequest(result);
    }

    /// <summary>Exchange the refresh token (httpOnly cookie, or body for non-browser clients) for a new access token.
    /// The refresh token is rotated on every call; replaying an old one signs the account out everywhere.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request)
    {
        if (!RefreshCookies.OriginAllowed(HttpContext))
            return StatusCode(403, ApiResponse<object>.Fail("FORBIDDEN_ORIGIN", "Request origin not allowed."));
        var token = RefreshCookies.Read(HttpContext, RefreshCookies.Customer, request?.RefreshToken);
        if (string.IsNullOrEmpty(token))
            return Unauthorized(ApiResponse<object>.Fail("NO_SESSION", "Please sign in."));
        var result = await _authService.RefreshTokenAsync(token);
        if (!result.Success)
        {
            RefreshCookies.Clear(HttpContext, RefreshCookies.Customer);
            return Unauthorized(result);
        }
        return Ok(WithRefreshCookie(result));
    }

    /// <summary>Logout current session (this device)</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.LogoutAsync(userId, User.FindFirstValue("wsid"), allDevices: false);
        RefreshCookies.Clear(HttpContext, RefreshCookies.Customer);
        return Ok(result);
    }

    /// <summary>Sign out on every device (bumps TokenVersion — all access + refresh tokens die immediately)</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.LogoutAsync(userId, null, allDevices: true);
        RefreshCookies.Clear(HttpContext, RefreshCookies.Customer);
        return Ok(result);
    }

    /// <summary>Moves the refresh token out of the JSON body into the httpOnly cookie.</summary>
    private ApiResponse<AuthResponse> WithRefreshCookie(ApiResponse<AuthResponse> result)
    {
        if (!result.Success || result.Data == null) return result;
        var refresh = result.Data.RefreshToken;
        if (!string.IsNullOrEmpty(refresh))
            RefreshCookies.Set(HttpContext, RefreshCookies.Customer, refresh, DateTimeOffset.UtcNow.AddDays(30));
        if (!RefreshCookies.ReturnInBody(HttpContext))
            result.Data = result.Data with { RefreshToken = "" };
        return result;
    }

    // ── Addresses ────────────────────────────────────────────────

    /// <summary>Get all saved addresses</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpGet("me/addresses")]
    public async Task<IActionResult> GetAddresses()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.GetAddressesAsync(userId);
        return Ok(result);
    }

    /// <summary>Add a new delivery address</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPost("me/addresses")]
    public async Task<IActionResult> AddAddress([FromBody] AddAddressRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.AddAddressAsync(userId, request);
        return StatusCode(201, result);
    }

    /// <summary>Update an address</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPut("me/addresses/{addressId:int}")]
    public async Task<IActionResult> UpdateAddress(int addressId, [FromBody] AddAddressRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.UpdateAddressAsync(userId, addressId, request);
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>Delete an address</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpDelete("me/addresses/{addressId:int}")]
    public async Task<IActionResult> DeleteAddress(int addressId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.DeleteAddressAsync(userId, addressId);
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }
}

public record GoogleLoginRequest(string GoogleToken);
