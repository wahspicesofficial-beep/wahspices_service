using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services.Marketing;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Admin → Marketing → Broadcasts: in-app banner + notification-bell messages, delivered live over SignalR.
/// Publish now, schedule (ActiveFrom), end early, delete. Seen / read counts per broadcast.
/// </summary>
[Route("api/admin/marketing/broadcasts")]
[RequireModule("Marketing")]
public class AdminBroadcastsController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly BroadcastService _svc;

    public AdminBroadcastsController(AppDbContext db, BroadcastService svc) { _db = db; _svc = svc; }

    public record BroadcastRequest(
        string? Type, string? Title, string? Message, string? ImageUrl, string? ButtonLabel, string? ButtonUrl,
        string? AudienceType, List<int>? UserIds, bool ShowAsBanner, DateTime? ActiveFrom, DateTime? ActiveUntil,
        string? Action);   // "draft" | "publish" (now, or at ActiveFrom when it is in the future)

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 100);
        var q = _db.Broadcasts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && status != "all") q = q.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(b => b.Title.Contains(s) || b.Message.Contains(s)); }
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(b => b.Id).Skip((page - 1) * limit).Take(limit).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var stats = await StatsAsync(ids);
        var statusCounts = await _db.Broadcasts.AsNoTracking().GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            items = rows.Select(b => Map(b, stats.GetValueOrDefault(b.Id))),
            total, page, limit, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)limit)),
            statusCounts = statusCounts.ToDictionary(x => x.Key, x => x.Count),
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var b = await _db.Broadcasts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Broadcast not found."));
        var stats = await StatsAsync(new List<int> { id });
        var selected = MarketingRules.ParseIds(b.SelectedUserIds);
        var firstIds = selected.Take(200).ToList();
        var selectedUsers = firstIds.Count == 0 ? null : await _db.Users.AsNoTracking().Where(u => firstIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Email, u.Phone, u.MarketingOptOut }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { broadcast = Map(b, stats.GetValueOrDefault(id)), selectedUsers }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BroadcastRequest req)
    {
        var b = new Broadcast { CreatedBy = AdminName };
        var err = await ApplyAsync(b, req);
        if (err != null) return BadRequest(err);
        _db.Broadcasts.Add(b);
        await _db.SaveChangesAsync();
        return await AfterSaveAsync(b, req.Action, created: true);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] BroadcastRequest req)
    {
        var b = await _db.Broadcasts.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Broadcast not found."));
        if (b.Status is not (BroadcastStatuses.Draft or BroadcastStatuses.Scheduled))
            return Conflict(ApiResponse<object>.Fail("NOT_EDITABLE", "A published broadcast can't be edited — end it and publish a new one."));
        var err = await ApplyAsync(b, req);
        if (err != null) return BadRequest(err);
        b.UpdatedAt = DateTime.UtcNow; b.UpdatedBy = AdminName;
        if (b.Status == BroadcastStatuses.Scheduled && req.Action != "publish") b.Status = BroadcastStatuses.Draft;
        await _db.SaveChangesAsync();
        return await AfterSaveAsync(b, req.Action, created: false);
    }

    [HttpPost("{id:int}/publish")]
    public async Task<IActionResult> Publish(int id)
    {
        var b = await _db.Broadcasts.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Broadcast not found."));
        if (b.Status is not (BroadcastStatuses.Draft or BroadcastStatuses.Scheduled))
            return Conflict(ApiResponse<object>.Fail("BAD_STATE", "This broadcast is already published."));
        b.ActiveFrom = null;   // "publish now" overrides a schedule
        return await AfterSaveAsync(b, "publish", created: false);
    }

    [HttpPost("{id:int}/end")]
    public async Task<IActionResult> End(int id)
    {
        var b = await _db.Broadcasts.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Broadcast not found."));
        if (b.Status != BroadcastStatuses.Published) return Conflict(ApiResponse<object>.Fail("BAD_STATE", "Only a live broadcast can be ended."));
        await _svc.EndAsync(b);
        return Ok(ApiResponse<object>.Ok(Map(b, null), "Broadcast ended — no more banners; it stays in customers' notification lists until read."));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var b = await _db.Broadcasts.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Broadcast not found."));
        await _svc.RemoveAsync(b, AdminName);
        return Ok(ApiResponse<object>.Ok(new { id }, "Broadcast deleted — removed from every customer's notifications."));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<IActionResult> AfterSaveAsync(Broadcast b, string? action, bool created)
    {
        string msg;
        if (action == "publish")
        {
            if (b.ActiveFrom != null && b.ActiveFrom > DateTime.UtcNow.AddSeconds(30))
            {
                b.Status = BroadcastStatuses.Scheduled;
                b.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                msg = "Broadcast scheduled";
            }
            else
            {
                var n = await _svc.PublishAsync(b);
                msg = $"Broadcast published to {n} customer(s)";
            }
        }
        else msg = created ? "Broadcast saved as draft" : "Broadcast updated";
        var stats = await StatsAsync(new List<int> { b.Id });
        var body = ApiResponse<object>.Ok(Map(b, stats.GetValueOrDefault(b.Id)), msg);
        return created ? StatusCode(201, body) : Ok(body);
    }

    private sealed record Stats(int Recipients, int BannerSeen, int Read);

    private async Task<Dictionary<int, Stats>> StatsAsync(List<int> ids)
    {
        if (ids.Count == 0) return new();
        var rows = await _db.BroadcastRecipients.AsNoTracking().Where(r => ids.Contains(r.BroadcastId))
            .GroupBy(r => r.BroadcastId)
            .Select(g => new { Id = g.Key, Total = g.Count(), Seen = g.Count(r => r.BannerSeenAt != null), Read = g.Count(r => r.ReadAt != null) })
            .ToListAsync();
        return rows.ToDictionary(r => r.Id, r => new Stats(r.Total, r.Seen, r.Read));
    }

    private static object Map(Broadcast b, Stats? s) => new
    {
        b.Id, b.Type, b.Title, b.Message, b.ImageUrl, b.ButtonLabel, b.ButtonUrl, b.AudienceType,
        userIds = MarketingRules.ParseIds(b.SelectedUserIds),
        b.ShowAsBanner, b.ActiveFrom, b.ActiveUntil, b.Status, b.PublishedAt, b.EndedAt, b.CreatedAt, b.CreatedBy,
        recipients = s?.Recipients ?? 0, bannerSeen = s?.BannerSeen ?? 0, read = s?.Read ?? 0,
    };

    private static DateTime? Utc(DateTime? d) => d == null ? null
        : d.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc) : d.Value.ToUniversalTime();

    private async Task<ApiResponse<object>?> ApplyAsync(Broadcast b, BroadcastRequest req)
    {
        var title = MarketingRules.CleanLine(req.Title);
        var message = MarketingRules.CleanText(req.Message);
        var image = MarketingRules.NormalizeImage(req.ImageUrl);
        if (title == null) return ApiResponse<object>.Fail("VALIDATION", "Title is required.", "title");
        if (title.Length > MarketingRules.TitleMax) return ApiResponse<object>.Fail("VALIDATION", $"Title must be {MarketingRules.TitleMax} characters or fewer.", "title");
        if (message == null && image == null) return ApiResponse<object>.Fail("VALIDATION", "Add a message or an image.", "message");
        if ((message?.Length ?? 0) > MarketingRules.BroadcastMessageMax) return ApiResponse<object>.Fail("VALIDATION", $"Message must be {MarketingRules.BroadcastMessageMax} characters or fewer.", "message");

        var type = MarketingRules.CleanLine(req.Type) ?? "Offer";
        if (!await _db.MasterDataValues.AnyAsync(m => m.Type == "CampaignType" && m.Value == type))
            return ApiResponse<object>.Fail("VALIDATION", "Pick a type from the list.", "type");
        if (MarketingRules.CheckImage(image) is { } ie) return ApiResponse<object>.Fail("VALIDATION", ie, "imageUrl");

        var label = MarketingRules.CleanLine(req.ButtonLabel);
        var link = string.IsNullOrWhiteSpace(req.ButtonUrl) ? null : req.ButtonUrl.Trim();
        if (MarketingRules.CheckLink(link) is { } le) return ApiResponse<object>.Fail("VALIDATION", le, "buttonUrl");
        if (label != null && label.Length > MarketingRules.ButtonLabelMax) return ApiResponse<object>.Fail("VALIDATION", $"Button text must be {MarketingRules.ButtonLabelMax} characters or fewer.", "buttonLabel");
        if (label != null && link == null) return ApiResponse<object>.Fail("VALIDATION", "Add a link for the button (or clear the button text).", "buttonUrl");

        var from = Utc(req.ActiveFrom);
        var until = Utc(req.ActiveUntil);
        if (until != null && until <= DateTime.UtcNow) return ApiResponse<object>.Fail("VALIDATION", "'Show until' must be in the future.", "activeUntil");
        if (from != null && until != null && until <= from) return ApiResponse<object>.Fail("VALIDATION", "'Show until' must be after 'Start at'.", "activeUntil");
        if (from != null && from > DateTime.UtcNow.AddDays(180)) return ApiResponse<object>.Fail("VALIDATION", "Schedule within the next 6 months.", "activeFrom");

        var audience = req.AudienceType == AudienceTypes.Selected ? AudienceTypes.Selected : AudienceTypes.All;
        var users = (req.UserIds ?? new()).Where(i => i > 0).Distinct().ToList();
        if (audience == AudienceTypes.Selected && users.Count == 0 && req.Action == "publish")
            return ApiResponse<object>.Fail("VALIDATION", "Select at least one customer.", "userIds");
        if (users.Count > MarketingRules.MaxSelectedUsers) return ApiResponse<object>.Fail("VALIDATION", $"Select at most {MarketingRules.MaxSelectedUsers:N0} customers (or send to all).");

        b.Type = type; b.Title = title; b.Message = message ?? ""; b.ImageUrl = image;
        b.ButtonLabel = label; b.ButtonUrl = link; b.AudienceType = audience;
        b.SelectedUserIds = audience == AudienceTypes.Selected ? JsonSerializer.Serialize(users) : null;
        b.ShowAsBanner = req.ShowAsBanner; b.ActiveFrom = from; b.ActiveUntil = until;
        return null;
    }
}
