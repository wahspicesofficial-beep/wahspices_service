using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Services;

/// <summary>
/// Who may approve an expense: only an admin whose login is linked to an active partner (Company → Partners),
/// and never the admin who added that expense. Used by Company → Expenses &amp; split and HR → Expenses alike.
/// </summary>
public static class ExpenseApproval
{
    public sealed record Approver(int AdminId, string AdminName, Partner? Partner);

    public static async Task<Approver> ForAsync(AppDbContext db, string adminIdClaim, string adminName)
    {
        var id = int.TryParse(adminIdClaim, out var v) ? v : 0;
        // The Super Admin runs the panel but never approves partners' expenses, even if a partner were linked.
        var admin = id == 0 ? null : await db.AdminUsers.AsNoTracking().Include(a => a.AdminRole).FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        var partner = admin == null || WahSpices.API.Authorization.AdminPermissions.IsSuperAdmin(admin) ? null
            : await db.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.AdminUserId == id && p.IsActive && !p.IsDeleted);
        return new Approver(id, adminName, partner);
    }

    /// <summary>True when this admin added the expense (older rows only have the creator's name).</summary>
    public static bool IsCreator(ExpenseEntry e, Approver a) =>
        e.CreatedByAdminId != null ? e.CreatedByAdminId == a.AdminId
                                   : string.Equals(e.CreatedBy, a.AdminName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when the approver may approve; otherwise (code, message) for the 4xx response.</summary>
    public static (string Code, string Message)? Check(ExpenseEntry e, Approver a)
    {
        if (e.IsApproved) return ("ALREADY_APPROVED", "This expense is already approved.");
        if (a.Partner == null)
            return ("NOT_A_PARTNER", "Only partners can approve expenses. Link your admin login to your partner profile in Company → Partners & ledger.");
        if (IsCreator(e, a)) return ("OWN_EXPENSE", "You added this expense, so you can't approve it — another partner has to approve it.");
        return null;
    }

    public static bool CanApprove(ExpenseEntry e, Approver a) => Check(e, a) == null;

    public static void Approve(ExpenseEntry e, Approver a)
    {
        e.IsApproved = true;
        e.ApprovedBy = a.Partner?.Name ?? a.AdminName;
        e.ApprovedByAdminId = a.AdminId;
        e.ApprovedAt = DateTime.UtcNow;
        e.UpdatedAt = DateTime.UtcNow;
        e.UpdatedBy = a.AdminName;
    }

    /// <summary>An approved expense whose money details changed needs a fresh approval.</summary>
    public static void ResetApproval(ExpenseEntry e)
    {
        e.IsApproved = false; e.ApprovedBy = null; e.ApprovedByAdminId = null; e.ApprovedAt = null;
    }

    /// <summary>Snapshot of the fields that decide the money: amount, who paid, how it is split.</summary>
    public static string MoneyKey(ExpenseEntry e) =>
        string.Join("|", e.Amount.ToString("0.00"), e.PayerType, e.PaidBy, e.PaidByPartnerId, e.SplitMethod,
            string.Join(",", e.Splits.Where(s => !s.IsDeleted).OrderBy(s => s.PartnerId ?? 0).Select(s => $"{s.PartnerId}:{s.Amount:0.00}")));
}
