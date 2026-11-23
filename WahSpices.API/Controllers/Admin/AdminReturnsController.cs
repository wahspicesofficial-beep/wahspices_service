using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.DTOs.Return;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Enums;
using WahSpices.API.Authorization;
using WahSpices.API.Services;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/returns")]
[RequireModule("Returns")]
public class AdminReturnsController : AdminBaseController
{
    private readonly IReturnService _returnService;
    private readonly ReturnPhotoStore _photos;

    public AdminReturnsController(IReturnService returnService, ReturnPhotoStore photos)
    {
        _returnService = returnService;
        _photos = photos;
    }

    /// <summary>Customer photo #index of a return (Returns module only; streamed, never public)</summary>
    [HttpGet("{returnId:int}/photos/{index:int}")]
    public async Task<IActionResult> Photo(int returnId, int index)
        => _photos.Serve(this, await _returnService.GetPhotoPathAsync(returnId, index, null));

    /// <summary>List all returns with filters</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] ReturnStatus? status,
        [FromQuery] string? search,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var filter = new ReturnFilterRequest
        {
            Status = status,
            Search = search,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            Limit = limit
        };
        var result = await _returnService.GetAllReturnsAsync(filter);
        return Ok(result);
    }

    /// <summary>KPI tiles: counts by status, open / awaiting action / awaiting refund, refunded + pending amounts</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => Ok(await _returnService.GetStatsAsync());

    /// <summary>Get return detail with full GST / tax breakdown</summary>
    [HttpGet("{returnId:int}")]
    public async Task<IActionResult> GetDetail(int returnId)
    {
        var result = await _returnService.GetReturnDetailAsync(returnId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Approve return — recalculates GST and generates tax summary</summary>
    [HttpPost("{returnId:int}/approve")]
    public async Task<IActionResult> Approve(int returnId, [FromBody] ApproveReturnRequest request)
    {
        var result = await _returnService.ApproveReturnAsync(returnId, request);
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>Reject return</summary>
    [HttpPost("{returnId:int}/reject")]
    public async Task<IActionResult> Reject(int returnId, [FromBody] RejectReturnRequest request)
    {
        var result = await _returnService.RejectReturnAsync(returnId, request?.Reason ?? "");
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>Mark items as picked up by courier</summary>
    [HttpPost("{returnId:int}/pickup")]
    public async Task<IActionResult> MarkPickedUp(int returnId, [FromBody] MarkPickupRequest request)
    {
        var result = await _returnService.MarkPickedUpAsync(returnId, request?.AwbNumber ?? "", request?.CourierName ?? "");
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>Mark items as received at warehouse</summary>
    [HttpPost("{returnId:int}/received")]
    public async Task<IActionResult> MarkReceived(int returnId, [FromBody] MarkReceivedRequest request)
    {
        var result = await _returnService.MarkReceivedAsync(returnId, request.AdminNotes);
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>Process refund — finalises credit note and marks order as Refunded</summary>
    [HttpPost("{returnId:int}/process-refund")]
    public async Task<IActionResult> ProcessRefund(int returnId, [FromBody] ProcessRefundRequest request)
    {
        var result = await _returnService.ProcessRefundAsync(returnId, request);
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }
}

public record RejectReturnRequest(string? Reason);
public record MarkPickupRequest(string? AwbNumber, string? CourierName);
public record MarkReceivedRequest(string? AdminNotes);
