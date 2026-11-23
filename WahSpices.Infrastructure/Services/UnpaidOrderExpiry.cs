using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// Online-payment orders reserve stock when placed. If the customer never completes the payment the order is
/// auto-cancelled after a window (config Orders:UnpaidExpiryMinutes, default 30) and its stock is restored.
/// Safe to run from several app instances at once: each order is claimed with a conditional UPDATE
/// (… WHERE Status = Pending AND PaymentStatus &lt;&gt; Paid) inside a transaction, and the stock restore is
/// idempotent per order number (InventoryLedger).
/// </summary>
public static class UnpaidOrderExpiry
{
    public const string Note = "Auto-cancelled: payment not completed";
    private static readonly PaymentStatus[] Unpaid = { PaymentStatus.Pending, PaymentStatus.Initiated, PaymentStatus.Failed };

    /// <summary>Cancels expired unpaid online orders (max <paramref name="batch"/> per call). Returns the order numbers cancelled.</summary>
    public static async Task<List<string>> RunAsync(AppDbContext db, TimeSpan window, ILogger logger, int batch = 50, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - window;
        var candidates = await db.Orders.AsNoTracking()
            .Where(o => !o.IsDeleted && o.PaymentMethod != PaymentMethod.CashOnDelivery && o.Status == OrderStatus.Pending
                        && Unpaid.Contains(o.PaymentStatus) && o.CreatedAt < cutoff)
            .OrderBy(o => o.CreatedAt).Select(o => o.Id).Take(batch).ToListAsync(ct);

        var done = new List<string>();
        foreach (var id in candidates)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                var number = await strategy.ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var now = DateTime.UtcNow;
                    // claim: only one instance wins, and only while still pending + unpaid
                    var claimed = await db.Orders
                        .Where(o => o.Id == id && o.Status == OrderStatus.Pending && o.PaymentStatus != PaymentStatus.Paid
                                    && o.PaymentStatus != PaymentStatus.Refunded && o.PaymentStatus != PaymentStatus.PartiallyRefunded)
                        .ExecuteUpdateAsync(u => u
                            .SetProperty(o => o.Status, OrderStatus.Cancelled)
                            .SetProperty(o => o.PaymentStatus, PaymentStatus.Failed)
                            .SetProperty(o => o.CancellationReason, "Payment not completed in time")
                            .SetProperty(o => o.UpdatedAt, now)
                            .SetProperty(o => o.UpdatedBy, "System"), ct);
                    if (claimed == 0) { await tx.RollbackAsync(ct); return null; }

                    var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == id, ct);
                    db.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        OrderId = id, Status = OrderStatus.Cancelled, Note = Note, UpdatedBy = "System", CreatedBy = "System",
                    });
                    await StoreRules.RestoreAsync(db, order.OrderNumber, order.OrderNumber,
                        order.Items.Where(i => !i.IsDeleted).Select(i => (i.ProductId, i.Quantity)),
                        $"Order #{order.OrderNumber} auto-cancelled (payment not completed)", "System");

                    foreach (var p in await db.Payments.Where(p => p.OrderId == id && !p.IsDeleted
                                 && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Initiated)).ToListAsync(ct))
                    {
                        p.Status = PaymentStatus.Failed;
                        p.ErrorCode = "EXPIRED";
                        p.ErrorDescription = "Payment not completed within the time limit; order auto-cancelled";
                        p.UpdatedAt = now;
                    }
                    // the coupon use is released — the order never went through
                    if (!string.IsNullOrWhiteSpace(order.CouponCode))
                    {
                        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == order.CouponCode, ct);
                        if (coupon != null && coupon.UsedCount > 0) coupon.UsedCount--;
                    }
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return order.OrderNumber;
                });
                if (number != null) { done.Add(number); logger.LogInformation("Order {Number} auto-cancelled: payment not completed", number); }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Auto-cancel of unpaid order {Id} failed", id);
            }
        }
        db.ChangeTracker.Clear();
        return done;
    }
}
