using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Return;
using WahSpices.Application.Interfaces.Services;

namespace WahSpices.API.Controllers.Orders;

[ApiController]
[Route("api/returns")]
[Produces("application/json")]
[Authorize(Policy = "CustomerOnly")]   // admin tokens must not act as the customer with the same id
public class ReturnController : ControllerBase
{
    private readonly IReturnService _returnService;
    private readonly ReturnPhotoStore _photos;

    public ReturnController(IReturnService returnService, ReturnPhotoStore photos)
    {
        _returnService = returnService;
        _photos = photos;
    }

    private static readonly JsonSerializerOptions FormJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Preview refund amount + tax breakdown before submitting return</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewReturnRequest request)
    {
        var result = await _returnService.PreviewReturnTaxAsync(UserId, request.OrderId, request.Items);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Initiate a return request — auto-calculates GST reversal</summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Initiate([FromBody] InitiateReturnRequest request)
    {
        var result = await _returnService.InitiateReturnAsync(UserId, request);
        return result.Success ? StatusCode(201, result) : BadRequest(result);
    }

    /// <summary>
    /// Initiate a return with up to 5 photos (multipart/form-data: "request" = the JSON body above, "photos" = files).
    /// Photos are validated by magic bytes (JPG / PNG / WebP, ≤ 5 MB each), stripped of metadata and stored outside wwwroot.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(26 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024)]
    public async Task<IActionResult> InitiateWithPhotos([FromForm(Name = "request")] string? requestJson, [FromForm(Name = "photos")] List<IFormFile>? photos)
    {
        InitiateReturnRequest? request = null;
        try { if (!string.IsNullOrWhiteSpace(requestJson)) request = JsonSerializer.Deserialize<InitiateReturnRequest>(requestJson, FormJson); }
        catch (JsonException) { }
        if (request == null)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "The return request is missing or invalid.", "request"));

        var files = photos ?? new();
        if (files.Count > ReturnPhotoStore.MaxPhotos)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", $"You can attach up to {ReturnPhotoStore.MaxPhotos} photos.", "photos"));
        var (paths, error) = await _photos.SaveAsync(UserId, files, HttpContext.RequestAborted);
        if (error != null)
            return BadRequest(ApiResponse<object>.Fail("INVALID_PHOTO", error, "photos"));

        var result = await _returnService.InitiateReturnAsync(UserId, request, paths);
        if (!result.Success) _photos.Delete(paths);   // nothing orphaned when the return is refused
        return result.Success ? StatusCode(201, result) : BadRequest(result);
    }

    /// <summary>A photo of one of the caller's own returns (authenticated; never a public URL).</summary>
    [HttpGet("{returnId:int}/photos/{index:int}")]
    public async Task<IActionResult> Photo(int returnId, int index)
        => _photos.Serve(this, await _returnService.GetPhotoPathAsync(returnId, index, UserId));

    /// <summary>Get all returns for the logged-in user</summary>
    [HttpGet]
    public async Task<IActionResult> GetMyReturns([FromQuery] int page = 1, [FromQuery] int limit = 10)
    {
        var result = await _returnService.GetUserReturnsAsync(UserId, page, limit);
        return Ok(result);
    }

    /// <summary>Get a specific return with full tax breakdown</summary>
    [HttpGet("{returnId:int}")]
    public async Task<IActionResult> GetDetail(int returnId)
    {
        var result = await _returnService.GetReturnAsync(UserId, returnId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Cancel a return request (only if not yet picked up)</summary>
    [HttpPost("{returnId:int}/cancel")]
    public async Task<IActionResult> Cancel(int returnId)
    {
        var result = await _returnService.CancelReturnAsync(UserId, returnId);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record PreviewReturnRequest(int OrderId, List<ReturnItemInput> Items);
