using Microsoft.EntityFrameworkCore;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Services.Shipping;

/// <summary>
/// Polls the provider for shipments that are still moving, so tracking keeps working even where webhooks
/// can't reach us (local development, firewall). Interval per partner = TrackingPollMinutes (0 = webhooks only).
/// </summary>
public sealed class ShipmentTrackingService(IServiceScopeFactory scopes, ILogger<ShipmentTrackingService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { await PassAsync(stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Shipment tracking pass failed"); }
            try { await Task.Delay(Tick, stop); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task PassAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accounts = await db.ShippingProviderAccounts.Where(a => a.IsEnabled && !a.IsDeleted && a.TrackingPollMinutes > 0).ToListAsync(ct);
        foreach (var account in accounts)
        {
            var due = DateTime.UtcNow.AddMinutes(-account.TrackingPollMinutes);
            var ids = await db.Shipments
                .Where(s => s.ProviderAccountId == account.Id && !s.IsDeleted && !s.IsCancelled && !s.IsDelivered && s.AwbCode != null
                            && !(s.IsRto && s.Status != null && s.Status.Contains("DELIVERED"))
                            && (s.LastTrackedAt == null || s.LastTrackedAt < due)
                            && s.CreatedAt > DateTime.UtcNow.AddDays(-60))
                .OrderBy(s => s.LastTrackedAt).Select(s => s.Id).Take(40).ToListAsync(ct);
            foreach (var id in ids)
            {
                using var inner = scopes.CreateScope();   // fresh DbContext per shipment: one failure never poisons the rest
                var sdb = inner.ServiceProvider.GetRequiredService<AppDbContext>();
                var svc = inner.ServiceProvider.GetRequiredService<ShippingService>();
                var shipment = await sdb.Shipments.Include(s => s.ProviderAccount).FirstAsync(s => s.Id == id, ct);
                try { await svc.RefreshTrackingAsync(shipment, ct); }
                catch (ShippingProviderException ex)
                {
                    shipment.LastTrackedAt = DateTime.UtcNow;
                    shipment.LastError = ex.Message;
                    await sdb.SaveChangesAsync(ct);
                    if (ex.HttpStatus is 401 or 403) break;   // credentials problem — stop hammering this partner
                }
                await Task.Delay(400, ct);   // stay well under provider rate limits
            }
        }
    }
}
