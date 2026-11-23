using WahSpices.Domain.Enums;

namespace WahSpices.Infrastructure.Email;

/// <summary>Every template with realistic sample data — used by the Development-only preview endpoint.</summary>
public static class EmailPreviewCatalog
{
    public sealed record SampleProduct(string Name, string Weight, decimal Price, decimal Mrp, string? ImagePath, string? Slug);

    public sealed record Entry(string Key, string Group, string Description, Func<EmailBrand, IReadOnlyList<SampleProduct>, RenderedEmail> Render);

    public static readonly IReadOnlyList<SampleProduct> FallbackProducts = new[]
    {
        new SampleProduct("Kitchen King Masala", "100g", 85, 95, "/uploads/products/catalog/kitchen-king-masala.png", "kitchen-king-masala"),
        new SampleProduct("Sabji Masala", "200g", 120, 140, "/uploads/products/catalog/sabji-masala.png", "sabji-masala"),
        new SampleProduct("Kadai Masala", "100g", 90, 90, "/uploads/products/catalog/kadai-masala.png", "kadai-masala"),
    };

    private static OrderEmailModel Order(IReadOnlyList<SampleProduct> p, PaymentMethod method, PaymentStatus pay, OrderStatus status,
        string? courier = null, string? awb = null, string? cancel = null)
    {
        var items = p.Take(3).Select((x, i) => new OrderEmailItem(x.Name, x.Weight, i == 1 ? 2 : 1, x.Price, x.Mrp, x.Price * (i == 1 ? 2 : 1), x.ImagePath, x.Slug)).ToList();
        var subtotal = items.Sum(i => i.LineTotal);
        var mrp = items.Sum(i => i.Mrp * i.Qty);
        const decimal coupon = 30;
        var delivery = subtotal - coupon >= 499 ? 0 : 49;
        return new OrderEmailModel
        {
            Id = 1042,
            OrderNumber = "WAH2611154821",
            PlacedAtUtc = DateTime.UtcNow.AddHours(-3),
            CustomerName = "Priya Sharma",
            CustomerEmail = "priya.sharma@example.com",
            Items = items,
            SubtotalMrp = mrp,
            Subtotal = subtotal,
            CouponDiscount = coupon,
            CouponCode = "WELCOME30",
            Delivery = delivery,
            GrandTotal = subtotal - coupon + delivery,
            PaymentMethod = method,
            PaymentStatus = pay,
            Status = status,
            Address = new OrderEmailAddress("Priya Sharma", "98765 43210", "B-402, Shanti Residency, Ring Road", "Opp. City Light Mall", "Surat", "Gujarat", "395007"),
            EstimatedDeliveryUtc = DateTime.UtcNow.AddDays(5),
            DeliveredAtUtc = status == OrderStatus.Delivered ? DateTime.UtcNow : null,
            Courier = courier,
            AwbNumber = awb,
            CancellationReason = cancel,
            PaymentReference = pay == PaymentStatus.Paid && method != PaymentMethod.CashOnDelivery ? "pay_PZx81kQe2LmN4c" : null,
        };
    }

    private static ReturnEmailModel Return(IReadOnlyList<SampleProduct> p, string? courier = null, string? awb = null, string? reject = null)
    {
        var x = p.Count > 1 ? p[1] : p[0];
        return new ReturnEmailModel
        {
            ReturnNumber = "RET/2026/0017",
            OrderId = 1042,
            OrderNumber = "WAH2611154821",
            CustomerName = "Priya Sharma",
            CustomerEmail = "priya.sharma@example.com",
            Type = ReturnType.Refund,
            Reason = "Pouch arrived torn",
            Items = new() { new ReturnEmailItem(x.Name, x.Weight, 2, x.Price, x.Price * 2, x.ImagePath, x.Slug) },
            RefundAmount = x.Price * 2,
            RefundMethod = RefundMethod.OriginalPaymentMethod,
            RefundReference = "UTR 4521 8873 1102",
            CreditNoteNumber = "CN/2026/0009",
            PickupAddress = "B-402, Shanti Residency, Ring Road, Surat, Gujarat - 395007",
            PickupScheduledUtc = DateTime.UtcNow.AddDays(2),
            Courier = courier,
            AwbNumber = awb,
            RejectionReason = reject,
            PaymentMethod = PaymentMethod.UPI,
            RequestedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
    }

    private const string ResetLinkSample = "/reset-password?token=sample-preview-token-not-valid-0123456789abcdef";

    public static readonly IReadOnlyList<Entry> All = new List<Entry>
    {
        // Auth
        new("auth-verify-register", "Account", "Email verification code (sign-up)", (b, p) => EmailTemplates.VerificationCode(b, "Priya Sharma", "482913", 5, EmailTemplates.OtpContext.Register)),
        new("auth-verify-resend", "Account", "Verification code (resend)", (b, p) => EmailTemplates.VerificationCode(b, "Priya Sharma", "730145", 5, EmailTemplates.OtpContext.Resend)),
        new("auth-verify-login", "Account", "Login needs verification (code)", (b, p) => EmailTemplates.VerificationCode(b, "Priya Sharma", "205867", 5, EmailTemplates.OtpContext.Login)),
        new("auth-password-reset", "Account", "Password reset link", (b, p) => EmailTemplates.PasswordReset(b, "Priya Sharma", b.Link(ResetLinkSample), 30)),
        new("auth-account-exists", "Account", "Account already exists notice", (b, p) => EmailTemplates.AccountExists(b, "Priya Sharma")),
        new("auth-mobile-in-use", "Account", "Mobile number already used notice", (b, p) => EmailTemplates.MobileInUse(b, "Priya Sharma")),
        new("auth-password-changed", "Account", "Password changed confirmation", (b, p) => EmailTemplates.PasswordChanged(b, "Priya Sharma", DateTime.UtcNow, false)),
        new("auth-password-reset-done", "Account", "Password reset completed", (b, p) => EmailTemplates.PasswordChanged(b, "Priya Sharma", DateTime.UtcNow, true)),
        new("auth-welcome", "Account", "Welcome email after verification", (b, p) => EmailTemplates.Welcome(b, "Priya Sharma")),

        // Orders
        new("order-placed-cod", "Orders", "Order confirmation — Cash on Delivery", (b, p) => EmailTemplates.OrderPlaced(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.Pending))),
        new("order-placed-online-pending", "Orders", "Order received — online payment awaited", (b, p) => EmailTemplates.OrderPlaced(b, Order(p, PaymentMethod.UPI, PaymentStatus.Initiated, OrderStatus.Pending))),
        new("order-confirmed", "Order status", "Status: Confirmed", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.Confirmed), OrderStatus.Confirmed, null)),
        new("order-processing", "Order status", "Status: Processing", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.Processing), OrderStatus.Processing, null)),
        new("order-packed", "Order status", "Status: Packed", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.UPI, PaymentStatus.Paid, OrderStatus.Packed), OrderStatus.Packed, null)),
        new("order-dispatched", "Order status", "Status: Shipped (courier + AWB + link)", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.Dispatched, "Delhivery", "1490 2280 4473 21"), OrderStatus.Dispatched, "Packed with extra care — your parcel has 3 items.")),
        new("order-in-transit", "Order status", "Status: In transit", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.UPI, PaymentStatus.Paid, OrderStatus.InTransit, "Delhivery", "1490228044732"), OrderStatus.InTransit, null)),
        new("order-out-for-delivery", "Order status", "Status: Out for delivery (COD reminder)", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.OutForDelivery, "Delhivery", "1490228044732"), OrderStatus.OutForDelivery, null)),
        new("order-delivered", "Order status", "Status: Delivered (rate products)", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Paid, OrderStatus.Delivered), OrderStatus.Delivered, null)),
        new("order-cancelled-customer", "Order status", "Cancelled by customer (paid online → refund)", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.UPI, PaymentStatus.Paid, OrderStatus.Cancelled), OrderStatus.Cancelled, "Ordered by mistake", CancelSource.Customer)),
        new("order-cancelled-store", "Order status", "Cancelled by store", (b, p) => EmailTemplates.OrderStatusUpdate(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.Cancelled), OrderStatus.Cancelled, "Sabji Masala 200g is temporarily out of stock. We're sorry for the trouble!", CancelSource.Store)),
        new("order-cancelled-unpaid", "Payments", "Auto-cancelled: payment not completed", (b, p) => EmailTemplates.OrderCancelled(b, Order(p, PaymentMethod.UPI, PaymentStatus.Failed, OrderStatus.Cancelled), CancelSource.PaymentTimeout, null)),
        new("order-refunded", "Order status", "Refunded (full)", (b, p) => EmailTemplates.RefundIssued(b, Order(p, PaymentMethod.UPI, PaymentStatus.Refunded, OrderStatus.Refunded), 335, false, null)),
        new("order-partially-refunded", "Order status", "Partially refunded", (b, p) => EmailTemplates.RefundIssued(b, Order(p, PaymentMethod.Card, PaymentStatus.PartiallyRefunded, OrderStatus.Delivered), 120, true, "Refund for 1 damaged pouch.")),

        // Payments
        new("payment-received", "Payments", "Payment received (online) — order confirmed", (b, p) => EmailTemplates.PaymentReceived(b, Order(p, PaymentMethod.UPI, PaymentStatus.Paid, OrderStatus.Confirmed))),
        new("payment-failed", "Payments", "Payment failed — retry", (b, p) => EmailTemplates.PaymentFailed(b, Order(p, PaymentMethod.Card, PaymentStatus.Failed, OrderStatus.Pending), 22)),
        new("payment-after-cancel", "Payments", "Paid after cancellation — refund", (b, p) => EmailTemplates.PaidAfterCancel(b, Order(p, PaymentMethod.UPI, PaymentStatus.Paid, OrderStatus.Cancelled))),

        // Returns
        new("return-requested", "Returns", "Return request received", (b, p) => EmailTemplates.ReturnUpdate(b, Return(p), ReturnEmailKind.Requested)),
        new("return-approved", "Returns", "Return approved (pickup)", (b, p) => EmailTemplates.ReturnUpdate(b, Return(p), ReturnEmailKind.Approved)),
        new("return-rejected", "Returns", "Return rejected (reason)", (b, p) => EmailTemplates.ReturnUpdate(b, Return(p, reject: "The pouch was opened and more than half used. As per our return policy, opened food products can only be returned if they are damaged or defective."), ReturnEmailKind.Rejected)),
        new("return-picked-up", "Returns", "Return picked up", (b, p) => EmailTemplates.ReturnUpdate(b, Return(p, "Delhivery", "RTN88120934"), ReturnEmailKind.PickedUp)),
        new("return-received", "Returns", "Return received at warehouse", (b, p) => EmailTemplates.ReturnUpdate(b, Return(p, "Delhivery", "RTN88120934"), ReturnEmailKind.Received)),
        new("return-refunded", "Returns", "Return refund processed", (b, p) => EmailTemplates.ReturnUpdate(b, Return(p, "Delhivery", "RTN88120934"), ReturnEmailKind.Refunded)),

        // Store / other
        new("store-new-order", "Store", "New order notification (store inbox)", (b, p) => EmailTemplates.StoreNewOrder(b, Order(p, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, OrderStatus.Pending))),
        new("invoice", "Billing", "Invoice email", (b, p) => EmailTemplates.Invoice(b, SampleInvoice(b, p))),
        new("contact-store", "Contact", "Contact form → store inbox", (b, p) => EmailTemplates.ContactStoreNotification(b, "WAH-261115-4821", "Rahul Mehta", "rahul.mehta@example.com", "98250 11223", "Bulk order enquiry", "Hi, I run a small restaurant in Vadodara and would like to order 10 kg of Kitchen King Masala every month. Do you offer wholesale pricing?\n\nThanks,\nRahul")),
        new("contact-ack", "Contact", "Contact form acknowledgement (ticket)", (b, p) => EmailTemplates.ContactAcknowledgement(b, "WAH-261115-4821", "Rahul Mehta", "Bulk order enquiry", "Hi, I run a small restaurant in Vadodara and would like to order 10 kg of Kitchen King Masala every month. Do you offer wholesale pricing?")),
        new("contact-reply", "Contact", "Reply to a contact message", (b, p) => EmailTemplates.ContactReply(b, "Rahul Mehta", "WAH-261115-4821", "Bulk order enquiry", "Hi, I run a small restaurant in Vadodara and would like to order 10 kg of Kitchen King Masala every month.", "Hi Rahul,\n\nThank you for your interest! Yes, we offer wholesale pricing for orders above 5 kg. Our sales team will call you tomorrow with a quote.")),
        new("feedback-reply", "Contact", "Reply to feedback", (b, p) => EmailTemplates.FeedbackReply(b, "Anita Desai", 5, "Kitchen King Masala", "The aroma is amazing — my family loved the paneer sabji!", "Thank you so much, Anita! Reviews like yours make our day. Happy cooking!")),
        new("crm-reminder", "CRM", "Payment reminder (trade customer)", (b, p) => EmailTemplates.CrmPaymentReminder(b, b.Name, "Dear Shree Ganesh Traders, this is a gentle reminder that ₹18,450 has been outstanding for 42 days. Kindly arrange the payment at your earliest convenience. — " + b.Name, 18450, 50000)),
        new("crm-greeting", "CRM", "Birthday / anniversary greeting with promo", (b, p) => EmailTemplates.CrmGreeting(b, b.Name, "Birthday", "Dear Mr. Patel, Happy Birthday! Enjoy a special discount — use code BIRTHDAY012 on your next order.", "BIRTHDAY012")),
        new("campaign-offer", "Marketing", "Campaign email (offer with coupon + products)", (b, p) => CampaignEmails.Render(b, new CampaignEmailModel
        {
            TypeValue = "Offer", TypeLabel = "Offer", Title = "Diwali special: 20% off every masala",
            Body = "Celebrate the festival of lights with fresh, small-batch masalas.\n\nUse the code below on any order this week — free delivery above ₹499.",
            CouponCode = "DIWALI20", ButtonLabel = "Shop the sale", ButtonUrl = "/products", RecipientName = "Priya Sharma",
            Products = p.Take(2).Select(x => new CampaignEmailProduct(x.Name, x.Weight, x.Price, x.Mrp, x.ImagePath, x.Slug)).ToList(),
            UnsubscribeUrl = b.Link("/unsubscribe?token=preview"),
        })),
        new("payroll-slip", "HR", "Salary slip", (b, p) => EmailTemplates.SalarySlip(b, "Ramesh Patel", "October 2026", SampleSlip())),
    };

    private static InvoiceEmailModel SampleInvoice(EmailBrand b, IReadOnlyList<SampleProduct> p) => new()
    {
        InvoiceNumber = "WAH/2026/049",
        DateUtc = DateTime.UtcNow,
        Status = "Paid",
        CompanyName = b.Name,
        CompanyAddress = b.Address,
        CompanyGstin = b.Gstin ?? "24ABCDE1234F1Z5",
        StateCodeLine = "State code: 24 (Gujarat)",
        CompanyContact = $"{b.Phone} | {b.Email}",
        CustomerName = "Priya Sharma",
        CustomerEmail = "priya.sharma@example.com",
        CustomerPhone = "98765 43210",
        Lines = p.Take(3).Select(x => new InvoiceEmailLine(x.Name + " " + x.Weight, "09109100", 1, x.Price, Math.Round(x.Price / 1.05m, 2), 5, x.Price)).ToList(),
        Subtotal = Math.Round(p.Take(3).Sum(x => x.Price) / 1.05m, 2),
        Cgst = Math.Round(p.Take(3).Sum(x => x.Price) / 1.05m * 0.025m, 2),
        Sgst = Math.Round(p.Take(3).Sum(x => x.Price) / 1.05m * 0.025m, 2),
        Delivery = 49,
        Total = p.Take(3).Sum(x => x.Price) + 49,
        BankLine = "State Bank of India | A/C: XXXXXX4521 | IFSC: SBIN0001234",
        Terms = "Goods once sold can be returned within 7 days if damaged.\nSubject to Surat jurisdiction.",
        FooterText = "Thank you for shopping with WAH! Spices — Pehle bite mein WAH!",
    };

    private static string SampleSlip()
    {
        const string font = "font-family:'Poppins',Arial,Helvetica,sans-serif;";
        var td = $"{font}font-size:12px;color:#1d2a20;padding:7px 8px;border-bottom:1px solid #efe3cf";
        var th = $"{font}font-size:11px;color:#ffffff;background:#1a5c2e;padding:8px;text-align:left";
        return $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><th style=\"{th}\">Earnings</th><th style=\"{th};text-align:right\">Amount</th><th style=\"{th}\">Deductions</th><th style=\"{th};text-align:right\">Amount</th></tr>" +
               $"<tr><td style=\"{td}\">Basic</td><td style=\"{td};text-align:right\">₹15,000.00</td><td style=\"{td}\">Provident Fund (EPF)</td><td style=\"{td};text-align:right\">₹1,800.00</td></tr>" +
               $"<tr><td style=\"{td}\">HRA</td><td style=\"{td};text-align:right\">₹6,000.00</td><td style=\"{td}\">Professional Tax</td><td style=\"{td};text-align:right\">₹200.00</td></tr>" +
               $"<tr><td style=\"{td};font-weight:700\">Gross earnings</td><td style=\"{td};text-align:right;font-weight:700\">₹21,000.00</td><td style=\"{td};font-weight:700\">Total deductions</td><td style=\"{td};text-align:right;font-weight:700\">₹2,000.00</td></tr></table>" +
               $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin-top:14px;background:#e8f3ec;border-radius:10px\"><tr><td style=\"{font}padding:12px 14px;font-size:15px;font-weight:700;color:#0f3d1e\">Net pay</td><td align=\"right\" style=\"{font}padding:12px 14px;font-size:20px;font-weight:700;color:#0f3d1e\">₹19,000.00</td></tr></table>";
    }
}
