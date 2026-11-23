using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Services.Marketing;

/// <summary>Live throttle state of one channel (also shown in the admin).</summary>
public sealed record ChannelUsage(
    string Channel, int PerMinute, int PerHour, int PerDay, int ProviderDailyLimit, int TransactionalReserve,
    int EffectiveDailyCap, int UsedLastMinute, int UsedLastHour, int UsedLast24h, int TransactionalLast24h,
    DateTime? PausedUntil, string? PauseReason, DateTime NextSlotAt, string? WaitingFor)
{
    public bool CanSendNow(DateTime now) => NextSlotAt <= now;
}

/// <summary>
/// Rolling-window limits per channel, counted from CampaignRecipients.LastAttemptAt (so they survive restarts):
///   • at most PerMinute attempts in any 60 s, evenly spaced (one every 60/PerMinute seconds),
///   • at most PerHour in any 60 min,
///   • at most EffectiveDailyCap in any 24 h, where for email
///       EffectiveDailyCap = min(PerDay, ProviderDailyLimit − max(TransactionalReserve, transactional sends in last 24 h))
///     so OTP / order emails always keep their headroom on the shared Gmail account.
/// When a limit is reached the dispatcher simply waits for the window to free up and carries on.
/// </summary>
public static class ChannelThrottle
{
    /// <summary>Defaults (Gmail free account ≈ 500 / rolling 24 h; WhatsApp Cloud API new-business tier 250 / 24 h).</summary>
    public static MessagingChannelSetting Default(string channel, IConfiguration config)
    {
        int C(string key, int d) => Math.Max(0, config.GetValue($"Marketing:Defaults:{channel}:{key}", d));
        return channel == MessagingChannels.WhatsApp
            ? new MessagingChannelSetting { Channel = channel, PerMinute = C("PerMinute", 20), PerHour = C("PerHour", 250), PerDay = C("PerDay", 250), ProviderDailyLimit = C("ProviderDailyLimit", 250), TransactionalReserve = C("TransactionalReserve", 0) }
            : new MessagingChannelSetting { Channel = channel, PerMinute = C("PerMinute", 20), PerHour = C("PerHour", 100), PerDay = C("PerDay", 400), ProviderDailyLimit = C("ProviderDailyLimit", 500), TransactionalReserve = C("TransactionalReserve", 100) };
    }

    /// <summary>Creates the settings rows on first use.</summary>
    public static async Task<List<MessagingChannelSetting>> EnsureSettingsAsync(AppDbContext db, IConfiguration config, CancellationToken ct = default)
    {
        var rows = await db.MessagingChannelSettings.ToListAsync(ct);
        var added = false;
        foreach (var ch in MessagingChannels.All)
            if (rows.All(r => r.Channel != ch))
            {
                var d = Default(ch, config);
                d.UpdatedAt = DateTime.UtcNow;
                d.UpdatedBy = "default";
                db.MessagingChannelSettings.Add(d);
                rows.Add(d);
                added = true;
            }
        if (added) await db.SaveChangesAsync(ct);
        return rows;
    }

    public static int EffectiveDailyCap(MessagingChannelSetting s, int transactional24h)
    {
        var cap = s.PerDay;
        if (s.ProviderDailyLimit > 0)
            cap = Math.Min(cap, s.ProviderDailyLimit - Math.Max(s.TransactionalReserve, transactional24h));
        return Math.Max(0, cap);
    }

    public static async Task<ChannelUsage> UsageAsync(AppDbContext db, MessagingChannelSetting s, ITransactionalEmailCounter counter, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var dayAgo = now.AddHours(-24);
        // attempts in the last 24 h (a few hundred timestamps at most with sane caps)
        var times = await db.CampaignRecipients.AsNoTracking()
            .Where(r => r.Channel == s.Channel && r.LastAttemptAt != null && r.LastAttemptAt > dayAgo)
            .Select(r => r.LastAttemptAt!.Value).OrderBy(t => t).ToListAsync(ct);

        var tx = s.Channel == MessagingChannels.Email ? counter.CountLast24Hours() : 0;
        var dayCap = EffectiveDailyCap(s, tx);
        var minAgo = now.AddMinutes(-1);
        var hourAgo = now.AddHours(-1);
        var inMinute = times.Where(t => t > minAgo).ToList();
        var inHour = times.Where(t => t > hourAgo).ToList();

        var next = now;
        string? waiting = null;
        void Bump(DateTime t, string why) { if (t > next) { next = t; waiting = why; } }

        if (s.PausedUntil is { } pu && pu > now) Bump(pu, "paused");
        if (s.PerMinute <= 0 || s.PerHour <= 0 || dayCap <= 0)
            Bump(now.AddMinutes(5), dayCap <= 0 && s.PerMinute > 0 && s.PerHour > 0 ? "daily limit (transactional reserve)" : "limit set to 0");
        else
        {
            if (times.Count > 0) Bump(times[^1].AddSeconds(60.0 / s.PerMinute), "spacing");
            if (inMinute.Count >= s.PerMinute) Bump(inMinute[inMinute.Count - s.PerMinute].AddMinutes(1), "per-minute limit");
            if (inHour.Count >= s.PerHour) Bump(inHour[inHour.Count - s.PerHour].AddHours(1), "per-hour limit");
            if (times.Count >= dayCap) Bump(times[times.Count - dayCap].AddHours(24), "daily limit");
        }

        return new ChannelUsage(s.Channel, s.PerMinute, s.PerHour, s.PerDay, s.ProviderDailyLimit, s.TransactionalReserve, dayCap,
            inMinute.Count, inHour.Count, times.Count, tx, s.PausedUntil > now ? s.PausedUntil : null, s.PausedUntil > now ? s.PauseReason : null,
            next, waiting == "spacing" ? null : waiting);
    }

    /// <summary>Rough finish time for <paramref name="pending"/> more sends on this channel (null when nothing pending or blocked indefinitely).</summary>
    public static DateTime? Eta(ChannelUsage u, int pending)
    {
        if (pending <= 0) return null;
        if (u.PerMinute <= 0 || u.PerHour <= 0 || u.EffectiveDailyCap <= 0) return null;
        var now = DateTime.UtcNow;
        var start = u.NextSlotAt > now ? u.NextSlotAt : now;
        var ratePerMin = Math.Min(u.PerMinute, u.PerHour / 60.0);
        var leftToday = Math.Max(0, u.EffectiveDailyCap - u.UsedLast24h);
        if (pending <= leftToday) return start.AddMinutes(pending / ratePerMin);
        if (leftToday == 0 && start > now)
        {
            // blocked by the daily cap: the window starts to reopen at `start`
            var days = (pending - 1) / u.EffectiveDailyCap;
            var last = pending - days * u.EffectiveDailyCap;
            return start.AddHours(24 * days).AddMinutes(last / ratePerMin);
        }
        var rest = pending - leftToday;
        var fullDays = (int)Math.Ceiling(rest / (double)u.EffectiveDailyCap);
        var lastDay = rest - (fullDays - 1) * u.EffectiveDailyCap;
        return start.AddHours(24 * fullDays).AddMinutes(lastDay / ratePerMin);
    }
}
