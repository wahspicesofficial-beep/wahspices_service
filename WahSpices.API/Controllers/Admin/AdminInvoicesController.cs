using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Invoice;
using WahSpices.Application.Interfaces.Services;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/invoices")]
[RequireModule("Billing")]
public class AdminInvoicesController : AdminBaseController
{
    private readonly IInvoiceService _invoices;
    private new string AdminName => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    public AdminInvoicesController(IInvoiceService invoices) => _invoices = invoices;

    /// <summary>List invoices. Query: page, limit, status, type, search, fromDate, toDate</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] InvoiceFilterParams filter)
    {
        try
        {
            var result = await _invoices.GetAllAsync(filter);
            return Ok(ApiResponse<PagedResult<InvoiceListItemDto>>.Ok(result));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field));
        }
    }

    /// <summary>KPI tiles + counts per status (Query: type, search, fromDate, toDate — status is ignored)</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] InvoiceFilterParams filter)
    {
        try { return Ok(ApiResponse<InvoiceSummaryDto>.Ok(await _invoices.GetSummaryAsync(filter))); }
        catch (AppException ex) { return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field)); }
    }

    /// <summary>Get full invoice detail with line items</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var result = await _invoices.GetByIdAsync(id);
            if (result == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Invoice not found"));
            return Ok(ApiResponse<InvoiceDto>.Ok(result));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Get invoice by order ID</summary>
    [HttpGet("order/{orderId:int}")]
    public async Task<IActionResult> GetByOrder(int orderId)
    {
        try
        {
            var result = await _invoices.GetByOrderIdAsync(orderId);
            if (result == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Invoice not found for this order"));
            return Ok(ApiResponse<InvoiceDto>.Ok(result));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Auto-generate invoice from a confirmed order</summary>
    [HttpPost("generate/{orderId:int}")]
    public async Task<IActionResult> GenerateFromOrder(int orderId)
    {
        try
        {
            var result = await _invoices.GenerateFromOrderAsync(orderId);
            return Ok(ApiResponse<InvoiceDto>.Ok(result, "Invoice generated successfully"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Create manual invoice (for B2B or custom scenarios)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request)
    {
        try
        {
            var result = await _invoices.CreateManualAsync(request, AdminName);
            var message = $"Invoice {result.InvoiceNumber} created";
            if (request.SendEmail && !string.IsNullOrWhiteSpace(result.CustomerEmail))
            {
                try
                {
                    await _invoices.SendInvoiceAsync(result.Id, new SendInvoiceRequest("email", result.CustomerEmail, null, null));
                    message += $" and emailed to {result.CustomerEmail}";
                    result = await _invoices.GetByIdAsync(result.Id) ?? result;
                }
                catch (AppException ex) { message += $", but the email failed: {ex.Message}"; }
            }
            return Ok(ApiResponse<InvoiceDto>.Ok(result, message));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>
    /// Send invoice via email or WhatsApp.
    /// Body: { via: "email"|"whatsapp", email?, phone?, baseUrl? }
    /// </summary>
    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> Send(int id, [FromBody] SendInvoiceRequest request)
    {
        try
        {
            var result = await _invoices.SendInvoiceAsync(id, request);
            return Ok(ApiResponse<SendInvoiceResultDto>.Ok(result, result.Message));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Get invoice template / company settings</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            var result = await _invoices.GetSettingsAsync();
            return Ok(ApiResponse<InvoiceSettingsDto>.Ok(result));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Update invoice status (Paid, Cancelled, etc.)</summary>
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateInvoiceStatusRequest req)
    {
        try
        {
            var result = await _invoices.UpdateStatusAsync(id, req.Status ?? "", AdminName);
            return Ok(ApiResponse<InvoiceDto>.Ok(result, "Status updated"));
        }
        catch (AppException ex) { return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field)); }
        catch (Exception)   { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    /// <summary>Update invoice template / company settings</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateInvoiceSettingsRequest request)
    {
        try
        {
            var result = await _invoices.UpdateSettingsAsync(request, AdminName);
            return Ok(ApiResponse<InvoiceSettingsDto>.Ok(result, "Settings updated"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field));
        }
        catch (Exception)
        {
            throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */
        }
    }

    /// <summary>Download the invoice as PDF (?inline=true to display in the browser)</summary>
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id, [FromQuery] bool inline = false)
    {
        var r = await _invoices.GetPdfAsync(id);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Invoice not found"));
        Response.Headers.CacheControl = "no-store";
        if (inline)
        {
            Response.Headers.ContentDisposition = $"inline; filename=\"{r.Value.FileName}\"";
            return File(r.Value.Pdf, "application/pdf");
        }
        return File(r.Value.Pdf, "application/pdf", r.Value.FileName);
    }

    /// <summary>Sample invoice PDF rendered with the (unsaved) template settings in the body</summary>
    [HttpPost("settings/preview")]
    public async Task<IActionResult> PreviewSettings([FromBody] UpdateInvoiceSettingsRequest request)
    {
        try
        {
            var pdf = await _invoices.PreviewPdfAsync(request);
            Response.Headers.CacheControl = "no-store";
            Response.Headers.ContentDisposition = "inline; filename=\"Invoice-preview.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (AppException ex) { return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field)); }
    }

    /// <summary>Save the IRN / Ack details returned by the e-invoice portal (IRP)</summary>
    [HttpPut("{id:int}/e-invoice")]
    public async Task<IActionResult> UpdateEInvoice(int id, [FromBody] UpdateEInvoiceRequest request)
    {
        try { return Ok(ApiResponse<InvoiceDto>.Ok(await _invoices.UpdateEInvoiceAsync(id, request, AdminName), "e-Invoice details saved")); }
        catch (AppException ex) { return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field)); }
    }

    /// <summary>GST e-invoice JSON (schema 1.1) for upload to the IRP / offline tool</summary>
    [HttpGet("{id:int}/e-invoice.json")]
    public async Task<IActionResult> EInvoiceJson(int id)
    {
        try
        {
            var (json, name) = await _invoices.GetEInvoiceJsonAsync(id);
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", name);
        }
        catch (AppException ex) { return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.ErrorCode, ex.Message, ex.Field)); }
    }
}
