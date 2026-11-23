using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Services;

/// <summary>
/// Background job: every Orders:UnpaidExpiryCheckSeconds (default 60) cancels online-payment orders still unpaid
/// after Orders:UnpaidExpiryMinutes (default 30; 0 or less disables) and restores their reserved stock.
/// </summary>
public sealed class UnpaidOrderExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<UnpaidOrderExpiryService> _log;
    private readonly IOrderEmails _emails;

    public UnpaidOrderExpiryService(IServiceScopeFactory scopes, IConfiguration config, ILogger<UnpaidOrderExpiryService> log, IOrderEmails emails)
    {
        _emails = emails;
        _scopes = scopes;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var minutes = _config.GetValue<int?>("Orders:UnpaidExpiryMinutes") ?? 30;
            var every = Math.Clamp(_config.GetValue<int?>("Orders:UnpaidExpiryCheckSeconds") ?? 60, 10, 3600);
            if (minutes > 0)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var cancelled = await UnpaidOrderExpiry.RunAsync(db, TimeSpan.FromMinutes(minutes), _log, ct: stoppingToken);
                    foreach (var number in cancelled) _emails.AutoCancelled(number);   // only orders this instance actually claimed
                    if (cancelled.Count > 0)
                        _log.LogInformation("Auto-cancelled {Count} unpaid online order(s) older than {Minutes} min: {Orders}",
                            cancelled.Count, minutes, string.Join(", ", cancelled));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _log.LogError(ex, "Unpaid order expiry run failed"); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(every), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
