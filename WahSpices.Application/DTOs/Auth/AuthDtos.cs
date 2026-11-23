using System.ComponentModel.DataAnnotations;

namespace WahSpices.Application.DTOs.Auth;

// Validation happens in AuthService so every failure returns the standard { success:false, error:{code,message,field} } JSON.
public record RegisterRequest(string? Name, string? Email, string? Phone, string? Password);

public record LoginRequest(string? Email, string? Password);

public record SendOtpRequest(
    string? Phone,
    string Purpose = "login"   // login | register | forgot-password
);

public record VerifyOtpRequest(string? Phone, string? Otp);

public record VerifyEmailOtpRequest(string? Email, string? Otp);

public record ResendEmailOtpRequest(string? Email);

public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public record ForgotPasswordRequest(string? Email);

public record ResetPasswordRequest(string? Token, string? NewPassword);

/// <summary>Body form of the refresh call (non-browser clients); browsers send the httpOnly cookie instead.</summary>
public record RefreshTokenRequest(string? RefreshToken);

public record UpdateProfileRequest(
    string? Name,
    string? Email,
    string? Phone
);

public record AddAddressRequest(
    [Required] string Name,
    [Required] string Phone,
    [Required] string AddressLine,
    [Required] string City,
    [Required] string State,
    [Required] string Pincode,
    string? Landmark,
    string Type = "Home",
    bool IsDefault = false
);

// ── Responses ────────────────────────────────────────
public record RegisterPendingResponse(
    string Email,
    string MaskedEmail
);

public record AuthResponse(
    UserDto User,
    string Token,
    string RefreshToken,
    int ExpiresIn
);

public record UserDto(
    int Id,
    string Name,
    string Email,
    string Phone,
    string? AvatarUrl,
    int LoyaltyPoints,
    int TotalOrders,
    DateTime CreatedAt
);

public record AddressDto(
    int Id,
    string Name,
    string Phone,
    string AddressLine,
    string City,
    string State,
    string Pincode,
    string? Landmark,
    string Type,
    bool IsDefault
);
