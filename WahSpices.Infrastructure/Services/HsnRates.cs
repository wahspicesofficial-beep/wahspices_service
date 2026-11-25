using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// GST rates come from the HSN master (Admin → GST &amp; Tax → HSN Master). A product / bill line's HSN code is
/// matched to the most specific active master entry by prefix: "09109100" → "091091" if present, else "0910".
/// Products keep a copy in Product.GstRate (used by orders, invoices and returns); it is refreshed whenever a
/// product is saved or the HSN master changes, so a bill always uses the master's rate.
/// </summary>
public static class HsnRates
{
    public static Task<List<HsnCode>> ActiveAsync(AppDbContext db, CancellationToken ct = default) =>
        db.HsnCodes.AsNoTracking().Where(h => !h.IsDeleted && h.IsActive).ToListAsync(ct);

    public static string Digits(string? code) => new((code ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Most specific master entry whose code is a prefix of <paramref name="code"/> (null when none).</summary>
    public static HsnCode? Resolve(IEnumerable<HsnCode> master, string? code)
    {
        var c = Digits(code);
        if (c.Length < 2) return null;
        return master.Where(h => h.Code.Length > 0 && c.StartsWith(h.Code, StringComparison.Ordinal))
                     .OrderByDescending(h => h.Code.Length).FirstOrDefault();
    }

    public static async Task<decimal?> RateForAsync(AppDbContext db, string? code, CancellationToken ct = default) =>
        Resolve(await ActiveAsync(db, ct), code)?.GstRate;

    /// <summary>Copy the master rate onto every product whose HSN matches. Returns how many products changed.</summary>
    public static async Task<int> ApplyToProductsAsync(AppDbContext db, string updatedBy, CancellationToken ct = default)
    {
        var master = await ActiveAsync(db, ct);
        if (master.Count == 0) return 0;
        var products = await db.Products.Where(p => !p.IsDeleted && p.HsnCode != null && p.HsnCode != "").ToListAsync(ct);
        var changed = 0;
        foreach (var p in products)
        {
            var hit = Resolve(master, p.HsnCode);
            if (hit == null || p.GstRate == hit.GstRate) continue;
            p.GstRate = hit.GstRate;
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedBy = updatedBy;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }
}
