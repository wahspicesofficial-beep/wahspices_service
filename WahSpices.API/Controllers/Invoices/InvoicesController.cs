using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Invoice;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Invoices;

/// <summary>
/// Customer-facing invoice reads. An invoice holds the customer's name, phone, email and addresses, so:
///  - by order id: only the signed-in customer who owns the order;
///  - by invoice number (email/WhatsApp links): the owner when signed in, otherwise the caller must also pass the
///    phone number or email on the invoice (?contact=…); failed lookups are throttled per IP.
/// Anything else is a 404 (never reveal whether an invoice exists).
/// </summary>
[ApiController]
[Route("api/invoices")]
[Produces("application/json")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoices;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    private int? CustomerId =>
        User.Identity?.IsAuthenticated == true && User.FindFirst("admin_id") == null && User.IsInRole("Customer") &&
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public InvoicesController(IInvoiceService invoices, AppDbContext db, IMemoryCache cache)
    {
        _invoices = invoices;
        _db = db;
        _cache = cache;
    }

    private static NotFoundObjectResult NotFoundResult() => new(ApiResponse<object>.Fail("NOT_FOUND", "Invoice not found"));

    /// <summary>Get invoice for one of the signed-in customer's orders</summary>
    [HttpGet("order/{orderId:int}")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> GetByOrder(int orderId)
    {
        var uid = CustomerId;
        if (uid == null || !await _db.Orders.AnyAsync(o => o.Id == orderId && o.UserId == uid && !o.IsDeleted))
            return NotFoundResult();
        var result = await _invoices.GetByOrderIdAsync(orderId);
        return result == null ? NotFoundResult() : Ok(ApiResponse<InvoiceDto>.Ok(result));
    }

    /// <summary>Download the PDF invoice for one of the signed-in customer's orders</summary>
    [HttpGet("order/{orderId:int}/pdf")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> PdfByOrder(int orderId)
    {
        var uid = CustomerId;
        if (uid == null || !await _db.Orders.AnyAsync(o => o.Id == orderId && o.UserId == uid && !o.IsDeleted))
            return NotFoundResult();
        var r = await _invoices.GetPdfByOrderAsync(orderId);
        if (r == null) return NotFoundResult();
        Response.Headers.CacheControl = "no-store";
        return File(r.Value.Pdf, "application/pdf", r.Value.FileName);
    }

    /// <summary>Get invoice by invoice number — owner, or anyone who also knows the phone/email on the invoice</summary>
    [HttpGet("{**invoiceNumber}")]   // numbers contain "/" (WAH/2026/049) — accept raw or %2F-encoded
    [AllowAnonymous]
    public async Task<IActionResult> GetByNumber(string invoiceNumber, [FromQuery] string? contact)
    {
        invoiceNumber = Uri.UnescapeDataString(invoiceNumber ?? "");
        if (string.IsNullOrWhiteSpace(invoiceNumber) || invoiceNumber.Length > 64) return NotFoundResult();
        var inv = await _db.Invoices.AsNoTracking()
            .Where(i => i.InvoiceNumber == invoiceNumber && !i.IsDeleted)
            .Select(i => new { i.OrderId, i.CustomerPhone, i.CustomerEmail, OwnerId = i.Order != null ? (int?)i.Order.UserId : null })
            .FirstOrDefaultAsync();

        var uid = CustomerId;
        var allowed = inv != null && uid != null && inv.OwnerId == uid;
        if (!allowed)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"invoice-fail:{ip}";
            var fails = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15); return 0; });
            if (fails >= 10)
                return StatusCode(429, ApiResponse<object>.Fail("TOO_MANY_ATTEMPTS", "Too many attempts. Please wait 15 minutes and try again."));

            allowed = inv != null && ContactMatches(contact, inv.CustomerPhone, inv.CustomerEmail);
            if (!allowed)
            {
                _cache.Set(key, fails + 1, TimeSpan.FromMinutes(15));
                return NotFoundResult();
            }
        }

        var result = await _invoices.GetByInvoiceNumberAsync(invoiceNumber);
        return result == null ? NotFoundResult() : Ok(ApiResponse<InvoiceDto>.Ok(result));
    }

    private static bool ContactMatches(string? contact, string? phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(contact)) return false;
        var c = contact.Trim();
        if (c.Contains('@')) return !string.IsNullOrWhiteSpace(email) && string.Equals(c, email.Trim(), StringComparison.OrdinalIgnoreCase);
        static string Last10(string s) { var d = new string(s.Where(char.IsDigit).ToArray()); return d.Length > 10 ? d[^10..] : d; }
        var cd = Last10(c);
        return cd.Length == 10 && !string.IsNullOrWhiteSpace(phone) && Last10(phone) == cd;
    }
}
