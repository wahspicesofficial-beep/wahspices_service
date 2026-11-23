using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Services;

public sealed record CompanyAlertRunResult(int Sent, string? Recipient, List<ComplianceAlertLine> Items, string? Skipped);

/// <summary>
/// Licence / warranty renewal alerts. Once a day (after CompanyAlerts:SendHourIst, default 9 IST) emails the store inbox
/// (SiteSettings.Email, or CompanyAlerts:Recipient when set) one branded digest listing every licence that has reached a
/// 30 / 15 / 7 / 1-day threshold or expired, plus asset warranties ending within 30 days. Each item + expiry date + threshold
/// is emailed once (CompanyAlertLogs). CompanyAlerts:Enabled=false switches the job off. Also seeds the module's master lists.
/// </summary>
public sealed class CompanyAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<CompanyAlertService> _log;
    private DateTime _lastRunDay = DateTime.MinValue;

    public CompanyAlertService(IServiceScopeFactory scopes, IConfiguration config, ILogger<CompanyAlertService> log)
    {
        _scopes = scopes; _config = config; _log = log;
    }

    public static readonly Dictionary<string, (string Value, string Label)[]> MasterLists = new()
    {
        ["CompanyAssetCategory"] = new[]
        {
            ("Computer", "Computers & laptops"), ("Printer", "Printers & scanners"), ("StampSeal", "Stamps & seals"), ("Furniture", "Furniture"),
            ("Machine", "Machines & equipment"), ("Electronics", "Electronics & appliances"), ("Mobile", "Mobile phones"), ("Vehicle", "Vehicles"), ("Other", "Other"),
        },
        ["LicenceType"] = new[]
        {
            ("FSSAI", "FSSAI food licence"), ("GST", "GST registration"), ("Udyam", "Udyam (MSME) registration"), ("TradeLicence", "Trade licence"),
            ("ShopEstablishment", "Shop & Establishment"), ("Trademark", "Trademark"), ("WeightsMeasures", "Weights & Measures (Legal Metrology)"),
            ("FireNoc", "Fire NOC"), ("PollutionNoc", "Pollution control NOC"), ("IEC", "Import Export Code (IEC)"), ("Other", "Other"),
        },
    };

    /// <summary>Adds the module's master-data lists the first time they're missing (never touches admin edits).</summary>
    public static async Task SeedAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        try
        {
            var existing = await db.MasterDataValues.IgnoreQueryFilters().Where(m => MasterLists.Keys.Contains(m.Type)).Select(m => m.Type).Distinct().ToListAsync(ct);
            var added = 0;
            foreach (var (type, values) in MasterLists)
            {
                if (existing.Contains(type)) continue;
                var i = 0;
                foreach (var (value, label) in values)
                {
                    db.MasterDataValues.Add(new MasterDataValue { Type = type, Value = value, Label = label, SortOrder = i++, IsActive = true, CreatedBy = "seed" });
                    added++;
                }
            }
            if (added > 0) { await db.SaveChangesAsync(ct); log.LogInformation("Seeded {Count} company master-data values", added); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Company master-data seeding skipped"); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        using (var scope = _scopes.CreateScope())
            await SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), _log, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var enabled = _config.GetValue<bool?>("CompanyAlerts:Enabled") ?? true;
            var hour = Math.Clamp(_config.GetValue<int?>("CompanyAlerts:SendHourIst") ?? 9, 0, 23);
            var nowIst = EmailLayout.ToIst(DateTime.UtcNow);
            if (enabled && nowIst.Date != _lastRunDay && nowIst.Hour >= hour)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var r = await RunAsync(scope.ServiceProvider, _config, _log, stoppingToken);
                    _lastRunDay = nowIst.Date;   // a failed send is retried next hour (nothing logged as sent)
                    if (r.Sent > 0) _log.LogInformation("Company renewal alert emailed to {To}: {Count} item(s)", r.Recipient, r.Sent);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _log.LogError(ex, "Company renewal alert run failed"); }
            }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(_config.GetValue<int?>("CompanyAlerts:CheckMinutes") ?? 60, 1, 720)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Finds items whose current threshold hasn't been emailed yet, sends one digest, and logs them. Safe to call any time.</summary>
    public static async Task<CompanyAlertRunResult> RunAsync(IServiceProvider sp, IConfiguration config, ILogger log, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var today = CompanyFinance.TodayIst();
        var alerts = await CompanyFinance.CurrentAlertsAsync(db, today);

        var due = new List<(AlertItem Item, int Threshold)>();
        foreach (var a in alerts)
        {
            var t = CompanyFinance.CurrentThreshold(a.DaysLeft, a.ReminderDays);
            if (t == null) continue;
            var exp = a.ExpiryDate.Date;
            var sent = await db.CompanyAlertLogs.AnyAsync(l => l.Kind == a.Kind && l.RefId == a.Id && l.ExpiryDate == exp && l.Threshold == t.Value && !l.IsDeleted, ct);
            if (!sent) due.Add((a, t.Value));
        }
        var lines = due.Select(d => new ComplianceAlertLine(d.Item.Kind, d.Item.Title, LabelFor(db, d.Item), d.Item.Number, d.Item.ExpiryDate, d.Item.DaysLeft)).ToList();
        if (due.Count == 0) return new(0, null, lines, "Nothing new to send.");

        var to = config["CompanyAlerts:Recipient"];
        if (string.IsNullOrWhiteSpace(to))
            to = await db.SiteSettings.AsNoTracking().Where(s => !s.IsDeleted).OrderBy(s => s.Id).Select(s => s.Email).FirstOrDefaultAsync(ct);
        if (!EmailAddressRules.IsDeliverable(to)) return new(0, null, lines, "No store email set in Admin → Settings.");

        var composer = sp.GetRequiredService<IEmailComposer>();
        await composer.SendNowAsync(to!, "WAH! Spices", "company-renewal-alert", b => CompanyAlertEmails.ComplianceAlert(b, lines), ct);

        foreach (var (item, t) in due)
            db.CompanyAlertLogs.Add(new CompanyAlertLog { Kind = item.Kind, RefId = item.Id, ExpiryDate = item.ExpiryDate.Date, Threshold = t, SentAt = DateTime.UtcNow, Recipient = to, CreatedBy = "alert-job" });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { log.LogWarning(ex, "Alert log save conflict (another instance sent the same alert)"); }
        return new(due.Count, to, lines, null);
    }

    private static string LabelFor(AppDbContext db, AlertItem a)
    {
        var type = a.Kind == "Licence" ? "LicenceType" : "CompanyAssetCategory";
        var label = db.MasterDataValues.AsNoTracking().Where(m => m.Type == type && m.Value == a.Category).Select(m => m.Label).FirstOrDefault();
        return label ?? a.Category;
    }
}
