using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Messaging;

namespace WahSpices.API.Services.Marketing;

/// <summary>
/// Background worker for customer communications:
///  • starts scheduled campaigns and publishes scheduled broadcasts when their time comes; ends expired broadcasts;
///  • drains Pending campaign recipients one at a time per channel, never faster than the channel limits
///    (<see cref="ChannelThrottle"/>). When a limit is hit it sleeps until the window frees up, so "send to everyone"
///    to thousands of customers spreads itself over as many days as the account allows;
///  • retries transient failures with back-off (2 min, 10 min, 30 min; 3 attempts), marks permanent ones Failed,
///    and stops the whole channel when the provider reports a quota / account problem (Gmail "5.4.5 Daily user
///    sending quota exceeded" → paused 24 h; the rows stay Pending and continue automatically afterwards).
/// Runs in-process: with several API instances only one should run it (Marketing:Dispatcher:Enabled=false elsewhere).
/// </summary>
public sealed class MarketingDispatcherService : BackgroundService
{
    private static readonly TimeSpan[] Backoff = { TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30) };
    private const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<MarketingDispatcherService> _log;
    private DateTime _lastHousekeeping = DateTime.MinValue;

    public MarketingDispatcherService(IServiceScopeFactory scopes, IConfiguration config, ILogger<MarketingDispatcherService> log)
    {
        _scopes = scopes; _config = config; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Marketing:Dispatcher:Enabled", true))
        {
            _log.LogInformation("Marketing dispatcher disabled (Marketing:Dispatcher:Enabled=false)");
            return;
        }
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { return; }
        await RecoverAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sleep = TimeSpan.FromSeconds(5);
            try
            {
                if (DateTime.UtcNow - _lastHousekeeping > TimeSpan.FromSeconds(5))
                {
                    await HousekeepingAsync(stoppingToken);
                    _lastHousekeeping = DateTime.UtcNow;
                }
                foreach (var channel in MessagingChannels.All)
                {
                    var wait = await TickChannelAsync(channel, stoppingToken);
                    if (wait < sleep) sleep = wait;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Marketing dispatcher loop error");
                sleep = TimeSpan.FromSeconds(15);
            }
            if (sleep < TimeSpan.FromMilliseconds(250)) sleep = TimeSpan.FromMilliseconds(250);
            try { await Task.Delay(sleep, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>After a crash / restart: rows left "in flight" go back to Pending (worst case one duplicate email).</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ChannelThrottle.EnsureSettingsAsync(db, _config, ct);
            var n = await db.CampaignRecipients.Where(r => r.Status == RecipientStatuses.Sending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Pending), ct);
            if (n > 0) _log.LogWarning("Marketing dispatcher: {Count} in-flight recipient(s) reset to Pending after restart", n);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Marketing dispatcher start-up recovery skipped (tables missing?)"); }
    }

    private async Task HousekeepingAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        // scheduled campaigns → start
        var due = await db.MarketingCampaigns.Where(c => c.Status == CampaignStatuses.Scheduled && c.ScheduledAt != null && c.ScheduledAt <= now)
            .OrderBy(c => c.ScheduledAt).Take(5).ToListAsync(ct);
        if (due.Count > 0)
        {
            var campaigns = scope.ServiceProvider.GetRequiredService<CampaignService>();
            foreach (var c in due) await campaigns.StartAsync(c, ct);
        }

        // sending campaigns with nothing left → completed
        var sendingIds = await db.MarketingCampaigns.Where(c => c.Status == CampaignStatuses.Sending).Select(c => c.Id).ToListAsync(ct);
        if (sendingIds.Count > 0)
        {
            var svc = scope.ServiceProvider.GetRequiredService<CampaignService>();
            foreach (var id in sendingIds) await svc.CompleteIfDoneAsync(id, ct);
        }

        // scheduled broadcasts → publish; expired → ended
        var broadcasts = scope.ServiceProvider.GetRequiredService<BroadcastService>();
        var dueB = await db.Broadcasts.Where(b => b.Status == BroadcastStatuses.Scheduled && b.ActiveFrom != null && b.ActiveFrom <= now)
            .OrderBy(b => b.ActiveFrom).Take(5).ToListAsync(ct);
        foreach (var b in dueB) await broadcasts.PublishAsync(b, ct);
        var expired = await db.Broadcasts.Where(b => b.Status == BroadcastStatuses.Published && b.ActiveUntil != null && b.ActiveUntil <= now)
            .Take(20).ToListAsync(ct);
        foreach (var b in expired) await broadcasts.EndAsync(b, ct);
    }

    /// <summary>Sends at most one message on the channel if the limits allow. Returns how long to wait before the next check.</summary>
    private async Task<TimeSpan> TickChannelAsync(string channel, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var nextId = await db.CampaignRecipients.AsNoTracking()
            .Where(r => r.Channel == channel && r.Status == RecipientStatuses.Pending && (r.NextAttemptAt == null || r.NextAttemptAt <= now)
                        && r.Campaign!.Status == CampaignStatuses.Sending && !r.Campaign.IsDeleted)
            .OrderBy(r => r.CampaignId).ThenBy(r => r.Id).Select(r => (long?)r.Id).FirstOrDefaultAsync(ct);
        if (nextId == null) return TimeSpan.FromSeconds(5);

        if (channel == MessagingChannels.Email && !EmailConfigured()) return TimeSpan.FromSeconds(30);
        if (channel == MessagingChannels.WhatsApp && !sp.GetRequiredService<IWhatsAppSender>().IsConfigured) return TimeSpan.FromSeconds(30);

        var settings = (await ChannelThrottle.EnsureSettingsAsync(db, _config, ct)).First(s => s.Channel == channel);
        var usage = await ChannelThrottle.UsageAsync(db, settings, sp.GetRequiredService<ITransactionalEmailCounter>(), ct);
        now = DateTime.UtcNow;
        if (!usage.CanSendNow(now))
        {
            var wait = usage.NextSlotAt - now;
            return wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
        }

        // claim the row (guards against a second worker / a cancel in between)
        var claimed = await db.CampaignRecipients.Where(r => r.Id == nextId && r.Status == RecipientStatuses.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Sending)
                                      .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                                      .SetProperty(r => r.LastAttemptAt, now), ct);
        if (claimed == 0) return TimeSpan.Zero;

        var row = await db.CampaignRecipients.AsNoTracking().Include(r => r.Campaign).FirstAsync(r => r.Id == nextId, ct);
        SendOutcome outcome;
        try
        {
            outcome = await SendAsync(sp, db, row, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = new SendOutcome(SendOutcomeKind.Transient, ex.Message);
        }
        await RecordAsync(db, row, settings, outcome, ct);
        return TimeSpan.Zero;
    }

    private bool EmailConfigured()
    {
        var mode = _config["Email:DeliveryMode"];
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(mode, "Outbox", StringComparison.OrdinalIgnoreCase)) return true;
        var u = _config["Email:Username"]; var p = _config["Email:Password"];
        return !string.IsNullOrWhiteSpace(u) && !string.IsNullOrWhiteSpace(p) && !p.StartsWith("SET-", StringComparison.OrdinalIgnoreCase) && u != "YOUR_GMAIL@gmail.com";
    }

    private async Task<SendOutcome> SendAsync(IServiceProvider sp, AppDbContext db, CampaignRecipient row, CancellationToken ct)
    {
        var c = row.Campaign!;
        // re-check consent at send time (the customer may have unsubscribed since the campaign started)
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == row.UserId)
            .Select(u => new { u.MarketingOptOut, u.IsActive, u.IsDeleted, u.Name }).FirstOrDefaultAsync(ct);
        if (user == null || user.IsDeleted || !user.IsActive) return new SendOutcome(SendOutcomeKind.Permanent, "skip:Account disabled or deleted");
        if (user.MarketingOptOut) return new SendOutcome(SendOutcomeKind.Permanent, "skip:Opted out of marketing");

        var content = sp.GetRequiredService<CampaignContent>();
        if (row.Channel == MessagingChannels.Email)
        {
            var msg = await content.BuildEmailAsync(c, row.UserId, row.Address, row.Name, ct);
            try
            {
                await sp.GetRequiredService<IEmailService>().SendAsync(msg, ct);
                return SendOutcome.Ok;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ClassifySmtp(ex);
            }
        }

        var wa = sp.GetRequiredService<IWhatsAppSender>();
        var data = await content.BuildWhatsAppAsync(c, row.Name, ct);
        if (wa.RequiresImage && string.IsNullOrWhiteSpace(data.ImageUrl))
            return new SendOutcome(SendOutcomeKind.Permanent, "skip:The WhatsApp template needs an image and this campaign has none");
        return await wa.SendTemplateAsync(row.Address, data, ct);
    }

    /// <summary>Gmail / SMTP replies → retry, fail, or stop the channel.</summary>
    public static SendOutcome ClassifySmtp(Exception ex)
    {
        var text = ex.ToString();
        bool Has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);
        var shortMsg = (ex.InnerException?.Message ?? ex.Message);
        if (shortMsg.Length > 300) shortMsg = shortMsg[..300];

        if (Has("5.4.5") || Has("sending quota") || Has("Daily user sending") || Has("sending limit exceeded") || Has("5.7.1 Daily"))
            return new SendOutcome(SendOutcomeKind.ChannelBlocked, "Gmail daily sending quota exceeded: " + shortMsg, TimeSpan.FromHours(24));
        if (ex is SmtpFailedRecipientException fr && fr.StatusCode is SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed or SmtpStatusCode.UserNotLocalTryAlternatePath or SmtpStatusCode.UserNotLocalWillForward)
            return new SendOutcome(SendOutcomeKind.Permanent, shortMsg);
        if (ex is SmtpException se)
        {
            if (Has("5.7.8") || Has("5.7.0 Authentication") || Has("Username and Password not accepted") || se.StatusCode == SmtpStatusCode.ClientNotPermitted)
                return new SendOutcome(SendOutcomeKind.ChannelBlocked, "SMTP login rejected — check Email:Username / Email:Password: " + shortMsg, TimeSpan.FromHours(1));
            if (se.StatusCode == SmtpStatusCode.ServiceNotAvailable && (Has("4.7.0") || Has("4.7.28") || Has("rate") || Has("Try again later")))
                return new SendOutcome(SendOutcomeKind.ChannelBlocked, "Gmail asked to slow down: " + shortMsg, TimeSpan.FromMinutes(15));
            return se.StatusCode switch
            {
                SmtpStatusCode.ServiceNotAvailable or SmtpStatusCode.MailboxBusy or SmtpStatusCode.LocalErrorInProcessing
                    or SmtpStatusCode.InsufficientStorage or SmtpStatusCode.GeneralFailure or SmtpStatusCode.TransactionFailed
                    => new SendOutcome(SendOutcomeKind.Transient, shortMsg),
                _ when (int)se.StatusCode >= 500 => new SendOutcome(SendOutcomeKind.Permanent, shortMsg),
                _ => new SendOutcome(SendOutcomeKind.Transient, shortMsg),
            };
        }
        if (ex is IOException or SocketException or TimeoutException || ex.InnerException is IOException or SocketException)
            return new SendOutcome(SendOutcomeKind.Transient, shortMsg);
        if (ex is FormatException) return new SendOutcome(SendOutcomeKind.Permanent, "Invalid address: " + shortMsg);
        return new SendOutcome(SendOutcomeKind.Transient, shortMsg);
    }

    private async Task RecordAsync(AppDbContext db, CampaignRecipient row, MessagingChannelSetting settings, SendOutcome o, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var q = db.CampaignRecipients.Where(r => r.Id == row.Id);
        var attempts = row.Attempts;   // already incremented by the claim
        var err = o.Error is { Length: > 500 } e ? e[..500] : o.Error;

        switch (o.Kind)
        {
            case SendOutcomeKind.Sent:
                await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Sent).SetProperty(r => r.SentAt, now)
                    .SetProperty(r => r.LastError, (string?)null).SetProperty(r => r.NextAttemptAt, (DateTime?)null), ct);
                break;

            case SendOutcomeKind.Permanent when err != null && err.StartsWith("skip:"):
                var reason = err[5..];
                // nothing was sent → don't count it against the limits
                await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Skipped).SetProperty(r => r.LastError, reason)
                    .SetProperty(r => r.LastAttemptAt, (DateTime?)null).SetProperty(r => r.Attempts, r => r.Attempts - 1), ct);
                break;

            case SendOutcomeKind.Permanent:
                await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Failed).SetProperty(r => r.LastError, err), ct);
                _log.LogWarning("Campaign {Campaign} → {Channel} recipient {Recipient} failed permanently: {Error}", row.CampaignId, row.Channel, row.Id, err);
                break;

            case SendOutcomeKind.ChannelBlocked:
                // the message did not go out: put it back (attempt not counted) and stop the channel for a while
                await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Pending).SetProperty(r => r.LastError, err)
                    .SetProperty(r => r.Attempts, r => r.Attempts - 1).SetProperty(r => r.LastAttemptAt, (DateTime?)null), ct);
                var until = now + (o.PauseFor ?? TimeSpan.FromHours(1));
                await db.MessagingChannelSettings.Where(x => x.Id == settings.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PausedUntil, until).SetProperty(x => x.PauseReason, err)
                        .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, "dispatcher"), ct);
                _log.LogError("{Channel} campaigns paused until {Until:u}: {Error}", row.Channel, until, err);
                break;

            default: // Transient
                if (attempts >= MaxAttempts)
                {
                    await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Failed).SetProperty(r => r.LastError, err), ct);
                    _log.LogWarning("Campaign {Campaign} → {Channel} recipient {Recipient} failed after {Attempts} attempts: {Error}", row.CampaignId, row.Channel, row.Id, attempts, err);
                }
                else
                {
                    var next = now + Backoff[Math.Clamp(attempts - 1, 0, Backoff.Length - 1)];
                    await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatuses.Pending).SetProperty(r => r.LastError, err)
                        .SetProperty(r => r.NextAttemptAt, next), ct);
                }
                break;
        }
    }
}
