using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Hubs;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Services.Marketing;

/// <summary>What the customer app receives for one broadcast (REST list items and SignalR "notification" payloads).</summary>
public sealed record NotificationDto(
    int Id, string Type, string TypeLabel, string Title, string Message, string? ImageUrl,
    string? ButtonLabel, string? ButtonUrl, bool ShowAsBanner, bool BannerPending,
    DateTime CreatedAt, DateTime? ReadAt);

/// <summary>
/// Publishing, ending and per-user state of in-app broadcasts.
/// Visibility rules:
///  • bell list  = rows with ReadAt == null for live broadcasts (Published or Ended, not deleted, ActiveFrom reached);
///  • banner     = ShowAsBanner, not closed (BannerSeenAt), not read, Published, inside ActiveFrom..ActiveUntil;
///  • late sign-ups get an "All customers" broadcast only while it is still running: before its ActiveUntil, or —
///    when it has no end date — within <see cref="LateJoinerDays"/> days of publishing.
/// </summary>
public sealed class BroadcastService
{
    public const int LateJoinerDays = 7;

    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly CampaignTypeLabels _labels;
    private readonly ILogger<BroadcastService> _log;

    public BroadcastService(AppDbContext db, IHubContext<NotificationsHub> hub, CampaignTypeLabels labels, ILogger<BroadcastService> log)
    {
        _db = db; _hub = hub; _labels = labels; _log = log;
    }

    // ── Admin side ───────────────────────────────────────────────────────────

    /// <summary>Marks the broadcast Published, creates the per-user rows and pushes it to connected customers.</summary>
    public async Task<int> PublishAsync(Broadcast b, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        b.Status = BroadcastStatuses.Published;
        b.PublishedAt = now;
        if (b.ActiveFrom == null || b.ActiveFrom > now) b.ActiveFrom = now;
        b.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        int created;
        if (b.AudienceType == AudienceTypes.All)
        {
            // one set-based statement — fine for tens of thousands of customers
            created = await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO BroadcastRecipients (BroadcastId, UserId, DeliveredAt)
SELECT {b.Id}, u.Id, {now} FROM Users u
WHERE u.IsDeleted = 0 AND u.IsActive = 1 AND u.Role = {(byte)UserRole.Customer}
  AND NOT EXISTS (SELECT 1 FROM BroadcastRecipients r WHERE r.BroadcastId = {b.Id} AND r.UserId = u.Id)", ct);
        }
        else
        {
            var ids = MarketingRules.ParseIds(b.SelectedUserIds);
            created = 0;
            foreach (var chunk in ids.Chunk(1000))
            {
                var valid = await _db.Users.AsNoTracking()
                    .Where(u => chunk.Contains(u.Id) && u.IsActive && u.Role == UserRole.Customer)
                    .Select(u => u.Id).ToListAsync(ct);
                var existing = await _db.BroadcastRecipients.AsNoTracking()
                    .Where(r => r.BroadcastId == b.Id && chunk.Contains(r.UserId)).Select(r => r.UserId).ToListAsync(ct);
                var add = valid.Except(existing).Select(uid => new BroadcastRecipient { BroadcastId = b.Id, UserId = uid, DeliveredAt = now }).ToList();
                _db.BroadcastRecipients.AddRange(add);
                await _db.SaveChangesAsync(ct);
                created += add.Count;
            }
        }

        await PushNewAsync(b, ct);
        _log.LogInformation("Broadcast {Id} \"{Title}\" published to {Count} customer(s)", b.Id, b.Title, created);
        return created;
    }

    public async Task EndAsync(Broadcast b, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        b.Status = BroadcastStatuses.Ended;
        b.EndedAt = now;
        b.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await SafePush(() => _hub.Clients.Group(NotificationsHub.AllCustomersGroup).SendAsync("broadcast-ended", new { id = b.Id }, ct));
    }

    public async Task RemoveAsync(Broadcast b, string? by, CancellationToken ct = default)
    {
        b.IsDeleted = true;
        b.UpdatedAt = DateTime.UtcNow;
        b.UpdatedBy = by;
        await _db.SaveChangesAsync(ct);
        await SafePush(() => _hub.Clients.Group(NotificationsHub.AllCustomersGroup).SendAsync("broadcast-removed", new { id = b.Id }, ct));
    }

    private async Task PushNewAsync(Broadcast b, CancellationToken ct)
    {
        var dto = await ToDtoAsync(b, bannerPending: b.ShowAsBanner, readAt: null);
        if (b.AudienceType == AudienceTypes.All)
        {
            await SafePush(() => _hub.Clients.Group(NotificationsHub.AllCustomersGroup).SendAsync("notification", dto, ct));
            return;
        }
        var ids = await _db.BroadcastRecipients.AsNoTracking().Where(r => r.BroadcastId == b.Id).Select(r => r.UserId).ToListAsync(ct);
        foreach (var chunk in ids.Chunk(200))
            await SafePush(() => _hub.Clients.Groups(chunk.Select(NotificationsHub.UserGroup).ToList()).SendAsync("notification", dto, ct));
    }

    private async Task SafePush(Func<Task> send)
    {
        try { await send(); }
        catch (Exception ex) { _log.LogWarning(ex, "Real-time push failed (clients will pick it up by polling)"); }
    }

    // ── Customer side ────────────────────────────────────────────────────────

    /// <summary>Late sign-ups: attach still-running "All customers" broadcasts published before the account existed.</summary>
    public async Task EnsureLateJoinerRowsAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.CreatedAt, u.IsActive }).FirstOrDefaultAsync(ct);
        if (user == null || !user.IsActive) return;
        var now = DateTime.UtcNow;
        var lateCutoff = now.AddDays(-LateJoinerDays);
        var missing = await _db.Broadcasts.AsNoTracking()
            .Where(b => b.Status == BroadcastStatuses.Published && b.AudienceType == AudienceTypes.All
                        && b.PublishedAt != null && b.PublishedAt < user.CreatedAt
                        && (b.ActiveUntil != null ? b.ActiveUntil > now : b.PublishedAt > lateCutoff)
                        && !_db.BroadcastRecipients.Any(r => r.BroadcastId == b.Id && r.UserId == userId))
            .Select(b => b.Id).Take(20).ToListAsync(ct);
        if (missing.Count == 0) return;
        _db.BroadcastRecipients.AddRange(missing.Select(id => new BroadcastRecipient { BroadcastId = id, UserId = userId, DeliveredAt = now }));
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* another tab attached it at the same moment (unique index) */ }
    }

    /// <summary>Rows the user may see in the bell (unread unless includeRead).</summary>
    public IQueryable<BroadcastRecipient> Visible(int userId, bool includeRead = false)
    {
        var now = DateTime.UtcNow;
        var q = _db.BroadcastRecipients.AsNoTracking().Include(r => r.Broadcast)
            .Where(r => r.UserId == userId && r.Broadcast != null && !r.Broadcast.IsDeleted
                        && (r.Broadcast.Status == BroadcastStatuses.Published || r.Broadcast.Status == BroadcastStatuses.Ended)
                        && (r.Broadcast.ActiveFrom == null || r.Broadcast.ActiveFrom <= now));
        return includeRead ? q : q.Where(r => r.ReadAt == null);
    }

    /// <summary>Banners still to show the user (oldest first).</summary>
    public IQueryable<BroadcastRecipient> PendingBanners(int userId)
    {
        var now = DateTime.UtcNow;
        return Visible(userId).Where(r => r.Broadcast!.ShowAsBanner && r.BannerSeenAt == null
                                          && r.Broadcast.Status == BroadcastStatuses.Published
                                          && (r.Broadcast.ActiveUntil == null || r.Broadcast.ActiveUntil > now));
    }

    public async Task<NotificationDto> ToDtoAsync(Broadcast b, bool bannerPending, DateTime? readAt) =>
        new(b.Id, b.Type, await _labels.LabelAsync(b.Type), b.Title, b.Message, b.ImageUrl, b.ButtonLabel, b.ButtonUrl,
            b.ShowAsBanner, bannerPending, b.PublishedAt ?? b.CreatedAt, readAt);

    public async Task<List<NotificationDto>> ToDtosAsync(IEnumerable<BroadcastRecipient> rows)
    {
        var now = DateTime.UtcNow;
        var list = new List<NotificationDto>();
        foreach (var r in rows)
        {
            var b = r.Broadcast!;
            var bannerPending = b.ShowAsBanner && r.BannerSeenAt == null && r.ReadAt == null && b.Status == BroadcastStatuses.Published
                                && (b.ActiveUntil == null || b.ActiveUntil > now);
            list.Add(await ToDtoAsync(b, bannerPending, r.ReadAt));
        }
        return list;
    }

    public Task PushStateAsync(int userId, object payload) =>
        SafePush(() => _hub.Clients.Group(NotificationsHub.UserGroup(userId)).SendAsync("notification-state", payload));
}
