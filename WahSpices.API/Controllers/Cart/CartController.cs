using WahSpices.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Interfaces.Services;
using System.Security.Claims;

namespace WahSpices.API.Controllers.Cart;

[Authorize(Policy = "CustomerOnly")]   // customer tokens only — admin tokens also carry a NameIdentifier (the admin id)
[ApiController]
[Route("api/cart")]
[Produces("application/json")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService) => _cartService = cartService;

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get current user's cart</summary>
    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var result = await _cartService.GetCartAsync(UserId);
        return Ok(result);
    }

    /// <summary>Add item to cart</summary>
    [HttpPost("add")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request)
    {
        var result = await _cartService.AddItemAsync(UserId, request.ProductId, request.Quantity);
        return Ok(result);
    }

    /// <summary>Update item quantity</summary>
    [HttpPut("update")]
    public async Task<IActionResult> UpdateItem([FromBody] UpdateCartItemRequest request)
    {
        var result = await _cartService.UpdateItemAsync(UserId, request.ProductId, request.Quantity);
        return Ok(result);
    }

    /// <summary>Remove item from cart</summary>
    [HttpDelete("remove/{productId:int}")]
    public async Task<IActionResult> RemoveItem(int productId)
    {
        var result = await _cartService.RemoveItemAsync(UserId, productId);
        return Ok(result);
    }

    /// <summary>Clear entire cart</summary>
    [HttpDelete("clear")]
    public async Task<IActionResult> Clear()
    {
        var result = await _cartService.ClearCartAsync(UserId);
        return Ok(result);
    }

    /// <summary>Apply coupon code</summary>
    [HttpPost("apply-coupon")]
    [RequireHumanProof("coupon")]
    public async Task<IActionResult> ApplyCoupon([FromBody] ApplyCouponDto request)
    {
        var result = await _cartService.ApplyCouponAsync(UserId, request.CouponCode);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Remove applied coupon</summary>
    [HttpDelete("remove-coupon")]
    public async Task<IActionResult> RemoveCoupon()
    {
        var result = await _cartService.RemoveCouponAsync(UserId);
        return Ok(result);
    }

    /// <summary>Sync guest cart after login</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] SyncCartRequest request)
    {
        var result = await _cartService.SyncCartAsync(UserId, request.Items);
        return Ok(result);
    }
}

public record AddCartItemRequest(int ProductId, int Quantity);
public record UpdateCartItemRequest(int ProductId, int Quantity);
public record ApplyCouponDto(string CouponCode);
public record SyncCartRequest(List<CartItemRequest> Items);
