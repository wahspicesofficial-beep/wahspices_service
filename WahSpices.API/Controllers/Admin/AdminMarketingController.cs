using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services;
using WahSpices.API.Services.Marketing;
using WahSpices.Application.Common;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Messaging;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Admin → Marketing → Campaigns: compose offers / launches, pick the audience, send by email (+ WhatsApp when
/// configured) through the throttled dispatcher; channel limits; audience picker; image upload.
/// Shared by the Broadcasts page: audience/*, products, upload.
/// </summary>
[Route("api/admin/marketing")]
[RequireModule("Marketing")]
public class AdminMarketingController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly CampaignService _campaigns;
    private readonly CampaignContent _content;
    private readonly IConfiguration _config;
    private readonly ITransactionalEmailCounter _txCounter;
    private readonly IWhatsAppSender _whatsApp;

    public AdminMarketingController(AppDbContext db, CampaignService campaigns, CampaignContent content, IConfiguration config,
        ITransactionalEmailCounter txCounter, IWhatsAppSender whatsApp)
    {
        _db = db; _campaigns = campaigns; _content = content; _config = config; _txCounter = txCounter; _whatsApp = whatsApp;
    }

    private static readonly string[] EditableStatuses = { CampaignStatuses.Draft, CampaignStatuses.Scheduled };

    // ═══════════════════════════════ CAMPAIGNS ═══════════════════════════════

    public record CampaignRequest(
        string? Type, string? Title, string? Body, string? ImageUrl, string? ButtonLabel, string? ButtonUrl,
        List<int>? ProductIds, string? CouponCode, bool SendEmail, bool SendWhatsApp, string? AudienceType, List<int>? UserIds);

    [HttpGet("campaigns")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 100);
        var q = _db.MarketingCampaigns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && status != "all") q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(c => c.Title.Contains(s)); }
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(c => c.Id).Skip((page - 1) * limit).Take(limit).ToListAsync();
        var counts = await CampaignService.CountsAsync(_db, rows.Select(r => r.Id).ToList());
        var usage = await UsageAsync();
        var statusCounts = await _db.MarketingCampaigns.AsNoTracking().GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items = rows.Select(c => Summary(c, counts.GetValueOrDefault(c.Id), usage)),
            total, page, limit, totalPages = (int)Math.Ceiling(total / (double)limit),
            statusCounts = statusCounts.ToDictionary(x => x.Key, x => x.Count),
        }));
    }

    [HttpGet("campaigns/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var c = await _db.MarketingCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        var counts = await CampaignService.CountsAsync(_db, new[] { id });
        var usage = await UsageAsync();
        var selected = MarketingRules.ParseIds(c.SelectedUserIds);
        return Ok(ApiResponse<object>.Ok(new
        {
            campaign = Detail(c),
            summary = Summary(c, counts.GetValueOrDefault(id), usage),
            selectedCount = selected.Count,
            selectedUsers = c.AudienceType == AudienceTypes.Selected ? await UserSummariesAsync(selected.Take(200).ToList()) : null,
        }));
    }

    [HttpPost("campaigns")]
    public async Task<IActionResult> Create([FromBody] CampaignRequest req)
    {
        var c = new MarketingCampaign { CreatedBy = AdminName };
        var err = await ApplyAsync(c, req);
        if (err != null) return BadRequest(err);
        _db.MarketingCampaigns.Add(c);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(Detail(c), "Campaign saved as draft"));
    }

    [HttpPut("campaigns/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CampaignRequest req)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (!EditableStatuses.Contains(c.Status) && !(c.Status == CampaignStatuses.Paused && c.StartedAt == null))
            return Conflict(ApiResponse<object>.Fail("NOT_EDITABLE", $"A {c.Status.ToLowerInvariant()} campaign can't be edited. Duplicate it instead."));
        var err = await ApplyAsync(c, req);
        if (err != null) return BadRequest(err);
        c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Detail(c), "Campaign updated"));
    }

    [HttpPost("campaigns/{id:int}/duplicate")]
    public async Task<IActionResult> Duplicate(int id)
    {
        var s = await _db.MarketingCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        var c = new MarketingCampaign
        {
            Type = s.Type, Title = s.Title.Length > MarketingRules.TitleMax - 7 ? s.Title : s.Title + " (copy)", Body = s.Body, ImageUrl = s.ImageUrl,
            ButtonLabel = s.ButtonLabel, ButtonUrl = s.ButtonUrl, ProductIds = s.ProductIds, CouponCode = s.CouponCode,
            SendEmail = s.SendEmail, SendWhatsApp = s.SendWhatsApp, AudienceType = s.AudienceType, SelectedUserIds = s.SelectedUserIds,
            CreatedBy = AdminName,
        };
        _db.MarketingCampaigns.Add(c);
        await _db.SaveChangesAsync();
        return StatusCode(201, ApiResponse<object>.Ok(Detail(c), "Copied to a new draft"));
    }

    [HttpDelete("campaigns/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (c.Status is CampaignStatuses.Sending or CampaignStatuses.Paused && c.StartedAt != null)
            return Conflict(ApiResponse<object>.Fail("IN_PROGRESS", "Cancel the campaign before deleting it."));
        c.IsDeleted = true; c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id }, "Campaign deleted"));
    }

    public record ScheduleRequest(DateTime? ScheduledAt);

    [HttpPost("campaigns/{id:int}/send")]
    public async Task<IActionResult> SendNow(int id)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (!EditableStatuses.Contains(c.Status) && !(c.Status == CampaignStatuses.Paused && c.StartedAt == null))
            return Conflict(ApiResponse<object>.Fail("BAD_STATE", $"This campaign is already {c.Status.ToLowerInvariant()}."));
        var ready = await ReadyToSendAsync(c);
        if (ready != null) return BadRequest(ready);
        c.ScheduledAt = null;
        await _campaigns.StartAsync(c);
        var counts = await CampaignService.CountsAsync(_db, new[] { id });
        var all = counts.GetValueOrDefault(id)?.GetValueOrDefault("All") ?? RecipientCounts.Empty;
        return Ok(ApiResponse<object>.Ok(new { id, status = c.Status, queued = all.Pending, skipped = all.Skipped },
            all.Pending == 0 ? "Nobody to send to — every recipient was skipped." : $"Sending started — {all.Pending} message(s) queued; they go out within the channel limits."));
    }

    [HttpPost("campaigns/{id:int}/schedule")]
    public async Task<IActionResult> Schedule(int id, [FromBody] ScheduleRequest req)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (!EditableStatuses.Contains(c.Status))
            return Conflict(ApiResponse<object>.Fail("BAD_STATE", $"This campaign is already {c.Status.ToLowerInvariant()}."));
        if (req.ScheduledAt == null) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Pick a date and time.", "scheduledAt"));
        var at = req.ScheduledAt.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(req.ScheduledAt.Value, DateTimeKind.Utc) : req.ScheduledAt.Value.ToUniversalTime();
        if (at < DateTime.UtcNow.AddMinutes(1)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Schedule at least a minute ahead (or use Send now).", "scheduledAt"));
        if (at > DateTime.UtcNow.AddDays(180)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Schedule within the next 6 months.", "scheduledAt"));
        var ready = await ReadyToSendAsync(c);
        if (ready != null) return BadRequest(ready);
        c.ScheduledAt = at; c.Status = CampaignStatuses.Scheduled; c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Detail(c), "Campaign scheduled"));
    }

    [HttpPost("campaigns/{id:int}/pause")]
    public async Task<IActionResult> Pause(int id)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (c.Status is not (CampaignStatuses.Sending or CampaignStatuses.Scheduled))
            return Conflict(ApiResponse<object>.Fail("BAD_STATE", "Only sending or scheduled campaigns can be paused."));
        await _campaigns.PauseAsync(c);
        return Ok(ApiResponse<object>.Ok(Detail(c), "Campaign paused — nothing more goes out until you resume it."));
    }

    [HttpPost("campaigns/{id:int}/resume")]
    public async Task<IActionResult> Resume(int id)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (c.Status != CampaignStatuses.Paused) return Conflict(ApiResponse<object>.Fail("BAD_STATE", "Only paused campaigns can be resumed."));
        await _campaigns.ResumeAsync(c);
        return Ok(ApiResponse<object>.Ok(Detail(c), "Campaign resumed"));
    }

    [HttpPost("campaigns/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (c.Status is CampaignStatuses.Completed or CampaignStatuses.Cancelled)
            return Conflict(ApiResponse<object>.Fail("BAD_STATE", $"This campaign is already {c.Status.ToLowerInvariant()}."));
        var n = await _campaigns.CancelAsync(c);
        return Ok(ApiResponse<object>.Ok(Detail(c), n > 0 ? $"Campaign cancelled — {n} unsent message(s) dropped." : "Campaign cancelled."));
    }

    public record RetryRequest(List<long>? Ids);

    [HttpPost("campaigns/{id:int}/retry-failed")]
    public async Task<IActionResult> RetryFailed(int id, [FromBody] RetryRequest? req)
    {
        var c = await _db.MarketingCampaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Campaign not found."));
        if (c.Status == CampaignStatuses.Cancelled) return Conflict(ApiResponse<object>.Fail("BAD_STATE", "This campaign was cancelled."));
        var n = await _campaigns.RetryFailedAsync(c, req?.Ids);
        return Ok(ApiResponse<object>.Ok(new { retried = n, status = c.Status }, n == 0 ? "No failed messages to retry." : $"{n} message(s) queued again."));
    }

    [HttpGet("campaigns/{id:int}/recipients")]
    public async Task<IActionResult> Recipients(int id, [FromQuery] string? status, [FromQuery] string? channel, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 200);
        var q = _db.CampaignRecipients.AsNoTracking().Where(r => r.CampaignId == id);
        if (!string.IsNullOrWhiteSpace(status) && status != "all") q = q.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(channel) && channel != "all") q = q.Where(r => r.Channel == channel);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(r => r.Name.Contains(s) || r.Address.Contains(s)); }
        var total = await q.CountAsync();
        var items = await q.OrderBy(r => r.Id).Skip((page - 1) * limit).Take(limit)
            .Select(r => new { r.Id, r.UserId, r.Channel, r.Name, r.Address, r.Status, r.Attempts, r.NextAttemptAt, r.LastAttemptAt, r.LastError, r.SentAt })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items, total, page, limit, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)limit)) }));
    }

    [HttpPost("campaigns/preview")]
    public async Task<IActionResult> Preview([FromBody] CampaignRequest req)
    {
        var c = new MarketingCampaign();
        var err = await ApplyAsync(c, req, forPreview: true);
        if (err != null) return BadRequest(err);
        var r = await _content.PreviewAsync(c);
        return Ok(ApiResponse<object>.Ok(new { subject = r.Subject, html = r.Html }));
    }

    public record TestSendRequest(CampaignRequest Campaign, List<string>? Emails);

    [HttpPost("campaigns/test-send")]
    public async Task<IActionResult> TestSend([FromBody] TestSendRequest req, [FromServices] IEmailService email)
    {
        var to = (req.Emails ?? new()).Select(e => e?.Trim() ?? "").Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (to.Count == 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Enter at least one email address.", "emails"));
        if (to.Count > 3) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Send a test to at most 3 addresses.", "emails"));
        if (to.Any(e => !System.Net.Mail.MailAddress.TryCreate(e, out _) || !EmailAddressRules.IsDeliverable(e)))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "One of the addresses is not a valid email.", "emails"));
        var c = new MarketingCampaign();
        var err = await ApplyAsync(c, req.Campaign, forPreview: true);
        if (err != null) return BadRequest(err);
        foreach (var addr in to)
        {
            try
            {
                await email.SendAsync(await _content.BuildTestEmailAsync(c, addr));
                _txCounter.Record();
            }
            catch (Exception ex)
            {
                return StatusCode(502, ApiResponse<object>.Fail("SEND_FAILED", "The mail server rejected the test: " + MarketingDispatcherService.ClassifySmtp(ex).Error));
            }
        }
        return Ok(ApiResponse<object>.Ok(new { sent = to.Count }, $"Test sent to {string.Join(", ", to)}"));
    }

    // ═══════════════════════════════ CHANNELS ═══════════════════════════════

    [HttpGet("channels")]
    public async Task<IActionResult> Channels()
    {
        var usage = await UsageAsync();
        var pending = await _db.CampaignRecipients.AsNoTracking()
            .Where(r => r.Status == RecipientStatuses.Pending && r.Campaign!.Status == CampaignStatuses.Sending)
            .GroupBy(r => r.Channel).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(MessagingChannels.All.Select(ch =>
        {
            var u = usage[ch];
            var p = pending.FirstOrDefault(x => x.Key == ch)?.Count ?? 0;
            var (configured, reason) = ChannelConfigured(ch);
            return new
            {
                channel = ch, configured, notConfiguredReason = reason, usage = u, pending = p, eta = ChannelThrottle.Eta(u, p),
                defaults = ChannelThrottle.Default(ch, _config),
            };
        })));
    }

    public record ChannelLimitsRequest(int PerMinute, int PerHour, int PerDay, int ProviderDailyLimit, int TransactionalReserve);

    [HttpPut("channels/{channel}")]
    public async Task<IActionResult> UpdateChannel(string channel, [FromBody] ChannelLimitsRequest req)
    {
        if (!MessagingChannels.All.Contains(channel)) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Unknown channel."));
        if (req.PerMinute is < 1 or > 600 || req.PerHour is < 1 or > 20000 || req.PerDay is < 1 or > 200000)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Per minute 1–600, per hour 1–20,000, per day 1–200,000."));
        if (req.PerHour < req.PerMinute) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Per-hour limit can't be lower than the per-minute limit."));
        if (req.PerDay < req.PerHour) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Per-day limit can't be lower than the per-hour limit."));
        if (req.ProviderDailyLimit < 0 || req.TransactionalReserve < 0 || req.TransactionalReserve > 100000)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Provider limit and reserve can't be negative."));
        if (req.ProviderDailyLimit > 0 && req.TransactionalReserve >= req.ProviderDailyLimit)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "The transactional reserve must be smaller than the provider's daily limit."));

        var rows = await ChannelThrottle.EnsureSettingsAsync(_db, _config);
        var s = await _db.MessagingChannelSettings.FirstAsync(x => x.Channel == channel);
        s.PerMinute = req.PerMinute; s.PerHour = req.PerHour; s.PerDay = req.PerDay;
        s.ProviderDailyLimit = req.ProviderDailyLimit; s.TransactionalReserve = req.TransactionalReserve;
        s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(await ChannelThrottle.UsageAsync(_db, s, _txCounter), "Limits saved"));
    }

    [HttpPost("channels/{channel}/resume")]
    public async Task<IActionResult> ResumeChannel(string channel)
    {
        await ChannelThrottle.EnsureSettingsAsync(_db, _config);
        var s = await _db.MessagingChannelSettings.FirstOrDefaultAsync(x => x.Channel == channel);
        if (s == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Unknown channel."));
        s.PausedUntil = null; s.PauseReason = null; s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(await ChannelThrottle.UsageAsync(_db, s, _txCounter), $"{channel} sending resumed"));
    }

    // ═══════════════════════════════ AUDIENCE ═══════════════════════════════

    public record AudienceCountRequest(string? AudienceType, List<int>? UserIds, bool SendEmail = true, bool SendWhatsApp = false);

    /// <summary>How many people each channel would actually reach (and why the rest are skipped).</summary>
    [HttpPost("audience/count")]
    public async Task<IActionResult> AudienceCount([FromBody] AudienceCountRequest req)
    {
        var type = req.AudienceType == AudienceTypes.Selected ? AudienceTypes.Selected : AudienceTypes.All;
        var users = await Audience.LoadAsync(_db, type, req.UserIds?.Take(MarketingRules.MaxSelectedUsers).ToList());
        var plan = Audience.Plan(users, true, true);
        object Channel(string ch) => new
        {
            reachable = plan.Count(p => p.Channel == ch && p.SkipReason == null),
            skipped = plan.Where(p => p.Channel == ch && p.SkipReason != null).GroupBy(p => p.SkipReason!).ToDictionary(g => g.Key, g => g.Count()),
        };
        // in-app (broadcast) reach: active customers
        var inApp = type == AudienceTypes.All
            ? await Audience.Customers(_db).CountAsync(u => u.IsActive)
            : users.Count(u => u.IsActive);
        return Ok(ApiResponse<object>.Ok(new { audienceType = type, people = users.Count, email = Channel(MessagingChannels.Email), whatsApp = Channel(MessagingChannels.WhatsApp), inApp }));
    }

    /// <summary>Searchable customer list for "selected customers". idsOnly=true returns every matching id (for "select all results").</summary>
    [HttpGet("audience/users")]
    public async Task<IActionResult> AudienceUsers(
        [FromQuery] string? search, [FromQuery] string? city, [FromQuery] int? orderedWithinDays, [FromQuery] bool neverOrdered = false,
        [FromQuery] string? reach = null, [FromQuery] bool idsOnly = false, [FromQuery] int page = 1, [FromQuery] int limit = 25)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 100);
        var q = Audience.Customers(_db);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => u.Name.Contains(s) || u.Email.Contains(s) || u.Phone.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(city))
        {
            var cty = city.Trim();
            q = q.Where(u => _db.Addresses.Any(a => a.UserId == u.Id && !a.IsDeleted && a.City.Contains(cty)));
        }
        var validOrders = _db.Orders.AsNoTracking().Where(o => !o.IsDeleted && o.Status != OrderStatus.Cancelled);
        if (orderedWithinDays is > 0)
        {
            var since = DateTime.UtcNow.AddDays(-Math.Min(orderedWithinDays.Value, 3650));
            q = q.Where(u => validOrders.Any(o => o.UserId == u.Id && o.CreatedAt >= since));
        }
        if (neverOrdered) q = q.Where(u => !validOrders.Any(o => o.UserId == u.Id));
        if (reach == "email") q = q.Where(u => u.IsActive && u.IsEmailVerified && !u.MarketingOptOut);
        else if (reach == "optedOut") q = q.Where(u => u.MarketingOptOut);
        else if (reach == "active") q = q.Where(u => u.IsActive);

        if (idsOnly)
        {
            var ids = await q.OrderBy(u => u.Id).Select(u => u.Id).Take(MarketingRules.MaxSelectedUsers).ToListAsync();
            return Ok(ApiResponse<object>.Ok(new { ids, capped = ids.Count >= MarketingRules.MaxSelectedUsers }));
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(u => u.Id).Skip((page - 1) * limit).Take(limit)
            .Select(u => new
            {
                u.Id, u.Name, u.Email, u.Phone, u.IsActive, u.IsEmailVerified, u.MarketingOptOut, u.CreatedAt,
                City = _db.Addresses.Where(a => a.UserId == u.Id && !a.IsDeleted).OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.Id).Select(a => a.City).FirstOrDefault(),
                Orders = validOrders.Count(o => o.UserId == u.Id),
                LastOrderAt = validOrders.Where(o => o.UserId == u.Id).Max(o => (DateTime?)o.CreatedAt),
            }).ToListAsync();
        var items = rows.Select(u => new
        {
            u.Id, u.Name, u.Email, u.Phone, u.City, u.Orders, u.LastOrderAt, u.CreatedAt, u.IsActive, u.MarketingOptOut,
            emailOk = u.IsActive && u.IsEmailVerified && !u.MarketingOptOut && EmailAddressRules.IsDeliverable(u.Email),
            whatsAppOk = u.IsActive && !u.MarketingOptOut && IWhatsAppSender.NormalizeIndianMobile(u.Phone) != null,
        });
        return Ok(ApiResponse<object>.Ok(new { items, total, page, limit, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)limit)) }));
    }

    public record IdsRequest(List<int>? Ids);

    [HttpPost("audience/users/by-ids")]
    public async Task<IActionResult> UsersByIds([FromBody] IdsRequest req) =>
        Ok(ApiResponse<object>.Ok(await UserSummariesAsync((req.Ids ?? new()).Distinct().Take(500).ToList())));

    private async Task<List<object>> UserSummariesAsync(List<int> ids)
    {
        if (ids.Count == 0) return new List<object>();
        var rows = await Audience.Customers(_db).Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Email, u.Phone, u.MarketingOptOut }).ToListAsync();
        return rows.Cast<object>().ToList();
    }

    // ═══════════════════════════════ PICKERS / UPLOAD ═══════════════════════════════

    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] string? search, [FromQuery] string? ids)
    {
        var q = _db.Products.AsNoTracking().Where(p => !p.IsDraft);
        var idList = MarketingRules.ParseIds(ids);
        if (idList.Count > 0) q = q.Where(p => idList.Contains(p.Id));
        else if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(p => p.Name.Contains(s) || p.Slug.Contains(s)); }
        var items = await q.OrderBy(p => p.Name).Take(20).Select(p => new { p.Id, p.Name, p.Slug, p.Weight, p.Price, p.Mrp, p.ImageUrl, p.InStock }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("coupons")]
    public async Task<IActionResult> Coupons()
    {
        var now = DateTime.UtcNow;
        var items = await _db.Coupons.AsNoTracking().Where(c => !c.IsDeleted && c.IsActive && (c.ExpiresAt == null || c.ExpiresAt > now))
            .OrderBy(c => c.Code).Take(100).Select(c => new { c.Code, c.DiscountType, c.DiscountValue, c.ExpiresAt }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(items));
    }

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>Image for a campaign / broadcast. JPG, PNG, WEBP or GIF up to 5 MB, content checked by magic bytes.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, [FromServices] IFileUploadService files)
    {
        if (file == null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail("NO_FILE", "Choose an image to upload."));
        var ext = Path.GetExtension(Path.GetFileName(file.FileName ?? "")).ToLowerInvariant();
        if (!ImageExts.Contains(ext)) return BadRequest(ApiResponse<object>.Fail("UPLOAD_ERROR", "Use a JPG, PNG, WEBP or GIF image."));
        if (file.Length > MaxImageBytes) return BadRequest(ApiResponse<object>.Fail("UPLOAD_ERROR", "The image must be 5 MB or smaller."));
        try
        {
            var r = await files.UploadAsync(file, "marketing");
            var path = MarketingRules.NormalizeImage(r.Url);
            return Ok(ApiResponse<object>.Ok(new { url = r.Url, path }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("UPLOAD_ERROR", ex.Message));
        }
    }

    // ═══════════════════════════════ helpers ═══════════════════════════════

    private (bool, string?) ChannelConfigured(string ch)
    {
        if (ch == MessagingChannels.WhatsApp) return (_whatsApp.IsConfigured, _whatsApp.NotConfiguredReason);
        var mode = _config["Email:DeliveryMode"];
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase)) return (false, "Email delivery is disabled (Email:DeliveryMode = Disabled).");
        if (string.Equals(mode, "Outbox", StringComparison.OrdinalIgnoreCase)) return (true, null);
        var p = _config["Email:Password"];
        return string.IsNullOrWhiteSpace(_config["Email:Username"]) || string.IsNullOrWhiteSpace(p) || p.StartsWith("SET-", StringComparison.OrdinalIgnoreCase)
            ? (false, "SMTP username / password are not set (Email:Username, Email:Password).")
            : (true, null);
    }

    private async Task<Dictionary<string, ChannelUsage>> UsageAsync()
    {
        var settings = await ChannelThrottle.EnsureSettingsAsync(_db, _config);
        var d = new Dictionary<string, ChannelUsage>();
        foreach (var s in settings) d[s.Channel] = await ChannelThrottle.UsageAsync(_db, s, _txCounter);
        return d;
    }

    private async Task<ApiResponse<object>?> ReadyToSendAsync(MarketingCampaign c)
    {
        if (!c.SendEmail && !c.SendWhatsApp) return ApiResponse<object>.Fail("VALIDATION", "Choose at least one channel.");
        if (c.SendEmail && !ChannelConfigured(MessagingChannels.Email).Item1) return ApiResponse<object>.Fail("NOT_CONFIGURED", ChannelConfigured(MessagingChannels.Email).Item2!);
        if (c.SendWhatsApp && !_whatsApp.IsConfigured) return ApiResponse<object>.Fail("NOT_CONFIGURED", "WhatsApp is not configured: " + _whatsApp.NotConfiguredReason);
        if (c.AudienceType == AudienceTypes.Selected && MarketingRules.ParseIds(c.SelectedUserIds).Count == 0)
            return ApiResponse<object>.Fail("VALIDATION", "Select at least one customer.");
        if (!string.IsNullOrWhiteSpace(c.CouponCode))
        {
            var now = DateTime.UtcNow;
            var ok = await _db.Coupons.AnyAsync(x => x.Code == c.CouponCode && !x.IsDeleted && x.IsActive && (x.ExpiresAt == null || x.ExpiresAt > now));
            if (!ok) return ApiResponse<object>.Fail("VALIDATION", $"Coupon {c.CouponCode} doesn't exist, is inactive or has expired.", "couponCode");
        }
        return null;
    }

    /// <summary>Validates the request and copies it onto the entity. Returns an error response or null.</summary>
    private async Task<ApiResponse<object>?> ApplyAsync(MarketingCampaign c, CampaignRequest req, bool forPreview = false)
    {
        var title = MarketingRules.CleanLine(req.Title);
        var body = MarketingRules.CleanText(req.Body);
        if (title == null) return ApiResponse<object>.Fail("VALIDATION", "Title / subject is required.", "title");
        if (title.Length > MarketingRules.TitleMax) return ApiResponse<object>.Fail("VALIDATION", $"Title must be {MarketingRules.TitleMax} characters or fewer.", "title");
        if (body == null && !forPreview) return ApiResponse<object>.Fail("VALIDATION", "Message is required.", "body");
        if ((body?.Length ?? 0) > MarketingRules.BodyMax) return ApiResponse<object>.Fail("VALIDATION", $"Message must be {MarketingRules.BodyMax} characters or fewer.", "body");

        var type = MarketingRules.CleanLine(req.Type) ?? "Offer";
        if (!await _db.MasterDataValues.AnyAsync(m => m.Type == "CampaignType" && m.Value == type))
            return ApiResponse<object>.Fail("VALIDATION", "Pick a campaign type from the list.", "type");

        var image = MarketingRules.NormalizeImage(req.ImageUrl);
        if (MarketingRules.CheckImage(image) is { } ie) return ApiResponse<object>.Fail("VALIDATION", ie, "imageUrl");
        var label = MarketingRules.CleanLine(req.ButtonLabel);
        var link = string.IsNullOrWhiteSpace(req.ButtonUrl) ? null : req.ButtonUrl.Trim();
        if (MarketingRules.CheckLink(link) is { } le) return ApiResponse<object>.Fail("VALIDATION", le, "buttonUrl");
        if (label != null && label.Length > MarketingRules.ButtonLabelMax) return ApiResponse<object>.Fail("VALIDATION", $"Button text must be {MarketingRules.ButtonLabelMax} characters or fewer.", "buttonLabel");
        if (label != null && link == null) return ApiResponse<object>.Fail("VALIDATION", "Add a link for the button (or clear the button text).", "buttonUrl");

        var coupon = MarketingRules.CleanCoupon(req.CouponCode, out var ce);
        if (ce != null) return ApiResponse<object>.Fail("VALIDATION", ce, "couponCode");

        var products = (req.ProductIds ?? new()).Where(i => i > 0).Distinct().ToList();
        if (products.Count > MarketingRules.MaxProducts) return ApiResponse<object>.Fail("VALIDATION", $"Link at most {MarketingRules.MaxProducts} products.", "productIds");
        if (products.Count > 0 && await _db.Products.CountAsync(p => products.Contains(p.Id)) != products.Count)
            return ApiResponse<object>.Fail("VALIDATION", "One of the linked products no longer exists.", "productIds");

        var audience = req.AudienceType == AudienceTypes.Selected ? AudienceTypes.Selected : AudienceTypes.All;
        var users = (req.UserIds ?? new()).Where(i => i > 0).Distinct().ToList();
        if (users.Count > MarketingRules.MaxSelectedUsers) return ApiResponse<object>.Fail("VALIDATION", $"Select at most {MarketingRules.MaxSelectedUsers:N0} customers (or send to all).");

        if (!req.SendEmail && !req.SendWhatsApp && !forPreview) return ApiResponse<object>.Fail("VALIDATION", "Choose at least one channel.", "channels");
        if (req.SendWhatsApp && !_whatsApp.IsConfigured && !forPreview)
            return ApiResponse<object>.Fail("NOT_CONFIGURED", "WhatsApp is not configured yet: " + _whatsApp.NotConfiguredReason, "channels");

        c.Type = type; c.Title = title; c.Body = body ?? ""; c.ImageUrl = image;
        c.ButtonLabel = label; c.ButtonUrl = link; c.CouponCode = coupon;
        c.ProductIds = products.Count == 0 ? null : string.Join(',', products);
        c.SendEmail = req.SendEmail; c.SendWhatsApp = req.SendWhatsApp;
        c.AudienceType = audience;
        c.SelectedUserIds = audience == AudienceTypes.Selected ? JsonSerializer.Serialize(users) : null;
        return null;
    }

    private static object Detail(MarketingCampaign c) => new
    {
        c.Id, c.Type, c.Title, c.Body, c.ImageUrl, c.ButtonLabel, c.ButtonUrl, c.CouponCode,
        productIds = MarketingRules.ParseIds(c.ProductIds),
        c.SendEmail, c.SendWhatsApp, c.AudienceType,
        userIds = c.AudienceType == AudienceTypes.Selected ? MarketingRules.ParseIds(c.SelectedUserIds) : new List<int>(),
        c.Status, c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CancelledAt, c.CreatedAt, c.CreatedBy, c.UpdatedAt,
    };

    private static object Summary(MarketingCampaign c, Dictionary<string, RecipientCounts>? counts, Dictionary<string, ChannelUsage> usage)
    {
        counts ??= new();
        var all = counts.GetValueOrDefault("All") ?? RecipientCounts.Empty;
        DateTime? eta = null;
        if (c.Status == CampaignStatuses.Sending)
            foreach (var ch in MessagingChannels.All)
                if (counts.TryGetValue(ch, out var rc) && rc.Pending + rc.Sending > 0 && usage.TryGetValue(ch, out var u))
                {
                    var e = ChannelThrottle.Eta(u, rc.Pending + rc.Sending);
                    if (e != null && (eta == null || e > eta)) eta = e;
                }
        return new
        {
            c.Id, c.Type, c.Title, c.Status, c.AudienceType, c.SendEmail, c.SendWhatsApp, c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CreatedAt, c.CreatedBy,
            counts = all,
            email = counts.GetValueOrDefault(MessagingChannels.Email),
            whatsApp = counts.GetValueOrDefault(MessagingChannels.WhatsApp),
            eta,
        };
    }
}
