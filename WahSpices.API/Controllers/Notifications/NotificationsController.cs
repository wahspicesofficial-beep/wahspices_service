using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Services.Marketing;
using WahSpices.Application.Common;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Notifications;

/// <summary>
/// The signed-in customer's broadcast notifications (bell + banners) and marketing preference.
/// The bell shows UNREAD notifications only (once read they disappear, as the store owner asked);
/// banners are the unread, not-yet-closed ones of live broadcasts.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "CustomerOnly")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BroadcastService _svc;

    public NotificationsController(AppDbContext db, BroadcastService svc) { _db = db; _svc = svc; }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>One call for the header: unread count, the latest unread items and pending banners.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int take = 20)
    {
        take = Math.Clamp(take, 1, 50);
        await _svc.EnsureLateJoinerRowsAsync(UserId);
        var unread = await _svc.Visible(UserId).CountAsync();
        var items = await _svc.Visible(UserId).OrderByDescending(r => r.Broadcast!.PublishedAt).Take(take).ToListAsync();
        var banners = await _svc.PendingBanners(UserId).OrderBy(r => r.Broadcast!.PublishedAt).Take(5).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { unreadCount = unread, items = await _svc.ToDtosAsync(items), banners = await _svc.ToDtosAsync(banners) }));
    }

    /// <summary>Paged list — unread first, newest first. includeRead=true also returns read ones (history).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool includeRead = false)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        await _svc.EnsureLateJoinerRowsAsync(UserId);
        var q = _svc.Visible(UserId, includeRead);
        var total = await q.CountAsync();
        var rows = await q.OrderBy(r => r.ReadAt == null ? 0 : 1).ThenByDescending(r => r.Broadcast!.PublishedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            items = await _svc.ToDtosAsync(rows), total, page, pageSize, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
        }));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        await _svc.EnsureLateJoinerRowsAsync(UserId);
        return Ok(ApiResponse<object>.Ok(new { unreadCount = await _svc.Visible(UserId).CountAsync() }));
    }

    [HttpGet("banners")]
    public async Task<IActionResult> Banners()
    {
        await _svc.EnsureLateJoinerRowsAsync(UserId);
        var rows = await _svc.PendingBanners(UserId).OrderBy(r => r.Broadcast!.PublishedAt).Take(5).ToListAsync();
        return Ok(ApiResponse<object>.Ok(await _svc.ToDtosAsync(rows)));
    }

    /// <summary>Marks one notification read (it leaves the bell list; its banner won't show either).</summary>
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var now = DateTime.UtcNow;
        var n = await _db.BroadcastRecipients.Where(r => r.UserId == UserId && r.BroadcastId == id && r.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReadAt, now));
        if (n > 0) await _svc.PushStateAsync(UserId, new { id, readAt = now });
        return Ok(ApiResponse<object>.Ok(new { id, updated = n > 0, unreadCount = await _svc.Visible(UserId).CountAsync() }));
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var now = DateTime.UtcNow;
        var ids = await _svc.Visible(UserId).Select(r => r.Id).ToListAsync();
        var n = ids.Count == 0 ? 0 : await _db.BroadcastRecipients.Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReadAt, now));
        if (n > 0) await _svc.PushStateAsync(UserId, new { all = true, readAt = now });
        return Ok(ApiResponse<object>.Ok(new { updated = n, unreadCount = 0 }));
    }

    /// <summary>The customer closed the banner — never shown as a banner again (stays in the bell until read).</summary>
    [HttpPost("{id:int}/banner-dismissed")]
    public async Task<IActionResult> BannerDismissed(int id)
    {
        var now = DateTime.UtcNow;
        var n = await _db.BroadcastRecipients.Where(r => r.UserId == UserId && r.BroadcastId == id && r.BannerSeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.BannerSeenAt, now));
        if (n > 0) await _svc.PushStateAsync(UserId, new { id, bannerSeenAt = now });
        return Ok(ApiResponse<object>.Ok(new { id, updated = n > 0 }));
    }

    // ── Marketing preference (Profile → "Marketing emails" toggle) ──

    [HttpGet("preferences")]
    public async Task<IActionResult> Preferences()
    {
        var optOut = await _db.Users.AsNoTracking().Where(u => u.Id == UserId).Select(u => u.MarketingOptOut).FirstOrDefaultAsync();
        return Ok(ApiResponse<object>.Ok(new { marketingEmails = !optOut }));
    }

    public record PreferencesRequest(bool MarketingEmails);

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] PreferencesRequest req)
    {
        var now = DateTime.UtcNow;
        var optOut = !req.MarketingEmails;
        await _db.Users.Where(u => u.Id == UserId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.MarketingOptOut, optOut)
            .SetProperty(u => u.MarketingOptOutAt, optOut ? now : (DateTime?)null)
            .SetProperty(u => u.UpdatedAt, now));
        return Ok(ApiResponse<object>.Ok(new { marketingEmails = req.MarketingEmails },
            req.MarketingEmails ? "You'll receive offers and product news." : "You won't receive offers and product news any more."));
    }
}

/// <summary>
/// Public unsubscribe for marketing email (signed token from the email). No login needed.
///   GET  /api/marketing/unsubscribe?token=…   → state for the unsubscribe page
///   POST /api/marketing/unsubscribe?token=…   → opt out (also the RFC 8058 one-click target used by Gmail / Outlook)
///   POST /api/marketing/resubscribe           → { token } opt back in
/// </summary>
[ApiController]
[Route("api/marketing")]
[AllowAnonymous]
[Produces("application/json")]
public class MarketingPublicController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UnsubscribeTokens _tokens;

    public MarketingPublicController(AppDbContext db, UnsubscribeTokens tokens) { _db = db; _tokens = tokens; }

    public record TokenRequest(string? Token);

    [HttpGet("unsubscribe")]
    public async Task<IActionResult> Status([FromQuery] string? token)
    {
        var uid = _tokens.Validate(token);
        if (uid == null) return BadRequest(ApiResponse<object>.Fail("INVALID_LINK", "This unsubscribe link is invalid or has expired. You can change email preferences in your profile."));
        var u = await _db.Users.AsNoTracking().Where(x => x.Id == uid).Select(x => new { x.Email, x.MarketingOptOut }).FirstOrDefaultAsync();
        if (u == null) return BadRequest(ApiResponse<object>.Fail("INVALID_LINK", "This account no longer exists."));
        return Ok(ApiResponse<object>.Ok(new { email = MarketingRules.MaskEmail(u.Email), optedOut = u.MarketingOptOut }));
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string? token)
    {
        token ??= await ReadBodyTokenAsync();
        return await SetAsync(token, optOut: true);
    }

    [HttpPost("resubscribe")]
    public async Task<IActionResult> Resubscribe([FromBody] TokenRequest req) => await SetAsync(req.Token, optOut: false);

    private async Task<IActionResult> SetAsync(string? token, bool optOut)
    {
        var uid = _tokens.Validate(token);
        if (uid == null) return BadRequest(ApiResponse<object>.Fail("INVALID_LINK", "This unsubscribe link is invalid or has expired. You can change email preferences in your profile."));
        var now = DateTime.UtcNow;
        var n = await _db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.MarketingOptOut, optOut)
            .SetProperty(u => u.MarketingOptOutAt, optOut ? now : (DateTime?)null)
            .SetProperty(u => u.UpdatedAt, now));
        if (n == 0) return BadRequest(ApiResponse<object>.Fail("INVALID_LINK", "This account no longer exists."));
        return Ok(ApiResponse<object>.Ok(new { optedOut = optOut },
            optOut ? "You've been unsubscribed from offers and product news." : "You're subscribed to offers and product news again."));
    }

    /// <summary>JSON { "token": … } from the unsubscribe page. One-click posts (form body) carry the token in the query instead.</summary>
    private async Task<string?> ReadBodyTokenAsync()
    {
        if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true) return null;
        try
        {
            var req = await JsonSerializer.DeserializeAsync<TokenRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return req?.Token;
        }
        catch (JsonException) { return null; }
    }
}
