using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Return;

namespace WahSpices.Application.Interfaces.Services;

public interface IReturnService
{
    // Customer APIs
    Task<ApiResponse<ReturnDto>> InitiateReturnAsync(int userId, InitiateReturnRequest request, IReadOnlyList<string>? photoPaths = null);
    /// <summary>Stored relative path of photo #index; userId = owner check (null = admin).</summary>
    Task<string?> GetPhotoPathAsync(int returnId, int index, int? userId);
    Task<ApiResponse<ReturnDto>> GetReturnAsync(int userId, int returnId);
    Task<ApiResponse<PagedResult<ReturnSummaryDto>>> GetUserReturnsAsync(int userId, int page, int limit);
    Task<ApiResponse<bool>> CancelReturnAsync(int userId, int returnId);

    // Admin APIs
    Task<ApiResponse<PagedResult<ReturnSummaryDto>>> GetAllReturnsAsync(ReturnFilterRequest filter);
    Task<ApiResponse<ReturnStatsDto>> GetStatsAsync();
    Task<ApiResponse<ReturnDto>> GetReturnDetailAsync(int returnId);
    Task<ApiResponse<ReturnDto>> ApproveReturnAsync(int returnId, ApproveReturnRequest request);
    Task<ApiResponse<ReturnDto>> RejectReturnAsync(int returnId, string reason);
    Task<ApiResponse<ReturnDto>> MarkPickedUpAsync(int returnId, string awbNumber, string courierName);
    Task<ApiResponse<ReturnDto>> MarkReceivedAsync(int returnId, string? adminNotes);
    Task<ApiResponse<ReturnDto>> ProcessRefundAsync(int returnId, ProcessRefundRequest request);

    // Tax preview (call before initiating return to show customer what refund they'll get)
    Task<ApiResponse<ReturnTaxPreviewDto>> PreviewReturnTaxAsync(int userId, int orderId, List<ReturnItemInput> items);
}
