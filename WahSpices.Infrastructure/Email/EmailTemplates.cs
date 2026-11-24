using System.Text;
using WahSpices.Domain.Enums;
using static WahSpices.Infrastructure.Email.EmailLayout;

namespace WahSpices.Infrastructure.Email;

/// <summary>
/// Every transactional email the system sends. Pure functions: (brand, data) → subject + HTML + plain text.
/// Used by the senders (via the queue) and by the Development preview endpoint with sample data.
/// </summary>
public static class EmailTemplates
{
    private const string ReasonOrder = "you placed an order with us. These updates are about your order and can't be switched off.";
    private const string ReasonAccount = "this email address was used on our website. Account and security emails can't be switched off.";

    private static string First(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0 || n.Equals("User", StringComparison.OrdinalIgnoreCase)) return "there";
        var sp = n.IndexOf(' ');
        return sp > 0 ? n[..sp] : n;
    }

    // ═════════════════════════════ AUTH ═════════════════════════════

    public enum OtpContext { Register, Resend, Login }

    public static RenderedEmail VerificationCode(EmailBrand b, string name, string code, int minutes, OtpContext ctx)
    {
        var (title, intro) = ctx switch
        {
            OtpContext.Login => ("Finish signing in", $"Hi {H(First(name))}, you tried to sign in but your email isn't verified yet. Enter this code on the website to verify it and finish signing in."),
            OtpContext.Resend => ("Here's your new code", $"Hi {H(First(name))}, as requested, here's a fresh verification code. Any earlier code no longer works."),
            _ => ("Verify your email", $"Hi {H(First(name))}, welcome to {H(b.Name)}! Enter this code on the website to verify your email and activate your account."),
        };
        var body = Hero("&#128272;", "Verification code", title, intro, Tone.Info) +
                   CodeBox(code, $"Expires in {minutes} minutes") +
                   Callout($"Never share this code with anyone — {H(b.Name)} staff will never ask for it.", Tone.Warning) +
                   Small($"<strong>Didn't request this?</strong> Someone may have typed your email by mistake. You can safely ignore this email — no account will be activated without the code.");
        return Render(b, $"Your {b.Name} verification code", $"Your code expires in {minutes} minutes. Never share it with anyone.",
            body, ReasonAccount);
    }

    public static RenderedEmail PasswordReset(EmailBrand b, string name, string link, int minutes)
    {
        var body = Hero("&#128273;", "Password reset", "Reset your password",
                       $"Hi {H(First(name))}, we received a request to reset the password for your {H(b.Name)} account. Click the button below to choose a new one.", Tone.Info) +
                   Buttons(("Reset my password", link, true)) +
                   Small($"This link works once and expires in <strong>{minutes} minutes</strong>.", "center") +
                   Divider() +
                   Small($"Button not working? Copy this link into your browser:<br><a href=\"{Attr(link)}\" style=\"color:{Green};word-break:break-all\">{H(link)}</a>") +
                   Callout("If you didn't ask to reset your password, you can ignore this email — your password stays the same.", Tone.Neutral);
        return Render(b, $"Reset your {b.Name} password", $"This secure link expires in {minutes} minutes.", body, ReasonAccount);
    }

    /// <summary>New admin-panel user: their sign-in email + the temporary password chosen by the Super Admin.</summary>
    public static RenderedEmail AdminInvite(EmailBrand b, string name, string email, string tempPassword, string role, string invitedBy)
    {
        var login = b.Link("/admin/login");
        var body = Hero("&#128272;", "Admin panel access", $"Welcome to the {H(b.Name)} team!",
                       $"Hi {H(First(name))}, {H(invitedBy)} has created an account for you on the {H(b.Name)} admin panel with the role <strong>{H(role)}</strong>.", Tone.Info) +
                   SectionTitle("Your sign-in details") +
                   KeyValues(new[]
                   {
                       ("Login page", A(login, login)),
                       ("Username (email)", $"<strong>{H(email)}</strong>"),
                       ("Temporary password", $"<strong style=\"font-family:monospace;font-size:15px;letter-spacing:1px\">{H(tempPassword)}</strong>"),
                       ("Role", H(role)),
                   }) +
                   Buttons(("Sign in to the admin panel", login, true)) +
                   Callout("<strong>You must change this password the first time you sign in.</strong> The temporary password stops working as soon as you set your own.", Tone.Warning) +
                   Small("For your security, delete this email after you have set your new password and never share your password with anyone — our team will never ask for it.");
        return Render(b, $"Your {b.Name} admin account is ready", $"Sign in as {email} and set your own password.", body, ReasonAccount, showHelp: false);
    }

    /// <summary>Admin-panel password reset by a Super Admin: new temporary password, change required at next sign-in.</summary>
    public static RenderedEmail AdminPasswordReset(EmailBrand b, string name, string email, string tempPassword, string resetBy)
    {
        var login = b.Link("/admin/login");
        var body = Hero("&#128273;", "Admin panel", "Your password was reset",
                       $"Hi {H(First(name))}, {H(resetBy)} has reset the password of your {H(b.Name)} admin account. You have been signed out everywhere.", Tone.Warning) +
                   KeyValues(new[]
                   {
                       ("Login page", A(login, login)),
                       ("Username (email)", $"<strong>{H(email)}</strong>"),
                       ("Temporary password", $"<strong style=\"font-family:monospace;font-size:15px;letter-spacing:1px\">{H(tempPassword)}</strong>"),
                   }) +
                   Buttons(("Sign in to the admin panel", login, true)) +
                   Callout("<strong>You must choose a new password when you sign in.</strong>", Tone.Warning) +
                   Small("Didn't expect this? Contact your administrator right away.");
        return Render(b, $"Your {b.Name} admin password was reset", "Sign in with the temporary password and set a new one.", body, ReasonAccount, showHelp: false);
    }

    public static RenderedEmail AccountExists(EmailBrand b, string name)
    {
        var body = Hero("&#128075;", "You already have an account", "Looks like you're already with us",
                       $"Hi {H(First(name))}, someone (hopefully you) just tried to create a new {H(b.Name)} account with this email address. You already have one, so no new account was created.", Tone.Info) +
                   Buttons(("Sign in", b.Link("/login"), true), ("Forgot password?", b.Link("/forgot-password"), false)) +
                   Callout("If this wasn't you, you can ignore this email — your account is safe and nothing has changed.", Tone.Neutral);
        return Render(b, "Someone tried to create an account with your email", "You already have an account — just sign in.", body, ReasonAccount);
    }

    public static RenderedEmail MobileInUse(EmailBrand b, string name)
    {
        var body = Hero("&#128241;", "Registration not completed", "We couldn't complete your registration",
                       $"Hi {H(First(name))}, we couldn't create your {H(b.Name)} account because this mobile number is already linked to another account.", Tone.Warning) +
                   PHtml("Please sign in with that account, or register again with a different mobile number.") +
                   Buttons(("Sign in", b.Link("/login"), true), ("Register again", b.Link("/register"), false)) +
                   Small("If you didn't try to register, you can ignore this email.");
        return Render(b, "We couldn't complete your registration", "This mobile number is already linked to another account.", body, ReasonAccount);
    }

    public static RenderedEmail PasswordChanged(EmailBrand b, string name, DateTime whenUtc, bool viaReset)
    {
        var body = Hero("&#9989;", "Security alert", "Your password was changed",
                       $"Hi {H(First(name))}, the password for your {H(b.Name)} account was {(viaReset ? "reset using a password-reset link" : "changed from your profile")} on <strong>{H(DateTimeIst(whenUtc))}</strong>.", Tone.Success) +
                   PHtml("For your security, you've been signed out of your other devices. Sign in again with your new password.") +
                   Callout($"<strong>Wasn't you?</strong> Reset your password right away and contact us{(string.IsNullOrWhiteSpace(b.Phone) ? "" : " at " + A(b.PhoneHref, b.Phone))}.", Tone.Danger) +
                   Buttons(("Reset password", b.Link("/forgot-password"), false), ("Sign in", b.Link("/login"), true));
        return Render(b, $"Your {b.Name} password was changed", "If this wasn't you, reset your password right away.", body, ReasonAccount);
    }

    public static RenderedEmail Welcome(EmailBrand b, string name)
    {
        var perks = new List<string>
        {
            "&#127798;&nbsp; Freshly ground, small-batch masalas — no added colour",
            "&#128666;&nbsp; " + (b.FreeDeliveryAbove > 0 ? $"Free delivery on orders above {Money(b.FreeDeliveryAbove)}" : "Free delivery on every order"),
            "&#128230;&nbsp; Track every order from your account" + (string.IsNullOrWhiteSpace(b.AvgDeliveryDays) ? "" : $" — delivered in {H(b.AvgDeliveryDays)} days"),
        };
        var list = string.Join("", perks.Select(p => $@"<tr><td style=""padding:6px 0;font-family:{Font};font-size:14px;line-height:21px;color:{Ink}"">{p}</td></tr>"));
        var body = Hero("&#127881;", "Welcome to the family", $"Welcome, {First(name)}!",
                       $"Your email is verified and your {H(b.Name)} account is ready. {(string.IsNullOrWhiteSpace(b.Tagline) ? "" : "<em>" + H(b.Tagline) + "</em>")}", Tone.Success) +
                   $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:18px 0;background:{Cream};border:1px solid {Line};border-radius:12px;border-collapse:separate""><tr><td style=""padding:10px 18px""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">{list}</table></td></tr></table>" +
                   Buttons(("Start shopping", b.Link("/products"), true), ("My account", b.Link("/profile"), false));
        return Render(b, $"Welcome to {b.Name}!", "Your account is ready — let's get cooking.", body, ReasonAccount);
    }

    // ═════════════════════════════ ORDER HELPERS ═════════════════════════════

    public static string TrackUrl(EmailBrand b, OrderEmailModel o) =>
        b.Link($"/track-order?order={Uri.EscapeDataString(o.OrderNumber)}&contact={Uri.EscapeDataString(o.CustomerEmail)}");
    public static string OrderUrl(EmailBrand b, int orderId) => b.Link($"/profile/orders?order={orderId}");
    private static string ProductUrl(EmailBrand b, string? slug) => string.IsNullOrWhiteSpace(slug) ? "" : b.Link("/product/" + Uri.EscapeDataString(slug));

    private static string OrderButtons(EmailBrand b, OrderEmailModel o) =>
        Buttons(("Track your order", TrackUrl(b, o), true), ("View order details", OrderUrl(b, o.Id), false)) +
        (string.IsNullOrWhiteSpace(o.AttachedInvoiceNumber) ? "" :
            Callout($"Your tax invoice <strong>{H(o.AttachedInvoiceNumber)}</strong> is attached to this email as a PDF. You can also download it anytime from your order details.", Tone.Info, "Invoice attached"));

    public static string MethodName(PaymentMethod m) => m switch
    {
        PaymentMethod.CashOnDelivery => "Cash on Delivery",
        PaymentMethod.Card => "Credit / Debit card",
        PaymentMethod.NetBanking => "Net banking",
        PaymentMethod.UPI => "UPI",
        PaymentMethod.Wallet => "Wallet",
        _ => m.ToString(),
    };

    private static string PaymentStatusPill(OrderEmailModel o) => o.PaymentStatus switch
    {
        PaymentStatus.Paid => Pill("Paid", Tone.Success),
        PaymentStatus.Refunded => Pill("Refunded", Tone.Neutral),
        PaymentStatus.PartiallyRefunded => Pill("Partly refunded", Tone.Neutral),
        PaymentStatus.Failed => Pill("Failed", Tone.Danger),
        _ => o.IsCod ? Pill("Pay on delivery", Tone.Warning) : Pill("Awaiting payment", Tone.Warning),
    };

    private static List<ItemRow> Rows(EmailBrand b, IEnumerable<OrderEmailItem> items) =>
        items.Select(i => new ItemRow(i.Name, i.Weight, i.Qty, i.UnitPrice, i.LineTotal, b.Asset(i.ImagePath), ProductUrl(b, i.Slug))).ToList();

    private static string OrderSummary(EmailBrand b, OrderEmailModel o, bool detailed, string title = "Order summary")
    {
        var rows = new List<(string, string, bool)>();
        if (detailed)
        {
            var productSavings = o.SubtotalMrp - o.Subtotal;
            if (productSavings > 0.009m)
            {
                rows.Add(("Items total (MRP)", Money(o.SubtotalMrp), false));
                rows.Add(("Product discount", "&minus;" + Money(productSavings), true));
            }
            else rows.Add(("Items total", Money(o.Subtotal), false));
            if (o.CouponDiscount > 0) rows.Add(($"Coupon{(string.IsNullOrWhiteSpace(o.CouponCode) ? "" : " (" + o.CouponCode + ")")}", "&minus;" + Money(o.CouponDiscount), true));
            rows.Add(("Delivery", o.Delivery <= 0 ? $"<span style=\"color:{Green};font-weight:700\">FREE</span>" : Money(o.Delivery), false));
        }
        var grandLabel = o.IsCod && o.PaymentStatus != PaymentStatus.Paid ? "Amount to pay on delivery" : "Grand total";
        return SectionTitle(title) + Items(Rows(b, o.Items), detailed) +
               (detailed ? Totals(rows, grandLabel, Money(o.GrandTotal))
                         : Totals(Array.Empty<(string, string, bool)>(), grandLabel, Money(o.GrandTotal)));
    }

    private static string AddressHtml(OrderEmailAddress? a)
    {
        if (a == null) return "—";
        var sb = new StringBuilder($"<strong>{H(a.Name)}</strong><br>{H(a.Line)}");
        if (!string.IsNullOrWhiteSpace(a.Landmark)) sb.Append($"<br>{H(a.Landmark)}");
        sb.Append($"<br>{H(a.City)}, {H(a.State)} &ndash; {H(a.Pincode)}");
        if (!string.IsNullOrWhiteSpace(a.Phone)) sb.Append($"<br><span style=\"color:{Muted}\">Phone: {H(a.Phone)}</span>");
        return sb.ToString();
    }

    private static string OrderMeta(OrderEmailModel o, bool showEta = true)
    {
        var det = new StringBuilder();
        det.Append($"Order <strong>#{H(o.OrderNumber)}</strong><br>");
        det.Append($"<span style=\"color:{Muted}\">Placed on</span> {H(Date(o.PlacedAtUtc))}<br>");
        det.Append($"<span style=\"color:{Muted}\">Payment</span> {H(MethodName(o.PaymentMethod))}<br>{PaymentStatusPill(o)}");
        if (showEta && o.EstimatedDeliveryUtc.HasValue && o.Status is not (OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Refunded or OrderStatus.Returned))
            det.Append($"<br><span style=\"color:{Muted}\">Estimated delivery</span><br><strong style=\"color:{Green}\">by {H(Date(o.EstimatedDeliveryUtc.Value))}</strong>");
        return TwoColumns(InfoCard("Order details", det.ToString()), InfoCard("Delivering to", AddressHtml(o.Address)));
    }

    public static int TrackerIndex(OrderStatus s) => s switch
    {
        OrderStatus.Pending => 0,
        OrderStatus.Confirmed or OrderStatus.Processing => 1,
        OrderStatus.Packed => 2,
        OrderStatus.Dispatched or OrderStatus.InTransit => 3,
        OrderStatus.OutForDelivery => 4,
        _ => 5,
    };

    /// <summary>Courier tracking link: the one saved on the order, else a known courier's public tracking page.</summary>
    public static string? CourierLink(OrderEmailModel o)
    {
        if (!string.IsNullOrWhiteSpace(o.CourierTrackingUrl) &&
            (o.CourierTrackingUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || o.CourierTrackingUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            return o.CourierTrackingUrl;
        if (string.IsNullOrWhiteSpace(o.AwbNumber) || string.IsNullOrWhiteSpace(o.Courier)) return null;
        var awb = Uri.EscapeDataString(o.AwbNumber.Trim());
        var c = o.Courier.ToLowerInvariant().Replace(" ", "");
        if (c.Contains("delhivery")) return $"https://www.delhivery.com/track/package/{awb}";
        if (c.Contains("shiprocket")) return $"https://shiprocket.co/tracking/{awb}";
        if (c.Contains("xpressbees")) return $"https://www.xpressbees.com/shipment/tracking?awbNo={awb}";
        if (c.Contains("ecom")) return $"https://ecomexpress.in/tracking/?awb_field={awb}";
        if (c.Contains("ekart")) return $"https://ekartlogistics.com/shipmenttrack/{awb}";
        if (c.Contains("bluedart")) return $"https://www.bluedart.com/web/guest/trackdartresult?trackFor=0&trackNo={awb}";
        return null;
    }

    private static string CourierBox(OrderEmailModel o)
    {
        if (string.IsNullOrWhiteSpace(o.Courier) && string.IsNullOrWhiteSpace(o.AwbNumber)) return "";
        var rows = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(o.Courier)) rows.Add(("Courier partner", H(o.Courier)));
        if (!string.IsNullOrWhiteSpace(o.AwbNumber)) rows.Add(("Tracking number (AWB)", $"<span style=\"font-family:'Courier New',Courier,monospace;font-size:15px\">{H(o.AwbNumber)}</span>"));
        var link = CourierLink(o);
        if (link != null) rows.Add(("Courier tracking", A(link, $"Track on {o.Courier ?? "courier"} website")));
        return SectionTitle("Shipment") + KeyValues(rows);
    }

    private static RenderedEmail OrderShell(EmailBrand b, OrderEmailModel o, string subject, string preheader, string body) =>
        Render(b, subject, preheader, body, ReasonOrder);

    // ═════════════════════════════ ORDERS ═════════════════════════════

    public static RenderedEmail OrderPlaced(EmailBrand b, OrderEmailModel o)
    {
        string intro, subject, pre;
        string payNote;
        if (o.IsCod)
        {
            subject = $"Order placed: #{o.OrderNumber} — thank you!";
            pre = $"We've received your order of {Money(o.GrandTotal)}. Pay on delivery.";
            intro = $"Hi {H(First(o.CustomerName))}, thank you for shopping with {H(b.Name)}! We've received your order and will confirm it shortly.";
            payNote = Callout($"You chose <strong>Cash on Delivery</strong>. Please pay <strong>{Money(o.GrandTotal)}</strong> in cash or UPI when your order arrives.", Tone.Warning, "Pay on delivery");
        }
        else if (o.PaymentStatus == PaymentStatus.Paid)
        {
            subject = $"Order confirmed: #{o.OrderNumber} — thank you!";
            pre = $"Payment of {Money(o.GrandTotal)} received. We're getting your order ready.";
            intro = $"Hi {H(First(o.CustomerName))}, thank you for shopping with {H(b.Name)}! Your payment was successful and your order is confirmed.";
            payNote = Callout($"We've received your payment of <strong>{Money(o.GrandTotal)}</strong> via {H(MethodName(o.PaymentMethod))}.", Tone.Success, "Payment received");
        }
        else
        {
            subject = $"Order received: #{o.OrderNumber} — payment awaited";
            pre = $"Complete your payment of {Money(o.GrandTotal)} to confirm your order.";
            intro = $"Hi {H(First(o.CustomerName))}, thank you for shopping with {H(b.Name)}! We've saved your order — it will be confirmed as soon as your payment is complete.";
            payNote = Callout($"Payment of <strong>{Money(o.GrandTotal)}</strong> via {H(MethodName(o.PaymentMethod))} is <strong>awaited</strong>. If the payment window closed, open your order to try again — unpaid orders are cancelled automatically after a short while.", Tone.Warning, "Payment awaited");
        }
        var body = Hero("&#127881;", o.IsCod || o.PaymentStatus != PaymentStatus.Paid ? "Order received" : "Order confirmed", "Thank you for your order!", intro) +
                   Tracker(TrackerIndex(o.Status)) +
                   OrderButtons(b, o) +
                   payNote +
                   OrderMeta(o) +
                   OrderSummary(b, o, detailed: true) +
                   Spacer(8) +
                   Small($"We'll email you at every step — when your order is confirmed, packed, shipped and delivered.");
        return OrderShell(b, o, subject, pre, body);
    }

    /// <summary>Status-change email for any OrderStatus (Cancelled uses <paramref name="cancelSource"/>).</summary>
    public static RenderedEmail OrderStatusUpdate(EmailBrand b, OrderEmailModel o, OrderStatus status, string? note, CancelSource? cancelSource = null)
    {
        if (status == OrderStatus.Cancelled) return OrderCancelled(b, o, cancelSource ?? CancelSource.Store, note);
        if (status == OrderStatus.Refunded) return RefundIssued(b, o, o.GrandTotal, partial: false, note);

        var n = H(First(o.CustomerName));
        var num = H(o.OrderNumber);
        string emoji, eyebrow, title, intro, subject, pre;
        var extra = "";
        var showTracker = true;
        switch (status)
        {
            case OrderStatus.Confirmed:
                emoji = "&#9989;"; eyebrow = "Order confirmed"; title = "Your order is confirmed!";
                intro = $"Great news, {n}! We've confirmed order <strong>#{num}</strong> and our team will start packing your spices shortly.";
                subject = $"Your order #{o.OrderNumber} is confirmed"; pre = "We're getting your spices ready.";
                break;
            case OrderStatus.Processing:
                emoji = "&#129379;"; eyebrow = "Being prepared"; title = "We're preparing your order";
                intro = $"Hi {n}, your order <strong>#{num}</strong> is being prepared — fresh spices, carefully weighed and sealed.";
                subject = $"Your order #{o.OrderNumber} is being prepared"; pre = "Your spices are being prepared and packed.";
                break;
            case OrderStatus.Packed:
                emoji = "&#128230;"; eyebrow = "Packed"; title = "Your order is packed";
                intro = $"Hi {n}, your order <strong>#{num}</strong> is packed, sealed and ready to ship. We'll let you know as soon as it's handed over to our courier partner.";
                subject = $"Your order #{o.OrderNumber} is packed"; pre = "Packed and ready to ship.";
                break;
            case OrderStatus.Dispatched:
                emoji = "&#128666;"; eyebrow = "Shipped"; title = "Your order is on its way!";
                intro = $"Hi {n}, good news — your order <strong>#{num}</strong> has been shipped" + (string.IsNullOrWhiteSpace(o.Courier) ? "." : $" with <strong>{H(o.Courier)}</strong>.");
                subject = $"Shipped: your order #{o.OrderNumber} is on its way"; pre = string.IsNullOrWhiteSpace(o.AwbNumber) ? "Your spices are on the way." : $"Tracking number {o.AwbNumber}.";
                extra = CourierBox(o);
                break;
            case OrderStatus.InTransit:
                emoji = "&#128739;"; eyebrow = "In transit"; title = "Your order is in transit";
                intro = $"Hi {n}, your order <strong>#{num}</strong> is moving through our courier network and getting closer to you.";
                subject = $"Your order #{o.OrderNumber} is in transit"; pre = "Getting closer every hour.";
                extra = CourierBox(o);
                break;
            case OrderStatus.OutForDelivery:
                emoji = "&#128757;"; eyebrow = "Out for delivery"; title = "Arriving today!";
                intro = $"Hi {n}, your order <strong>#{num}</strong> is out for delivery and should reach you today. Please keep your phone handy for the delivery partner.";
                subject = $"Out for delivery: your order #{o.OrderNumber} arrives today"; pre = "Your spices arrive today.";
                if (o.IsCod && o.PaymentStatus != PaymentStatus.Paid)
                    extra = Callout($"Please keep <strong>{Money(o.GrandTotal)}</strong> ready — pay in cash or UPI to the delivery partner.", Tone.Warning, "Cash on Delivery");
                extra += CourierBox(o);
                break;
            case OrderStatus.Delivered:
                emoji = "&#127881;"; eyebrow = "Delivered"; title = "Delivered — enjoy your spices!";
                intro = $"Hi {n}, your order <strong>#{num}</strong> was delivered{(o.DeliveredAtUtc.HasValue ? " on " + H(Date(o.DeliveredAtUtc.Value)) : "")}. Thank you for choosing {H(b.Name)} — we hope every dish says <em>WAH!</em>";
                subject = $"Delivered: your order #{o.OrderNumber}"; pre = "Thank you! Tell us how you liked it.";
                extra = RateProducts(b, o) +
                        Spacer(10) + Small($"Something not right? You can request a return from {A(OrderUrl(b, o.Id), "your order page")}.");
                break;
            case OrderStatus.ReturnRequested:
                emoji = "&#8617;"; eyebrow = "Return requested"; title = "Return requested";
                intro = $"Hi {n}, a return has been requested for order <strong>#{num}</strong>. We'll keep you posted at every step.";
                subject = $"Return requested for order #{o.OrderNumber}"; pre = "We'll keep you posted."; showTracker = false;
                break;
            case OrderStatus.Returned:
                emoji = "&#128230;"; eyebrow = "Returned"; title = "Your return has reached us";
                intro = $"Hi {n}, the returned items from order <strong>#{num}</strong> have reached our warehouse. Your refund is being processed.";
                subject = $"Return received for order #{o.OrderNumber}"; pre = "Your refund is being processed."; showTracker = false;
                break;
            default:
                emoji = "&#128203;"; eyebrow = "Order update"; title = "Your order has been updated";
                intro = $"Hi {n}, the status of your order <strong>#{num}</strong> is now <strong>{H(StatusLabel(status))}</strong>.";
                subject = $"Update on your order #{o.OrderNumber}"; pre = $"Status: {StatusLabel(status)}.";
                break;
        }
        var body = Hero(emoji, eyebrow, title, intro) +
                   (showTracker ? Tracker(TrackerIndex(status)) : "") +
                   (string.IsNullOrWhiteSpace(note) ? "" : Callout(HMultiline(note), Tone.Info, "Note from our team")) +
                   extra +
                   OrderButtons(b, o) +
                   OrderSummary(b, o, detailed: false, title: "In this order") +
                   Spacer(20) +
                   (status is OrderStatus.Delivered ? "" : OrderMeta(o));
        return OrderShell(b, o, subject, pre, body);
    }

    public static string StatusLabel(OrderStatus s) => s switch
    {
        OrderStatus.Pending => "Order placed",
        OrderStatus.InTransit => "In transit",
        OrderStatus.OutForDelivery => "Out for delivery",
        OrderStatus.ReturnRequested => "Return requested",
        OrderStatus.Dispatched => "Shipped",
        _ => s.ToString(),
    };

    private static string RateProducts(EmailBrand b, OrderEmailModel o)
    {
        var products = o.Items.Where(i => !string.IsNullOrWhiteSpace(i.Slug)).GroupBy(i => i.Slug).Select(g => g.First()).Take(6).ToList();
        if (products.Count == 0) return "";
        var sb = new StringBuilder(SectionTitle("How did we do?"));
        sb.Append(PHtml("Your review helps other food lovers pick the right masala. It takes less than a minute:", "font-size:14px;color:" + Muted));
        sb.Append(@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">");
        foreach (var p in products)
        {
            var url = ProductUrl(b, p.Slug);
            var img = b.Asset(p.ImagePath);
            var thumb = img == null ? "" : $@"<img src=""{Attr(img)}"" width=""40"" height=""40"" alt=""{Attr(p.Name)}"" style=""display:block;width:40px;height:40px;border-radius:8px;border:1px solid {Line};font-size:9px"">";
            sb.Append($@"<tr><td width=""52"" style=""width:52px;padding:8px 12px 8px 0;border-bottom:1px solid {Line}"">{thumb}</td>" +
                      $@"<td style=""padding:8px 0;border-bottom:1px solid {Line};font-family:{Font};font-size:14px;line-height:20px;font-weight:600;color:{Ink}"">{H(p.Name)}<div style=""font-size:16px;line-height:20px;color:{Orange};letter-spacing:2px"">&#9733;&#9733;&#9733;&#9733;&#9733;</div></td>" +
                      $@"<td align=""right"" style=""padding:8px 0;border-bottom:1px solid {Line}""><a href=""{Attr(url)}"" target=""_blank"" style=""display:inline-block;padding:7px 14px;border:2px solid {Green};border-radius:8px;font-family:{Font};font-size:13px;line-height:16px;font-weight:600;color:{Green};text-decoration:none;white-space:nowrap"">Rate &amp; review</a></td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    public static RenderedEmail OrderCancelled(EmailBrand b, OrderEmailModel o, CancelSource source, string? reason)
    {
        var n = H(First(o.CustomerName));
        var num = H(o.OrderNumber);
        var why = string.IsNullOrWhiteSpace(reason) ? o.CancellationReason : reason;
        string intro, title;
        switch (source)
        {
            case CancelSource.Customer:
                title = "Your order has been cancelled";
                intro = $"Hi {n}, as requested, we've cancelled your order <strong>#{num}</strong>.";
                break;
            case CancelSource.PaymentTimeout:
                title = "Order cancelled — payment not completed";
                intro = $"Hi {n}, we didn't receive the payment for your order <strong>#{num}</strong> in time, so it was cancelled automatically and the items were released.";
                why = null;
                break;
            default:
                title = "We had to cancel your order";
                intro = $"Hi {n}, we're really sorry — we had to cancel your order <strong>#{num}</strong>.";
                break;
        }
        var refund = o.PaidOnline
            ? Callout($"You paid <strong>{Money(o.GrandTotal)}</strong> online. The full amount will be refunded to your original payment method — it usually reaches your account within 5–7 business days.", Tone.Success, "Refund")
            : o.IsCod ? Callout("This was a Cash on Delivery order, so nothing was charged.", Tone.Neutral)
            : Callout("No payment was taken for this order. If any amount was debited, it will be refunded automatically by your bank or payment provider.", Tone.Neutral);
        var body = Hero("&#10060;", "Order cancelled", title, intro, Tone.Danger) +
                   (string.IsNullOrWhiteSpace(why) ? "" : Callout(HMultiline(why), Tone.Danger, "Reason")) +
                   refund +
                   Buttons(("Shop again", b.Link("/products"), true), ("View order details", OrderUrl(b, o.Id), false)) +
                   OrderSummary(b, o, detailed: false, title: "Cancelled items");
        var subject = source == CancelSource.PaymentTimeout ? $"Order #{o.OrderNumber} cancelled — payment not completed" : $"Your order #{o.OrderNumber} has been cancelled";
        return OrderShell(b, o, subject, o.PaidOnline ? "Your refund is on its way." : "Here are the details.", body);
    }

    public static RenderedEmail RefundIssued(EmailBrand b, OrderEmailModel o, decimal amount, bool partial, string? note)
    {
        var body = Hero("&#128184;", partial ? "Partial refund" : "Refund processed", partial ? "We've issued a partial refund" : "Your refund has been processed",
                       $"Hi {H(First(o.CustomerName))}, we've refunded <strong>{Money(amount)}</strong> for order <strong>#{H(o.OrderNumber)}</strong> to your original payment method ({H(MethodName(o.PaymentMethod))}).") +
                   KeyValues(new[]
                   {
                       ("Refund amount", $"<span style=\"color:{Green};font-size:16px\">{Money(amount)}</span>"),
                       ("Order total", Money(o.GrandTotal)),
                       ("Refunded to", H(MethodName(o.PaymentMethod))),
                   }) +
                   (string.IsNullOrWhiteSpace(note) ? "" : Callout(HMultiline(note), Tone.Info, "Note from our team")) +
                   Small("Banks usually take 5–7 business days to show the money in your account. If you don't see it by then, please contact us with your order number.") +
                   Buttons(("View order details", OrderUrl(b, o.Id), true));
        return OrderShell(b, o, partial ? $"Partial refund of {Money(amount)} for order #{o.OrderNumber}" : $"Refund of {Money(amount)} processed for order #{o.OrderNumber}",
            "It usually reaches your account within 5–7 business days.", body);
    }

    // ═════════════════════════════ PAYMENTS ═════════════════════════════

    public static RenderedEmail PaymentReceived(EmailBrand b, OrderEmailModel o)
    {
        var body = Hero("&#9989;", "Payment successful", "Payment received — order confirmed!",
                       $"Hi {H(First(o.CustomerName))}, we've received your payment of <strong>{Money(o.GrandTotal)}</strong> for order <strong>#{H(o.OrderNumber)}</strong>. We'll start packing your spices right away.") +
                   Tracker(TrackerIndex(OrderStatus.Confirmed)) +
                   KeyValues(new List<(string, string)>
                   {
                       ("Amount paid", $"<span style=\"color:{Green}\">{Money(o.GrandTotal)}</span>"),
                       ("Payment method", H(MethodName(o.PaymentMethod))),
                   }.Concat(string.IsNullOrWhiteSpace(o.PaymentReference) ? Array.Empty<(string, string)>() : new[] { ("Payment reference", H(o.PaymentReference)) })) +
                   OrderButtons(b, o) +
                   OrderMeta(o) +
                   OrderSummary(b, o, detailed: true);
        return OrderShell(b, o, $"Payment received for order #{o.OrderNumber}", "Your order is confirmed.", body);
    }

    public static RenderedEmail PaymentFailed(EmailBrand b, OrderEmailModel o, int? minutesLeft)
    {
        var window = minutesLeft is > 0 ? $" within the next {minutesLeft} minutes" : " soon";
        var body = Hero("&#9888;&#65039;", "Payment unsuccessful", "Your payment didn't go through",
                       $"Hi {H(First(o.CustomerName))}, we couldn't confirm the payment for your order <strong>#{H(o.OrderNumber)}</strong>. Don't worry — your order is saved.", Tone.Warning) +
                   Callout($"Please complete the payment of <strong>{Money(o.GrandTotal)}</strong>{window} from your order page, otherwise the order will be cancelled automatically. If money was debited, it will be refunded automatically by your bank.", Tone.Warning) +
                   Buttons(("Complete payment", OrderUrl(b, o.Id), true), ("Track your order", TrackUrl(b, o), false)) +
                   OrderSummary(b, o, detailed: false, title: "Your order");
        return OrderShell(b, o, $"Payment failed for order #{o.OrderNumber}", "Your order is saved — please try the payment again.", body);
    }

    public static RenderedEmail PaidAfterCancel(EmailBrand b, OrderEmailModel o)
    {
        var body = Hero("&#128184;", "Refund on the way", "We'll refund your payment",
                       $"Hi {H(First(o.CustomerName))}, we received your payment of <strong>{Money(o.GrandTotal)}</strong> for order <strong>#{H(o.OrderNumber)}</strong>, but the order had already been cancelled because the payment window had closed.", Tone.Warning) +
                   Callout($"The full amount of <strong>{Money(o.GrandTotal)}</strong> will be refunded to your original payment method. Refunds usually reach your account within 5–7 business days.", Tone.Success, "Refund") +
                   PHtml("Still want these spices? You're welcome to place the order again.") +
                   Buttons(("Shop again", b.Link("/products"), true), ("View order details", OrderUrl(b, o.Id), false)) +
                   OrderSummary(b, o, detailed: false, title: "Cancelled order");
        return OrderShell(b, o, $"Payment received for cancelled order #{o.OrderNumber} — refund on the way", "Your payment will be refunded.", body);
    }

    /// <summary>Store inbox notification for a new order (SiteSettings.OrderEmails).</summary>
    public static RenderedEmail StoreNewOrder(EmailBrand b, OrderEmailModel o)
    {
        var body = Hero("&#128722;", "New order", $"New order #{o.OrderNumber}",
                       $"<strong>{H(o.CustomerName)}</strong> ({H(o.CustomerEmail)}) placed an order of <strong>{Money(o.GrandTotal)}</strong> — {H(MethodName(o.PaymentMethod))}.") +
                   Buttons(("Open in admin", b.Link("/admin/orders"), true)) +
                   OrderMeta(o) +
                   OrderSummary(b, o, detailed: true);
        return Render(b, $"New order #{o.OrderNumber} — {Money(o.GrandTotal)} ({MethodName(o.PaymentMethod)})", $"{o.CustomerName} · {o.Items.Sum(i => i.Qty)} item(s)",
            body, "\"New order emails\" are switched on in Admin → Settings.", showHelp: false);
    }

    // ═════════════════════════════ RETURNS ═════════════════════════════

    private static string RefundMethodName(RefundMethod m, PaymentMethod pay) => m switch
    {
        RefundMethod.OriginalPaymentMethod => pay == PaymentMethod.CashOnDelivery ? "Bank transfer / UPI (COD order)" : $"Original payment method ({MethodName(pay)})",
        RefundMethod.BankTransfer => "Bank transfer",
        RefundMethod.WalletCredit => "Wallet credit",
        RefundMethod.StoreCoupon => "Store coupon",
        RefundMethod.UPI => "UPI",
        _ => m.ToString(),
    };

    private static string ReturnItems(EmailBrand b, ReturnEmailModel r, string title) =>
        SectionTitle(title) +
        Items(r.Items.Select(i => new ItemRow(i.Name, i.Weight, i.Qty, i.UnitPrice, i.LineTotal, b.Asset(i.ImagePath), ProductUrl(b, i.Slug))), detailed: true) +
        Totals(Array.Empty<(string, string, bool)>(), "Refund amount", Money(r.RefundAmount));

    private static readonly string[] ReturnSteps = { "Requested", "Approved", "Picked up", "Received", "Refunded" };

    private static string ReturnTracker(int current) => Tracker(current, ReturnSteps);

    public static RenderedEmail ReturnUpdate(EmailBrand b, ReturnEmailModel r, ReturnEmailKind kind)
    {
        var n = H(First(r.CustomerName));
        var rn = H(r.ReturnNumber);
        var on = H(r.OrderNumber);
        var orderBtn = Buttons(("View return status", OrderUrl(b, r.OrderId), true));
        string body, subject, pre;
        switch (kind)
        {
            case ReturnEmailKind.Requested:
                body = Hero("&#8617;&#65039;", "Return request received", "We've received your return request",
                           $"Hi {n}, we've received your request to return items from order <strong>#{on}</strong>. Your return number is <strong>{rn}</strong>.", Tone.Info) +
                       ReturnTracker(0) +
                       KeyValues(new[] { ("Return number", rn), ("Order", "#" + on), ("Reason", H(r.Reason)), ("Estimated refund", $"<span style=\"color:{Green}\">{Money(r.RefundAmount)}</span>") }) +
                       Callout("Our team will review your request within 1–2 business days. Once approved, we'll arrange a pickup from your address. Please keep the items in their original packing.", Tone.Info, "What happens next") +
                       orderBtn + ReturnItems(b, r, "Items to return");
                subject = $"Return request {r.ReturnNumber} received"; pre = $"Estimated refund {Money(r.RefundAmount)}. We'll review it within 1–2 business days.";
                break;
            case ReturnEmailKind.Approved:
                var pickup = r.PickupScheduledUtc.HasValue ? $"on <strong>{H(Date(r.PickupScheduledUtc.Value))}</strong>" : "in the next few days — we'll share the date";
                body = Hero("&#9989;", "Return approved", "Your return is approved",
                           $"Hi {n}, good news — your return <strong>{rn}</strong> for order <strong>#{on}</strong> has been approved. Our courier partner will pick up the items {pickup}.") +
                       ReturnTracker(1) +
                       (string.IsNullOrWhiteSpace(r.PickupAddress) ? "" : InfoCard("Pickup address", H(r.PickupAddress))) +
                       Callout("Please keep the items packed (ideally in the original pouch/box) and hand them over to the pickup partner. Your refund will be processed once the items reach our warehouse.", Tone.Info, "Getting ready for pickup") +
                       orderBtn + ReturnItems(b, r, "Items being returned");
                subject = $"Return {r.ReturnNumber} approved — pickup scheduled"; pre = "Here's what happens next.";
                break;
            case ReturnEmailKind.Rejected:
                body = Hero("&#128683;", "Return not approved", "We couldn't approve your return",
                           $"Hi {n}, we've reviewed your return request <strong>{rn}</strong> for order <strong>#{on}</strong> and unfortunately couldn't approve it.", Tone.Danger) +
                       (string.IsNullOrWhiteSpace(r.RejectionReason) ? "" : Callout(HMultiline(r.RejectionReason), Tone.Danger, "Reason")) +
                       PHtml("If you think this is a mistake or have more details to share (like photos), just reply to this email or contact us — we'll take another look.") +
                       Buttons(("View order details", OrderUrl(b, r.OrderId), true), ("Contact us", b.Link("/contact"), false));
                subject = $"Update on your return request {r.ReturnNumber}"; pre = "We couldn't approve this return.";
                break;
            case ReturnEmailKind.PickedUp:
                var rows = new List<(string, string)> { ("Return number", rn) };
                if (!string.IsNullOrWhiteSpace(r.Courier)) rows.Add(("Courier partner", H(r.Courier)));
                if (!string.IsNullOrWhiteSpace(r.AwbNumber)) rows.Add(("Tracking number (AWB)", $"<span style=\"font-family:'Courier New',Courier,monospace\">{H(r.AwbNumber)}</span>"));
                body = Hero("&#128666;", "Picked up", "Your return has been picked up",
                           $"Hi {n}, the items for return <strong>{rn}</strong> have been picked up and are on their way to our warehouse.") +
                       ReturnTracker(2) + KeyValues(rows) +
                       Small("We'll process your refund as soon as the items reach us and pass a quick quality check.") +
                       orderBtn;
                subject = $"Return {r.ReturnNumber} picked up"; pre = "Your items are on their way back to us.";
                break;
            case ReturnEmailKind.Received:
                body = Hero("&#128230;", "Received", "We've received your returned items",
                           $"Hi {n}, your returned items for <strong>{rn}</strong> have reached our warehouse. Your refund of <strong>{Money(r.RefundAmount)}</strong> is being processed.") +
                       ReturnTracker(3) +
                       KeyValues(new[] { ("Refund amount", $"<span style=\"color:{Green}\">{Money(r.RefundAmount)}</span>"), ("Refund to", H(RefundMethodName(r.RefundMethod, r.PaymentMethod))) }) +
                       orderBtn;
                subject = $"Return {r.ReturnNumber} received — refund in progress"; pre = $"Refund of {Money(r.RefundAmount)} is being processed.";
                break;
            default: // Refunded
                var kv = new List<(string, string)>
                {
                    ("Refund amount", $"<span style=\"color:{Green};font-size:16px\">{Money(r.RefundAmount)}</span>"),
                    ("Refunded via", H(RefundMethodName(r.RefundMethod, r.PaymentMethod))),
                };
                if (!string.IsNullOrWhiteSpace(r.RefundReference)) kv.Add(("Reference", H(r.RefundReference)));
                if (!string.IsNullOrWhiteSpace(r.CreditNoteNumber)) kv.Add(("Credit note", H(r.CreditNoteNumber)));
                body = Hero("&#128184;", "Refund processed", "Your refund has been processed",
                           $"Hi {n}, we've processed a refund of <strong>{Money(r.RefundAmount)}</strong> for return <strong>{rn}</strong> (order <strong>#{on}</strong>).") +
                       ReturnTracker(4) + KeyValues(kv) +
                       Small("Depending on your bank, it can take 5–7 business days for the amount to show in your account.") +
                       orderBtn + ReturnItems(b, r, "Returned items");
                subject = $"Refund of {Money(r.RefundAmount)} processed for return {r.ReturnNumber}"; pre = "It can take 5–7 business days to reflect in your account.";
                break;
        }
        return Render(b, subject, pre, body, "you requested a return for an order placed with us.");
    }

    // ═════════════════════════════ INVOICE ═════════════════════════════

    public static RenderedEmail Invoice(EmailBrand b, InvoiceEmailModel inv)
    {
        var th = $"padding:9px 6px;background:{Green};color:#ffffff;font-family:{Font};font-size:11px;line-height:15px;font-weight:600;text-align:left";
        var td = $"padding:9px 6px;border-bottom:1px solid {Line};font-family:{Font};font-size:12px;line-height:17px;color:{Ink}";
        var sb = new StringBuilder();
        sb.Append(Hero("&#129534;", "Tax invoice", $"Invoice {inv.InvoiceNumber}", $"Hi {H(First(inv.CustomerName))}, thank you for your purchase! Here is your invoice from {H(inv.CompanyName)}.", Tone.Info));
        if (!string.IsNullOrWhiteSpace(inv.Message)) sb.Append(Callout(HMultiline(inv.Message), Tone.Neutral, "Message from " + H(inv.CompanyName)));
        if (inv.PdfAttached) sb.Append(Callout($"Your invoice <strong>{H(inv.InvoiceNumber)}</strong> is attached as a PDF — download or print it for your records.", Tone.Info, "PDF attached"));
        sb.Append(TwoColumns(
            InfoCard("From", $"<strong>{H(inv.CompanyName)}</strong><br>{H(inv.CompanyAddress)}" +
                             (string.IsNullOrWhiteSpace(inv.CompanyGstin) ? "" : $"<br>GSTIN: {H(inv.CompanyGstin)}") +
                             (string.IsNullOrWhiteSpace(inv.StateCodeLine) ? "" : $"<br>{H(inv.StateCodeLine)}") +
                             (string.IsNullOrWhiteSpace(inv.CompanyContact) ? "" : $"<br>{H(inv.CompanyContact)}")),
            InfoCard("Bill to", $"<strong>{H(inv.CustomerName)}</strong>" +
                                (string.IsNullOrWhiteSpace(inv.CustomerEmail) ? "" : $"<br>{H(inv.CustomerEmail)}") +
                                (string.IsNullOrWhiteSpace(inv.CustomerPhone) ? "" : $"<br>{H(inv.CustomerPhone)}") +
                                $"<br><br><span style=\"color:{Muted}\">Invoice</span> <strong>{H(inv.InvoiceNumber)}</strong><br><span style=\"color:{Muted}\">Date</span> {H(Date(inv.DateUtc))}<br><span style=\"color:{Muted}\">Status</span> {H(inv.Status)}")));
        sb.Append(SectionTitle("Items"));
        sb.Append($@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><th style=""{th}"">#</th><th style=""{th}"">Product</th><th class=""hide-sm"" style=""{th}"">HSN</th><th style=""{th};text-align:right"">Qty</th><th class=""hide-sm"" style=""{th};text-align:right"">Rate</th><th class=""hide-sm"" style=""{th};text-align:right"">Taxable</th><th style=""{th};text-align:right"">GST</th><th style=""{th};text-align:right"">Total</th></tr>");
        var idx = 1;
        foreach (var l in inv.Lines)
            sb.Append($@"<tr><td style=""{td}"">{idx++}</td><td style=""{td}"">{H(l.Name)}</td><td class=""hide-sm"" style=""{td}"">{H(l.Hsn ?? "-")}</td><td style=""{td};text-align:right"">{l.Qty}</td><td class=""hide-sm"" style=""{td};text-align:right;white-space:nowrap"">{Money(l.Rate)}</td><td class=""hide-sm"" style=""{td};text-align:right;white-space:nowrap"">{Money(l.Taxable)}</td><td style=""{td};text-align:right"">{l.GstRate:0.##}%</td><td style=""{td};text-align:right;white-space:nowrap;font-weight:600"">{Money(l.Total)}</td></tr>");
        sb.Append("</table>");
        var rows = new List<(string, string, bool)> { ("Subtotal (ex-GST)", Money(inv.Subtotal), false) };
        if (inv.Discount > 0) rows.Add(("Discount", "&minus;" + Money(inv.Discount), true));
        if (inv.Cgst > 0) rows.Add(("CGST", Money(inv.Cgst), false));
        if (inv.Sgst > 0) rows.Add(("SGST", Money(inv.Sgst), false));
        if (inv.Igst > 0) rows.Add(("IGST", Money(inv.Igst), false));
        if (inv.Delivery > 0) rows.Add(("Delivery", Money(inv.Delivery), false));
        if (inv.RoundOff != 0) rows.Add(("Round off", (inv.RoundOff > 0 ? "+" : "&minus;") + Money(Math.Abs(inv.RoundOff)), false));
        sb.Append(Totals(rows, "Grand total", Money(inv.Total)));
        if (!string.IsNullOrWhiteSpace(inv.BankLine)) sb.Append(Spacer(12) + Small($"<strong>Bank:</strong> {H(inv.BankLine)}"));
        if (!string.IsNullOrWhiteSpace(inv.Terms)) sb.Append(Small(HMultiline(inv.Terms)));
        sb.Append(Callout(H(inv.FooterText ?? $"Thank you for shopping with {inv.CompanyName}!"), Tone.Success));
        return Render(b, $"Invoice {inv.InvoiceNumber} from {inv.CompanyName}", $"Invoice total {Money(inv.Total)}.", sb.ToString(),
            "you made a purchase from us and an invoice was issued to this address.");
    }

    // ═════════════════════════════ CONTACT / FEEDBACK / CRM / HR ═════════════════════════════

    public static RenderedEmail ContactStoreNotification(EmailBrand b, string ticket, string name, string email, string? phone, string subject, string message)
    {
        var body = Hero("&#128233;", "Website contact form", "New contact message", $"Ticket <strong>{H(ticket)}</strong> — reply from Admin &rarr; Website &rarr; Contact Messages.", Tone.Info) +
                   KeyValues(new[]
                   {
                       ("Ticket", H(ticket)), ("Name", H(name)), ("Email", A("mailto:" + email, email)),
                       ("Phone", H(string.IsNullOrWhiteSpace(phone) ? "—" : phone)), ("Subject", H(subject)),
                   }) +
                   SectionTitle("Message") +
                   Callout(HMultiline(message), Tone.Neutral) +
                   Buttons(("Open messages", b.Link("/admin/messages"), true));
        return Render(b, $"New contact message {ticket}: {subject}", $"{name}: {Trim(message, 90)}", body,
            "this is the store inbox set in Admin → Settings.", showHelp: false);
    }

    public static RenderedEmail ContactAcknowledgement(EmailBrand b, string ticket, string name, string subject, string message)
    {
        var body = Hero("&#128172;", "Message received", "Thank you for contacting us!",
                       $"Hi {H(First(name))}, thank you for your interest in {H(b.Name)}. We have received your message and a member of our team will get back to you shortly.", Tone.Info) +
                   KeyValues(new[] { ("Ticket number", $"<strong>{H(ticket)}</strong>"), ("Subject", H(subject)) }) +
                   SectionTitle("Your message") +
                   Callout(HMultiline(message), Tone.Neutral) +
                   PHtml("We usually reply within one business day. Our answer will be sent to this email address, so please keep an eye on your inbox (and your spam folder, just in case).") +
                   Small("Please quote your ticket number if you contact us again about this.") +
                   PHtml($"Warm regards,<br><strong>Team {H(b.Name)}</strong>");
        return Render(b, $"Thank you for contacting {b.Name} [{ticket}]", $"We've received your message (ticket {ticket}) and will reply shortly.", body,
            "you sent us a message through the contact form on our website.");
    }

    /// <summary>Default subject for an admin reply, e.g. "Re: Bulk order enquiry [WAH-261123-1234]".</summary>
    public static string ContactReplySubject(string originalSubject, string? ticket) =>
        string.IsNullOrWhiteSpace(ticket) ? $"Re: {originalSubject}" : $"Re: {originalSubject} [{ticket}]";

    public static RenderedEmail ContactReply(EmailBrand b, string name, string? ticket, string originalSubject, string originalMessage, string reply, string? emailSubject = null)
    {
        var subject = string.IsNullOrWhiteSpace(emailSubject) ? ContactReplySubject(originalSubject, ticket) : emailSubject.Trim();
        var body = Hero("&#128172;", string.IsNullOrWhiteSpace(ticket) ? "Reply from our team" : $"Ticket {ticket}", subject, null, Tone.Info) +
                   PHtml($"Hi {H(First(name))},") +
                   PHtml(HMultiline(reply)) +
                   PHtml($"Warm regards,<br><strong>Team {H(b.Name)}</strong>") +
                   Divider() +
                   Small($"Your original message{(string.IsNullOrWhiteSpace(ticket) ? "" : $" ({H(ticket)})")} — {H(originalSubject)}:<br>{HMultiline(originalMessage)}") +
                   Small("You can simply reply to this email if you have any more questions.");
        return Render(b, subject, Trim(reply, 100), body, "you contacted us through our website.");
    }

    public static RenderedEmail FeedbackReply(EmailBrand b, string name, int rating, string? product, string? originalMessage, string reply)
    {
        var stars = new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', 5 - Math.Clamp(rating, 0, 5));
        var body = Hero("&#128591;", "Thank you", "Thanks for your feedback", null, Tone.Success) +
                   PHtml($"Hi {H(First(name))},") +
                   PHtml(HMultiline(reply)) +
                   PHtml($"Warm regards,<br><strong>Team {H(b.Name)}</strong>") +
                   Divider() +
                   Small($"Your feedback: <span style=\"color:{Orange};letter-spacing:1px\">{stars}</span>{(string.IsNullOrWhiteSpace(product) ? "" : " · " + H(product))}<br>{HMultiline(originalMessage)}");
        return Render(b, $"Thank you for your feedback — {b.Name}", Trim(reply, 100), body, "you shared feedback on our website.");
    }

    public static RenderedEmail CrmPaymentReminder(EmailBrand b, string company, string message, decimal outstanding, decimal creditLimit)
    {
        var rows = new List<(string, string)> { ("Outstanding amount", $"<span style=\"color:{Red};font-size:16px\">{Money(outstanding)}</span>") };
        if (creditLimit > 0) rows.Add(("Credit limit", Money(creditLimit)));
        var body = Hero("&#129534;", "Payment reminder", "Friendly payment reminder", null, Tone.Warning) +
                   PHtml(HMultiline(message)) +
                   KeyValues(rows) +
                   Small($"Already paid? Please ignore this reminder or share the payment reference with us. — {H(company)}");
        return Render(b, $"Payment reminder — {company}", $"Outstanding: {Money(outstanding)}", body, "you have a trade account with us.");
    }

    public static RenderedEmail CrmGreeting(EmailBrand b, string company, string occasion, string message, string promoCode)
    {
        var emoji = occasion.Equals("Anniversary", StringComparison.OrdinalIgnoreCase) ? "&#128144;" : "&#127874;";
        var body = Hero(emoji, $"Happy {occasion}", $"Happy {occasion}!", null, Tone.Success) +
                   PHtml(HMultiline(message), "text-align:center") +
                   $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""center"" style=""margin:18px auto;border-collapse:separate""><tr><td align=""center"" style=""background:{Cream};border:2px dashed {Orange};border-radius:14px;padding:14px 28px""><div style=""font-family:{Font};font-size:12px;line-height:16px;color:{Muted};text-transform:uppercase;letter-spacing:1.2px"">Your promo code</div><div style=""font-family:'Courier New',Courier,monospace;font-size:26px;line-height:34px;font-weight:700;color:{GreenDark};letter-spacing:3px"">{H(promoCode)}</div></td></tr></table>" +
                   Buttons(("Shop now", b.Link("/products"), true)) +
                   Small($"With warm wishes from all of us at {H(company)}.", "center");
        return Render(b, $"Happy {occasion} from {company}!", $"A little treat for you: {promoCode}", body, "you're a valued customer of ours.");
    }

    public static RenderedEmail SalarySlip(EmailBrand b, string employeeName, string monthLabel, string slipHtml)
    {
        var body = Hero("&#128176;", "Salary slip", $"Salary slip — {monthLabel}", $"Hi {H(First(employeeName))}, please find your salary slip for {H(monthLabel)} below.", Tone.Info) +
                   Spacer(8) + slipHtml + Spacer(8) +
                   Small("This is a computer-generated salary slip and does not require a signature. Please contact HR if anything looks incorrect.");
        return Render(b, $"Salary slip — {monthLabel}", $"Your salary slip for {monthLabel}.", body, "you are an employee of ours.", showHelp: false);
    }

    private static string Trim(string? s, int max)
    {
        var t = (s ?? "").Replace('\n', ' ').Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
