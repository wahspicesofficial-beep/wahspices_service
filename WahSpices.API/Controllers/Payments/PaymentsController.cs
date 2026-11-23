using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Payment;
using WahSpices.Application.Interfaces.Services;

namespace WahSpices.API.Controllers.Payments;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
[Authorize(Policy = "CustomerOnly")]   // customer tokens only — admin tokens also carry a NameIdentifier (the admin id)
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public PaymentsController(IPaymentService paymentService) => _paymentService = paymentService;

    /// <summary>
    /// Step 1 — Create a Razorpay order before showing the payment modal.
    /// Call this when the user clicks "Pay Now" at checkout.
    /// Returns: razorpayOrderId, amountInPaise, keyId — pass these to Razorpay JS SDK.
    /// </summary>
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreatePaymentOrderRequest request)
    {
        try
        {
            var result = await _paymentService.CreateOrderAsync(request.OrderId, UserId);
            return Ok(ApiResponse<CreatePaymentOrderResponse>.Ok(result, "Razorpay order created"));
        }
        catch (AppException ex)
        {
            var body = ApiResponse<object>.Fail(ex.ErrorCode, ex.Message);
            return ex.ErrorCode == "NOT_FOUND" ? NotFound(body) : BadRequest(body);
        }
    }

    /// <summary>
    /// Step 2 — Verify the payment after Razorpay callback.
    /// Call this after Razorpay's onSuccess handler with the three razorpay_* fields.
    /// Verifies HMAC signature and marks the order as Paid + Confirmed.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest request)
    {
        try
        {
            var result = await _paymentService.VerifyPaymentAsync(request, UserId);
            if (!result.Success)
                return BadRequest(ApiResponse<VerifyPaymentResponse>.Ok(result, result.Message));

            return Ok(ApiResponse<VerifyPaymentResponse>.Ok(result, "Payment verified successfully"));
        }
        catch (AppException ex)
        {
            var body = ApiResponse<object>.Fail(ex.ErrorCode, ex.Message);
            return ex.ErrorCode == "NOT_FOUND" ? NotFound(body) : BadRequest(body);
        }
    }

    /// <summary>
    /// Get payment status for an order (customer can check their own order).
    /// </summary>
    [HttpGet("order/{orderId:int}")]
    public async Task<IActionResult> GetPaymentByOrder(int orderId)
    {
        try
        {
            var result = await _paymentService.GetPaymentByOrderIdAsync(orderId, UserId);
            if (result == null)
                return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "No payment found for this order"));

            return Ok(ApiResponse<PaymentDetailsDto>.Ok(result));
        }
        catch (AppException ex)
        {
            var body = ApiResponse<object>.Fail(ex.ErrorCode, ex.Message);
            return ex.ErrorCode == "NOT_FOUND" ? NotFound(body) : BadRequest(body);
        }
    }
}
