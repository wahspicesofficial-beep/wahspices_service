using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Services.Marketing;

/// <summary>Per-status recipient counts for one campaign (all channels or one).</summary>
public sealed record RecipientCounts(int Total, int Pending, int Sending, int Sent, int Failed, int Skipped, int Cancelled)
{
    public static readonly RecipientCounts Empty = new(0, 0, 0, 0, 0, 0, 0);

    public static RecipientCounts From(IEnumerable<(string Status, int Count)> rows)
    {
        int Get(string s) => rows.Where(r => r.Status == s).Sum(r => r.Count);
        var all = rows.Sum(r => r.Count);
        return new RecipientCounts(all, Get(RecipientStatuses.Pending), Get(RecipientStatuses.Sending), Get(RecipientStatuses.Sent),
            Get(RecipientStatuses.Failed), Get(RecipientStatuses.Skipped), Get(RecipientStatuses.Cancelled));
    }
}

/// <summary>Campaign life cycle: start (materialise recipients), pause, resume, cancel, retry failed.</summary>
public sealed class CampaignService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CampaignService> _log;

    public CampaignService(AppDbContext db, ILogger<CampaignService> log) { _db = db; _log = log; }

    /// <summary>
    /// Creates one CampaignRecipient per user × channel (ineligible → Skipped with the reason) and switches the
    /// campaign to Sending. The dispatcher then drains Pending rows under the channel limits. Idempotent.
    /// </summary>
    public async Task<int> StartAsync(MarketingCampaign c, CancellationToken ct = default)
    {
        var selected = c.AudienceType == AudienceTypes.Selected ? MarketingRules.ParseIds(c.SelectedUserIds) : null;
        var users = await Audience.LoadAsync(_db, c.AudienceType, selected, ct);
        var plan = Audience.Plan(users, c.SendEmail, c.SendWhatsApp);

        var existing = (await _db.CampaignRecipients.AsNoTracking().Where(r => r.CampaignId == c.Id)
            .Select(r => new { r.UserId, r.Channel }).ToListAsync(ct))
            .Select(x => (x.UserId, x.Channel)).ToHashSet();

        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var chunk in plan.Where(p => !existing.Contains((p.UserId, p.Channel))).Chunk(500))
        {
            _db.CampaignRecipients.AddRange(chunk.Select(p => new CampaignRecipient
            {
                CampaignId = c.Id, UserId = p.UserId, Channel = p.Channel, Address = p.Address, Name = p.Name,
                Status = p.SkipReason == null ? RecipientStatuses.Pending : RecipientStatuses.Skipped,
                LastError = p.SkipReason, CreatedAt = now,
            }));
            await _db.SaveChangesAsync(ct);
            foreach (var e in _db.ChangeTracker.Entries<CampaignRecipient>().ToList()) e.State = EntityState.Detached;   // keep the tracker small
            added += chunk.Length;
        }

        var tracked = _db.Entry(c).State == EntityState.Detached ? await _db.MarketingCampaigns.FirstAsync(x => x.Id == c.Id, ct) : c;
        tracked.Status = CampaignStatuses.Sending;
        tracked.StartedAt ??= now;
        tracked.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        c.Status = tracked.Status; c.StartedAt = tracked.StartedAt;
        _log.LogInformation("Campaign {Id} \"{Title}\" started: {Count} recipient row(s)", c.Id, c.Title, added);
        await CompleteIfDoneAsync(c.Id, ct);
        return added;
    }

    public async Task PauseAsync(MarketingCampaign c)
    {
        c.Status = CampaignStatuses.Paused;
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ResumeAsync(MarketingCampaign c)
    {
        var started = await _db.CampaignRecipients.AnyAsync(r => r.CampaignId == c.Id);
        if (!started && c.ScheduledAt > DateTime.UtcNow)
        {
            c.Status = CampaignStatuses.Scheduled;
            c.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return;
        }
        if (!started) { await StartAsync(c); return; }
        c.Status = CampaignStatuses.Sending;
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await CompleteIfDoneAsync(c.Id);
    }

    public async Task<int> CancelAsync(MarketingCampaign c)
    {
        var now = DateTime.UtcNow;
        c.Status = CampaignStatuses.Cancelled;
        c.CancelledAt = now;
        c.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return await _db.CampaignRecipients.Where(r => r.CampaignId == c.Id && r.Status == RecipientStatuses.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Cancelled).SetProperty(r => r.NextAttemptAt, (DateTime?)null));
    }

    /// <summary>Failed rows (optionally only some) go back to Pending with a fresh attempt budget.</summary>
    public async Task<int> RetryFailedAsync(MarketingCampaign c, IReadOnlyCollection<long>? ids = null)
    {
        var q = _db.CampaignRecipients.Where(r => r.CampaignId == c.Id && r.Status == RecipientStatuses.Failed);
        if (ids is { Count: > 0 }) q = q.Where(r => ids.Contains(r.Id));
        var n = await q.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RecipientStatuses.Pending)
            .SetProperty(r => r.Attempts, 0)
            .SetProperty(r => r.NextAttemptAt, (DateTime?)null)
            .SetProperty(r => r.LastError, (string?)null));
        if (n > 0 && c.Status is CampaignStatuses.Completed)
        {
            c.Status = CampaignStatuses.Sending;
            c.CompletedAt = null;
            c.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return n;
    }

    /// <summary>Sending → Completed once nothing is Pending / in flight.</summary>
    public async Task<bool> CompleteIfDoneAsync(int campaignId, CancellationToken ct = default)
    {
        var open = await _db.CampaignRecipients.AnyAsync(r => r.CampaignId == campaignId
            && (r.Status == RecipientStatuses.Pending || r.Status == RecipientStatuses.Sending), ct);
        if (open) return false;
        var now = DateTime.UtcNow;
        var n = await _db.MarketingCampaigns.Where(c => c.Id == campaignId && c.Status == CampaignStatuses.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatuses.Completed)
                                      .SetProperty(c => c.CompletedAt, now).SetProperty(c => c.UpdatedAt, now), ct);
        return n > 0;
    }

    public static async Task<Dictionary<int, Dictionary<string, RecipientCounts>>> CountsAsync(AppDbContext db, IReadOnlyCollection<int> campaignIds)
    {
        var rows = await db.CampaignRecipients.AsNoTracking().Where(r => campaignIds.Contains(r.CampaignId))
            .GroupBy(r => new { r.CampaignId, r.Channel, r.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Channel, g.Key.Status, Count = g.Count() }).ToListAsync();
        return rows.GroupBy(r => r.CampaignId).ToDictionary(
            g => g.Key,
            g =>
            {
                var d = g.GroupBy(x => x.Channel).ToDictionary(cg => cg.Key, cg => RecipientCounts.From(cg.Select(x => (x.Status, x.Count))));
                d["All"] = RecipientCounts.From(g.Select(x => (x.Status, x.Count)));
                return d;
            });
    }
}
