using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Payment;
using WahSpices.Application.Interfaces.Services;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/payments")]
[RequireModule("Payments")]
public class AdminPaymentsController : AdminBaseController
{
    private readonly IPaymentService _paymentService;
    private new string AdminName => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    public AdminPaymentsController(IPaymentService paymentService) => _paymentService = paymentService;

    /// <summary>
    /// List all payments with optional filters.
    /// Query params: page, limit, status (Pending/Initiated/Paid/Failed/Refunded/PartiallyRefunded),
    ///              search (order number or Razorpay ID), fromDate, toDate
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaymentFilterParams filter)
    {
        try
        {
            var result = await _paymentService.GetAllPaymentsAsync(filter);
            return Ok(ApiResponse<PagedResult<PaymentListItemDto>>.Ok(result));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Get full payment details including all refunds.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var result = await _paymentService.GetPaymentByIdAsync(id);
            if (result == null)
                return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Payment not found"));

            return Ok(ApiResponse<PaymentDetailsDto>.Ok(result));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>
    /// Initiate a refund for a payment.
    /// Calls Razorpay refund API and saves the refund record.
    /// Body: { amount, reason, notes, returnOrderId (optional), method }
    /// </summary>
    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> InitiateRefund(int id, [FromBody] InitiateRefundRequest request)
    {
        try
        {
            var result = await _paymentService.InitiateRefundAsync(id, request, AdminName);
            return Ok(ApiResponse<RefundDetailsDto>.Ok(result, $"Refund of ₹{request.Amount} initiated successfully"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>
    /// Sync refund status from Razorpay (in case webhook was missed).
    /// Fetches latest status from Razorpay API and updates the DB.
    /// </summary>
    [HttpPost("refunds/{refundId:int}/sync")]
    public async Task<IActionResult> SyncRefundStatus(int refundId)
    {
        try
        {
            var result = await _paymentService.SyncRefundStatusAsync(refundId);
            if (result == null)
                return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Refund not found or no Razorpay refund ID"));

            return Ok(ApiResponse<RefundDetailsDto>.Ok(result, "Refund status synced"));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }
}
