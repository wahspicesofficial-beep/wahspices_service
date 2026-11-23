using WahSpices.Application.DTOs.Auth;
using WahSpices.Application.Common;

namespace WahSpices.Application.Interfaces.Services;

public interface IAuthService
{
    Task<ApiResponse<RegisterPendingResponse>> RegisterAsync(RegisterRequest request);
    Task<ApiResponse<AuthResponse>> VerifyEmailOtpAsync(VerifyEmailOtpRequest request);
    Task<ApiResponse<bool>> ResendEmailOtpAsync(ResendEmailOtpRequest request);
    Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request);
    Task<ApiResponse<bool>> SendOtpAsync(SendOtpRequest request);
    Task<ApiResponse<AuthResponse>> VerifyOtpAsync(VerifyOtpRequest request);
    Task<ApiResponse<AuthResponse>> GoogleLoginAsync(string googleToken);
    Task<ApiResponse<bool>> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequest request);
    Task<ApiResponse<UserDto>> GetProfileAsync(int userId);
    Task<ApiResponse<UserDto>> UpdateProfileAsync(int userId, UpdateProfileRequest request);
    Task<ApiResponse<AuthResponse>> ChangePasswordAsync(int userId, ChangePasswordRequest request);
    Task<ApiResponse<AuthResponse>> RefreshTokenAsync(string refreshToken);
    Task<ApiResponse<bool>> LogoutAsync(int userId, string? sessionKey, bool allDevices);

    // Addresses
    Task<ApiResponse<List<AddressDto>>> GetAddressesAsync(int userId);
    Task<ApiResponse<AddressDto>> AddAddressAsync(int userId, AddAddressRequest request);
    Task<ApiResponse<AddressDto>> UpdateAddressAsync(int userId, int addressId, AddAddressRequest request);
    Task<ApiResponse<bool>> DeleteAddressAsync(int userId, int addressId);
}
