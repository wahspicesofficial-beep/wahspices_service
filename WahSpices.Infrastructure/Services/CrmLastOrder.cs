using Microsoft.EntityFrameworkCore;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// Keeps CrmCustomer.LastOrderDate current. CRM customers are not foreign-keyed to field-sales clients or invoices,
/// so they are matched by GSTIN, then phone (last 10 digits), then e-mail, then exact business name.
/// </summary>
public static class CrmLastOrder
{
    public static string Digits10(string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return d.Length > 10 ? d[^10..] : d;
    }

    /// <summary>Sets LastOrderDate on the matching CRM customer when <paramref name="when"/> is newer. Caller saves.</summary>
    public static async Task TouchAsync(AppDbContext db, string? name, string? phone, string? gstin, string? email, DateTime when)
    {
        var id = await MatchAsync(db, name, phone, gstin, email);
        if (id == null) return;
        var c = await db.CrmCustomers.FirstAsync(x => x.Id == id);
        if (c.LastOrderDate == null || c.LastOrderDate < when) { c.LastOrderDate = when; c.UpdatedAt = DateTime.UtcNow; }
    }

    public static async Task<int?> MatchAsync(AppDbContext db, string? name, string? phone, string? gstin, string? email)
    {
        name = name?.Trim(); gstin = gstin?.Trim(); email = email?.Trim();
        var p10 = Digits10(phone);
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(gstin) && string.IsNullOrEmpty(email) && p10.Length < 10) return null;
        var cands = await db.CrmCustomers.AsNoTracking().Where(c => !c.IsDeleted &&
                ((gstin != null && gstin != "" && c.Gstin == gstin) ||
                 (p10.Length == 10 && c.Phone.Contains(p10.Substring(p10.Length - 4))) ||
                 (email != null && email != "" && c.Email == email) ||
                 (name != null && name != "" && c.Name == name)))
            .Select(c => new { c.Id, c.Name, c.Phone, c.Gstin, c.Email }).Take(500).ToListAsync();
        return (cands.FirstOrDefault(c => !string.IsNullOrEmpty(gstin) && string.Equals(c.Gstin, gstin, StringComparison.OrdinalIgnoreCase))
             ?? cands.FirstOrDefault(c => p10.Length == 10 && Digits10(c.Phone) == p10)
             ?? cands.FirstOrDefault(c => !string.IsNullOrEmpty(email) && string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase))
             ?? cands.FirstOrDefault(c => !string.IsNullOrEmpty(name) && string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))?.Id;
    }

    /// <summary>
    /// One-off / idempotent backfill from existing field-sales orders, field-sales bills and (non-cancelled) invoices.
    /// Only moves dates forward. Returns how many CRM customers were updated.
    /// </summary>
    public static async Task<int> BackfillAsync(AppDbContext db)
    {
        var events = new List<(string? Name, string? Phone, string? Gstin, string? Email, DateTime When)>();
        events.AddRange((await db.SalesOrders.AsNoTracking().Where(o => !o.IsDeleted && o.Status != "Cancelled")
                .GroupBy(o => o.ClientId).Select(g => new { g.Key, When = g.Max(o => o.OrderDate) }).ToListAsync())
            .Join(await db.RetailClients.AsNoTracking().Select(c => new { c.Id, c.Name, c.Phone, c.GstNumber, c.Email }).ToListAsync(),
                o => o.Key, c => c.Id, (o, c) => ((string?)c.Name, c.Phone, c.GstNumber, c.Email, o.When)));
        events.AddRange((await db.SalesOrderBills.AsNoTracking().Where(b => !b.IsDeleted)
                .GroupBy(b => b.ClientId).Select(g => new { g.Key, When = g.Max(b => b.BillDate) }).ToListAsync())
            .Join(await db.RetailClients.AsNoTracking().Select(c => new { c.Id, c.Name, c.Phone, c.GstNumber, c.Email }).ToListAsync(),
                o => o.Key, c => c.Id, (o, c) => ((string?)c.Name, c.Phone, c.GstNumber, c.Email, o.When)));
        events.AddRange((await db.Invoices.AsNoTracking().Where(i => !i.IsDeleted && i.Status != Domain.Enums.InvoiceStatus.Cancelled)
                .GroupBy(i => new { i.CustomerName, i.CustomerPhone, i.CustomerGstin, i.CustomerEmail })
                .Select(g => new { g.Key.CustomerName, g.Key.CustomerPhone, g.Key.CustomerGstin, g.Key.CustomerEmail, When = g.Max(i => i.CreatedAt) })
                .ToListAsync())
            .Select(i => ((string?)i.CustomerName, i.CustomerPhone, i.CustomerGstin, i.CustomerEmail, i.When)));

        var latest = new Dictionary<int, DateTime>();
        foreach (var e in events)
        {
            var id = await MatchAsync(db, e.Name, e.Phone, e.Gstin, e.Email);
            if (id is int k && (!latest.TryGetValue(k, out var d) || d < e.When)) latest[k] = e.When;
        }
        var updated = 0;
        foreach (var (id, when) in latest)
        {
            var c = await db.CrmCustomers.FirstAsync(x => x.Id == id);
            if (c.LastOrderDate == null || c.LastOrderDate < when) { c.LastOrderDate = when; c.UpdatedAt = DateTime.UtcNow; updated++; }
        }
        await db.SaveChangesAsync();
        return updated;
    }
}
