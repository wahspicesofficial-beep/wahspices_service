using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Services.Shipping;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Shipping;

/// <summary>
/// Courier tracking webhook. The path deliberately avoids provider names — Shiprocket refuses webhook URLs that
/// contain "shiprocket", "sr", "kr" … The caller proves itself with the per-partner token (header x-api-key, as set
/// in the provider panel and in Admin → Shipping → Partners); the matching partner's adapter parses the body.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/hooks/parcel-updates")]
public class ParcelUpdatesController(AppDbContext db, ShippingService shipping, ShippingProviderRegistry registry,
    ILogger<ParcelUpdatesController> logger) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Receive([FromBody] JsonElement body, CancellationToken ct)
    {
        var token = Request.Headers["x-api-key"].FirstOrDefault()
                    ?? Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                    ?? Request.Headers["token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();

        var accounts = await db.ShippingProviderAccounts.Where(a => a.IsEnabled && !a.IsDeleted && a.WebhookToken != null).ToListAsync(ct);
        var account = accounts.FirstOrDefault(a => FixedEquals(a.WebhookToken!, token.Trim()));
        if (account == null) return Unauthorized();
        if (!registry.Has(account.ProviderCode)) return Ok(new { received = true });

        var update = registry.Get(account.ProviderCode).ParseWebhook(body);
        if (update == null) return Ok(new { received = true, ignored = true });   // test pings etc.
        try
        {
            var matched = await shipping.ApplyWebhookAsync(account, update, ct);
            return Ok(new { received = true, matched });
        }
        catch (Exception ex)
        {
            // Always 200 so the provider doesn't disable the webhook; polling will catch up.
            logger.LogError(ex, "Courier webhook for AWB {Awb} could not be applied", update.Awb);
            return Ok(new { received = true, matched = false });
        }
    }

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
