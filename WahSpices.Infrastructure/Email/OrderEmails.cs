using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.Infrastructure.Email;

/// <summary>
/// Customer notifications for orders, payments and returns. Call these AFTER the state change was saved; each call
/// queues one job — the worker loads the order fresh from the DB, renders the template and sends it with retries.
/// The caller never waits on SMTP and never fails because of email.
/// </summary>
public interface IOrderEmails
{
    void OrderPlaced(int orderId);
    void StatusChanged(int orderId, OrderStatus status, string? note = null, CancelSource? cancelSource = null);
    void PaymentReceived(int orderId);
    void PaymentFailed(int orderId);
    void PaidAfterCancel(int orderId);
    void AutoCancelled(string orderNumber);
    void Refunded(int orderId, decimal amount, bool partial, string? note = null);
    void ReturnUpdate(int returnId, ReturnEmailKind kind);
}

public sealed class OrderEmails : IOrderEmails
{
    private readonly IEmailQueue _queue;
    private readonly IConfiguration _config;

    public OrderEmails(IEmailQueue queue, IConfiguration config)
    {
        _queue = queue;
        _config = config;
    }

    public void OrderPlaced(int orderId)
    {
        Order($"order-placed #{orderId}", o => o.Id == orderId, (b, m) => EmailTemplates.OrderPlaced(b, m), attachInvoiceFor: orderId);
        // store inbox copy (Admin → Settings → "New Order Emails")
        _queue.Enqueue(new EmailJob($"store-new-order #{orderId}", async (sp, ct) =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var s = await db.SiteSettings.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (s == null || !s.OrderEmails || !EmailAddressRules.IsDeliverable(s.Email)) return null;
            var m = await OrderEmailData.LoadOrderAsync(db, o => o.Id == orderId, ct);
            if (m == null) return null;
            var b = await sp.GetRequiredService<IEmailBrandProvider>().GetAsync(ct);
            var r = EmailTemplates.StoreNewOrder(b, m);
            return new EmailMessage(s.Email, b.Name, r.Subject, r.Html, r.Text, "store-new-order");
        }));
    }

    public void StatusChanged(int orderId, OrderStatus status, string? note = null, CancelSource? cancelSource = null) =>
        Order($"order-status {status} #{orderId}", o => o.Id == orderId, (b, m) => EmailTemplates.OrderStatusUpdate(b, m, status, note, cancelSource));

    public void PaymentReceived(int orderId) =>
        Order($"payment-received #{orderId}", o => o.Id == orderId, (b, m) => EmailTemplates.PaymentReceived(b, m), attachInvoiceFor: orderId);

    public void PaymentFailed(int orderId)
    {
        var minutes = _config.GetValue<int?>("Orders:UnpaidExpiryMinutes") ?? 30;
        Order($"payment-failed #{orderId}", o => o.Id == orderId, (b, m) =>
        {
            int? left = minutes > 0 ? Math.Max(1, (int)Math.Ceiling((m.PlacedAtUtc.AddMinutes(minutes) - DateTime.UtcNow).TotalMinutes)) : null;
            return EmailTemplates.PaymentFailed(b, m, left);
        });
    }

    public void PaidAfterCancel(int orderId) =>
        Order($"paid-after-cancel #{orderId}", o => o.Id == orderId, (b, m) => EmailTemplates.PaidAfterCancel(b, m));

    public void AutoCancelled(string orderNumber) =>
        Order($"order-auto-cancelled {orderNumber}", o => o.OrderNumber == orderNumber,
            (b, m) => EmailTemplates.OrderCancelled(b, m, CancelSource.PaymentTimeout, null));

    public void Refunded(int orderId, decimal amount, bool partial, string? note = null) =>
        Order($"refund #{orderId}", o => o.Id == orderId, (b, m) => EmailTemplates.RefundIssued(b, m, amount, partial, note));

    public void ReturnUpdate(int returnId, ReturnEmailKind kind) =>
        _queue.Enqueue(new EmailJob($"return-{kind} #{returnId}", async (sp, ct) =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var r = await OrderEmailData.LoadReturnAsync(db, returnId, ct);
            if (r == null || !EmailAddressRules.IsDeliverable(r.CustomerEmail)) return null;
            var b = await sp.GetRequiredService<IEmailBrandProvider>().GetAsync(ct);
            var e = EmailTemplates.ReturnUpdate(b, r, kind);
            return new EmailMessage(r.CustomerEmail, r.CustomerName, e.Subject, e.Html, e.Text, $"return-{kind}");
        }));

    private void Order(string label, Expression<Func<Order, bool>> where, Func<EmailBrand, OrderEmailModel, RenderedEmail> build, int? attachInvoiceFor = null) =>
        _queue.Enqueue(new EmailJob(label, async (sp, ct) =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var m = await OrderEmailData.LoadOrderAsync(db, where, ct);
            if (m == null || !EmailAddressRules.IsDeliverable(m.CustomerEmail)) return null;
            var b = await sp.GetRequiredService<IEmailBrandProvider>().GetAsync(ct);

            // Attach the tax invoice PDF when one exists and Billing → Template → "Attach PDF to emails" is on
            var attachments = Array.Empty<EmailAttachment>();
            Invoice? invoice = null;
            if (attachInvoiceFor.HasValue)
            {
                try
                {
                    var settings = await db.InvoiceSettings.AsNoTracking().Where(x => !x.IsDeleted).FirstOrDefaultAsync(ct);
                    invoice = settings?.AttachPdfToEmails == true
                        ? await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == attachInvoiceFor && !i.IsDeleted && i.Status != InvoiceStatus.Cancelled, ct)
                        : null;
                    if (invoice != null)
                    {
                        var pdf = await sp.GetRequiredService<IInvoiceService>().GetPdfAsync(invoice.Id);
                        if (pdf != null)
                        {
                            attachments = new[] { new EmailAttachment(pdf.Value.FileName, pdf.Value.Pdf) };
                            m.AttachedInvoiceNumber = invoice.InvoiceNumber;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("OrderEmails")
                      .LogError(ex, "Invoice PDF for order {OrderId} could not be attached", attachInvoiceFor);
                }
            }

            var r = build(b, m);
            if (invoice != null && attachments.Length > 0)
            {
                invoice.LastSentAt = DateTime.UtcNow;
                invoice.LastSentTo = m.CustomerEmail;
                invoice.SentCount++;
                await db.SaveChangesAsync(ct);
            }
            return new EmailMessage(m.CustomerEmail, m.CustomerName, r.Subject, r.Html, r.Text, label.Split(' ')[0]) { Attachments = attachments };
        }));
}

public static class OrderEmailData
{
    public static async Task<OrderEmailModel?> LoadOrderAsync(AppDbContext db, Expression<Func<Order, bool>> where, CancellationToken ct = default)
    {
        var o = await db.Orders.AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Address)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .Where(x => !x.IsDeleted)
            .FirstOrDefaultAsync(where, ct);
        if (o == null) return null;
        var payRef = await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == o.Id && !p.IsDeleted && p.RazorpayPaymentId != null)
            .OrderByDescending(p => p.CreatedAt).Select(p => p.RazorpayPaymentId).FirstOrDefaultAsync(ct);
        var a = o.Address;
        return new OrderEmailModel
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            PlacedAtUtc = o.CreatedAt,
            CustomerName = o.User?.Name ?? a?.Name ?? "",
            CustomerEmail = o.User?.Email ?? "",
            Items = o.Items.Where(i => !i.IsDeleted).Select(i => new OrderEmailItem(i.ProductName, i.Weight, i.Quantity, i.Price, i.Mrp, i.LineTotal,
                i.Product?.ImageUrl, i.Product?.Slug)).ToList(),
            SubtotalMrp = o.SubtotalMrp,
            Subtotal = o.Subtotal,
            CouponDiscount = o.CouponDiscount,
            CouponCode = o.CouponCode,
            Delivery = o.DeliveryCharge,
            GrandTotal = o.GrandTotal,
            PaymentMethod = o.PaymentMethod,
            PaymentStatus = o.PaymentStatus,
            Status = o.Status,
            Address = a == null ? null : new OrderEmailAddress(a.Name, a.Phone, a.AddressLine, a.Landmark, a.City, a.State, a.Pincode),
            EstimatedDeliveryUtc = o.EstimatedDelivery,
            DeliveredAtUtc = o.DeliveredAt,
            Courier = o.Courier,
            AwbNumber = o.AwbNumber,
            CourierTrackingUrl = o.CourierTrackingUrl,
            CancellationReason = o.CancellationReason,
            PaymentReference = payRef ?? o.RazorpayPaymentId,
        };
    }

    public static async Task<ReturnEmailModel?> LoadReturnAsync(AppDbContext db, int returnId, CancellationToken ct = default)
    {
        var r = await db.ReturnOrders.AsNoTracking()
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .Include(x => x.Order)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == returnId && !x.IsDeleted, ct);
        if (r == null) return null;
        return new ReturnEmailModel
        {
            ReturnNumber = r.ReturnNumber,
            OrderId = r.OrderId,
            OrderNumber = r.Order?.OrderNumber ?? "",
            CustomerName = r.User?.Name ?? "",
            CustomerEmail = r.User?.Email ?? "",
            Type = r.Type,
            Reason = r.Reason,
            Items = r.Items.Where(i => !i.IsDeleted && i.IsApproved).Select(i => new ReturnEmailItem(i.ProductName, i.Weight, i.Quantity, i.UnitPrice, i.LineTotal,
                i.Product?.ImageUrl, i.Product?.Slug)).ToList(),
            RefundAmount = r.GrandRefundAmount,
            RefundMethod = r.RefundMethod,
            RefundReference = r.RefundReferenceId,
            CreditNoteNumber = r.CreditNoteNumber,
            PickupAddress = r.PickupAddress,
            PickupScheduledUtc = r.PickupScheduledAt,
            Courier = r.CourierName,
            AwbNumber = r.AwbNumber,
            RejectionReason = r.RejectionReason,
            PaymentMethod = r.Order?.PaymentMethod ?? PaymentMethod.CashOnDelivery,
            RequestedAtUtc = r.CreatedAt,
        };
    }
}
