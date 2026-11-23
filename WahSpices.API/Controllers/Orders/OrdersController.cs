using WahSpices.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Order;
using WahSpices.Application.DTOs.Return;
using WahSpices.Application.Interfaces.Services;
using System.Security.Claims;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace WahSpices.API.Controllers.Orders;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IReturnService _returnService;
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;

    public OrdersController(IOrderService orderService, IReturnService returnService, IMemoryCache cache, AppDbContext db)
    {
        _orderService = orderService;
        _returnService = returnService;
        _cache = cache;
        _db = db;
    }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private int? OptionalUserId =>
        User.Identity?.IsAuthenticated == true && User.FindFirst("admin_id") == null &&
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Price a cart exactly as placing the order would (public — guest carts too)</summary>
    [HttpPost("quote")]
    [RequireHumanProof("quote")]
    public async Task<IActionResult> Quote([FromBody] QuoteRequest request)
    {
        var result = await _orderService.QuoteAsync(request ?? new QuoteRequest(new(), null));
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Place a new order</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPost]
    [RequireHumanProof("order")]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var result = await _orderService.PlaceOrderAsync(UserId, request);
        if (result.Success) return StatusCode(201, result);
        return result.Error?.Code == "MAINTENANCE" ? StatusCode(503, result) : BadRequest(result);
    }

    /// <summary>Get logged-in user's orders</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpGet]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10)
    {
        var result = await _orderService.GetUserOrdersAsync(UserId, status, page, limit);
        return Ok(result);
    }

    /// <summary>Get order detail (items, history, payment, return/refund)</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetDetail(int orderId)
    {
        var result = await _orderService.GetOrderDetailAsync(UserId, orderId);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>
    /// Track an order. Public callers must pass the phone number or email used on the order
    /// (?contact=…, or ?phone= / ?email=); a logged-in customer can track their own orders without it.
    /// Failed public lookups are throttled per IP.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("track/{orderNumber}")]
    [RequireHumanProof("track")]
    public async Task<IActionResult> Track(string orderNumber, [FromQuery] string? contact, [FromQuery] string? phone, [FromQuery] string? email)
    {
        var who = contact ?? phone ?? email;
        var uid = OptionalUserId;
        if (uid.HasValue && string.IsNullOrWhiteSpace(who))
        {
            var own = await _orderService.TrackForUserAsync(uid.Value, orderNumber);
            return own.Success ? Ok(own) : NotFound(own);
        }
        if (string.IsNullOrWhiteSpace(who))
            return BadRequest(ApiResponse<object>.Fail("CONTACT_REQUIRED",
                "Enter the phone number or email used for the order to track it.", "contact"));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"track-fail:{ip}";
        var fails = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15); return 0; });
        if (fails >= 15)
            return StatusCode(429, ApiResponse<object>.Fail("TOO_MANY_ATTEMPTS", "Too many tracking attempts. Please wait 15 minutes and try again."));

        var result = await _orderService.TrackPublicAsync(orderNumber, who);
        if (result.Success) return Ok(result);
        if (result.Error?.Code == "NOT_FOUND") _cache.Set(key, fails + 1, TimeSpan.FromMinutes(15));
        return result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>Cancel an order</summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPost("{orderId:int}/cancel")]
    public async Task<IActionResult> Cancel(int orderId, [FromBody] CancelOrderRequest request)
    {
        var result = await _orderService.CancelOrderAsync(UserId, orderId, request?.Reason ?? "");
        return result.Success ? Ok(result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }

    /// <summary>
    /// Request a return for a whole order (shortcut). Creates a real return (same flow as POST /api/returns,
    /// visible in Admin → Returns) for every item of the order.
    /// </summary>
    [Authorize(Policy = "CustomerOnly")]
    [HttpPost("{orderId:int}/return")]
    public async Task<IActionResult> RequestReturn(int orderId, [FromBody] CancelOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Please tell us why you're returning this order.", "reason"));
        var items = await _db.OrderItems
            .Where(i => i.OrderId == orderId && !i.IsDeleted && i.Order.UserId == UserId)
            .Select(i => new ReturnItemInput { OrderItemId = i.Id, Quantity = i.Quantity, Reason = request.Reason })
            .ToListAsync();
        if (items.Count == 0) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Order not found"));
        var result = await _returnService.InitiateReturnAsync(UserId, new InitiateReturnRequest
        {
            OrderId = orderId, Reason = request.Reason.Trim(), Items = items,
        });
        return result.Success ? StatusCode(201, result) : result.Error?.Code == "NOT_FOUND" ? NotFound(result) : BadRequest(result);
    }
}

public record CancelOrderRequest(string Reason);
