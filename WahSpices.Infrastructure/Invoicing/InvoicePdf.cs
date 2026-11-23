using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;

namespace WahSpices.Infrastructure.Invoicing;

/// <summary>Everything printed on an invoice PDF. Built from an Invoice + InvoiceSettings (+ store fallbacks).</summary>
public sealed class InvoicePdfModel
{
    public required Invoice Invoice { get; init; }
    public required InvoiceSettings Settings { get; init; }
    public string? OrderNumber { get; init; }
    public DateTime? OrderDateUtc { get; init; }
    public string? FallbackUdyam { get; init; }
    public string? FallbackFssai { get; init; }
    public bool IsSample { get; init; }
}

/// <summary>
/// Renders GST tax invoices (A4) with QuestPDF. Pure function of <see cref="InvoicePdfModel"/> — no DB access — so the
/// Template tab can preview unsaved settings. Fonts (Noto Sans, has the ₹ glyph) and the default logo are embedded.
/// Images are taken only from data: URLs or files under wwwroot (never fetched from the network).
/// </summary>
public static class InvoicePdfRenderer
{
    private const string FontFamily = "Noto Sans";
    private static readonly IST Ist = new();
    private static readonly byte[]? DefaultLogo;

    static InvoicePdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = false;
        var asm = typeof(InvoicePdfRenderer).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.GetManifestResourceStream(name)!;
                QuestPDF.Drawing.FontManager.RegisterFont(s);
            }
            else if (name.EndsWith("wah_logo.jpg", StringComparison.OrdinalIgnoreCase))
            {
                using var s = asm.GetManifestResourceStream(name)!;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                DefaultLogo = ms.ToArray();
            }
        }
    }

    public static string FileNameFor(string invoiceNumber) =>
        "Invoice-" + string.Concat(invoiceNumber.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')) + ".pdf";

    public static byte[] Render(InvoicePdfModel m)
    {
        var s = m.Settings;
        var inv = m.Invoice;
        var color = IsHex(s.PrimaryColor) ? s.PrimaryColor : "#1a5c2e";
        var accent = "#F4F7F2";
        var lines = inv.LineItems.Where(l => !l.IsDeleted).ToList();
        var intra = inv.GstType == GSTType.IntraState;
        var hasIrn = !string.IsNullOrWhiteSpace(inv.Irn);
        var title = string.IsNullOrWhiteSpace(s.InvoiceTitle) ? "Tax Invoice" : s.InvoiceTitle.Trim();
        var logo = LoadImage(s.CompanyLogo) ?? DefaultLogo;
        var signature = s.ShowSignature ? LoadImage(s.SignatureImage) : null;
        var stamp = s.ShowStamp ? LoadImage(s.StampImage) : null;
        var udyam = Blank(s.UdyamNumber) ? m.FallbackUdyam : s.UdyamNumber;
        var fssai = Blank(s.Fssai) ? m.FallbackFssai : s.Fssai;
        var invDate = Ist.ToLocal(inv.InvoiceDate ?? inv.CreatedAt);
        var paid = inv.Status == InvoiceStatus.Paid;
        var cancelled = inv.Status == InvoiceStatus.Cancelled;
        var upiQr = s.ShowUpiQr && !paid && !cancelled && !Blank(s.UpiId) && inv.TotalAmount > 0
            ? Qr($"upi://pay?pa={Uri.EscapeDataString(s.UpiId!.Trim())}&pn={Uri.EscapeDataString(s.CompanyName)}&am={inv.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)}&cu=INR&tn={Uri.EscapeDataString("Invoice " + inv.InvoiceNumber)}")
            : null;
        var irnQr = hasIrn && !Blank(inv.SignedQrCode) ? Qr(inv.SignedQrCode!) : null;

        var showMrp = s.ShowMrpColumn && lines.Any(l => l.MrpPrice > 0);
        var showDisc = s.ShowDiscountColumn && lines.Any(l => l.DiscountAmount > 0);
        var showHsn = s.ShowHsnColumn;

        var doc = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(8.5f).FontColor("#1F2937"));

            if (cancelled || m.IsSample)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-30)
                    .Text(cancelled ? "CANCELLED" : "SAMPLE").FontSize(80).Bold().FontColor(cancelled ? "#33DC2626" : "#22000000");

            // ───────────── Header ─────────────
            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    if (logo != null)
                        row.ConstantItem(64).Height(64).Element(e => SafeImage(e, logo));
                    row.ConstantItem(logo != null ? 10 : 0);
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(s.CompanyName).FontSize(16).Bold().FontColor(color);
                        var addr = JoinNonEmpty(", ", s.Address, s.City, s.State) + (Blank(s.Pincode) ? "" : $" - {s.Pincode}");
                        if (!Blank(addr)) c.Item().Text(addr).FontSize(8);
                        var contact = JoinNonEmpty("  |  ", Blank(s.Phone) ? null : "Ph: " + s.Phone, s.Email, s.Website);
                        if (!Blank(contact)) c.Item().Text(contact).FontSize(8);
                        var reg = JoinNonEmpty("  |  ",
                            Blank(s.Gstin) ? null : "GSTIN: " + s.Gstin!.Trim().ToUpperInvariant(),
                            GstStates.CodeOf(s.State) is { } sc ? $"State: {s.State} ({sc})" : null,
                            Blank(s.Pan) ? null : "PAN: " + s.Pan);
                        if (!Blank(reg)) c.Item().Text(reg).FontSize(8).SemiBold();
                        var reg2 = JoinNonEmpty("  |  ", Blank(fssai) ? null : "FSSAI Lic. No.: " + fssai, Blank(udyam) ? null : "Udyam: " + udyam);
                        if (!Blank(reg2)) c.Item().Text(reg2).FontSize(8);
                    });
                    row.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().Background(color).PaddingVertical(6).PaddingHorizontal(8).AlignCenter()
                            .Text(title.ToUpperInvariant()).FontSize(12).Bold().FontColor(Colors.White);
                        if (hasIrn) c.Item().PaddingTop(2).AlignCenter().Text("e-Invoice").FontSize(8).SemiBold().FontColor(color);
                        c.Item().PaddingTop(3).AlignCenter().Text("Original for Recipient").FontSize(7).Italic().FontColor("#6B7280");
                        c.Item().PaddingTop(4).AlignCenter().Text(paid ? "PAID" : cancelled ? "CANCELLED" : inv.Status == InvoiceStatus.PartiallyPaid ? "PARTIALLY PAID" : "PAYMENT DUE")
                            .FontSize(9).Bold().FontColor(paid ? "#15803D" : cancelled ? "#DC2626" : "#B45309");
                    });
                });
                col.Item().PaddingTop(8).LineHorizontal(1.2f).LineColor(color);
            });

            // ───────────── Content ─────────────
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(8);

                if (hasIrn)
                {
                    col.Item().Border(0.7f).BorderColor("#D1D5DB").Background(accent).Padding(6).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text(t => { t.Span("IRN: ").SemiBold(); t.Span(inv.Irn!).FontSize(7.5f); });
                            c.Item().Text(t =>
                            {
                                t.Span("Ack No: ").SemiBold(); t.Span(inv.IrnAckNo ?? "-");
                                t.Span("     Ack Date: ").SemiBold(); t.Span(inv.IrnAckDate.HasValue ? Ist.ToLocal(inv.IrnAckDate.Value).ToString("dd-MM-yyyy HH:mm") : "-");
                            });
                        });
                        if (irnQr != null) r.ConstantItem(70).Height(70).Image(irnQr);
                    });
                }

                // Invoice meta
                col.Item().Border(0.7f).BorderColor("#D1D5DB").Row(r =>
                {
                    void Cell(RowDescriptor rd, params (string K, string? V)[] kv) => rd.RelativeItem().Padding(6).Column(c =>
                    {
                        foreach (var (k, v) in kv.Where(x => !Blank(x.V)))
                            c.Item().Text(t => { t.Span(k + ": ").FontColor("#6B7280"); t.Span(v!).SemiBold(); });
                    });
                    var pos = GstStates.CodeOf(inv.PlaceOfSupply) is { } pc ? $"{pc}-{GstStates.NameOf(pc)}" : inv.PlaceOfSupply;
                    Cell(r, ("Invoice No", inv.InvoiceNumber), ("Invoice Date", invDate.ToString("dd MMM yyyy")),
                        ("Order No", m.OrderNumber), ("Order Date", m.OrderDateUtc.HasValue ? Ist.ToLocal(m.OrderDateUtc.Value).ToString("dd MMM yyyy") : null));
                    Cell(r, ("Place of Supply", pos), ("Supply Type", inv.Type == InvoiceType.B2B ? "B2B" : "B2C"),
                        ("Payment", JoinNonEmpty(" · ", inv.PaymentMethod, paid ? "Paid" : null)), ("Reverse Charge", "No"));
                });

                // Bill to / Ship to
                col.Item().Row(r =>
                {
                    var bill = ParseAddress(inv.BillingAddress);
                    var ship = ParseAddress(inv.ShippingAddress) ?? bill;
                    r.RelativeItem().Element(e => PartyBox(e, "Bill To", inv.CustomerName, bill, inv.CustomerPhone, inv.CustomerEmail, inv.CustomerGstin, color));
                    r.ConstantItem(8);
                    r.RelativeItem().Element(e => PartyBox(e, "Ship To", ship?.Name ?? inv.CustomerName, ship, ship?.Phone ?? inv.CustomerPhone, null, null, color));
                });

                // Items
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(18);           // #
                        c.RelativeColumn(4);            // item
                        if (showHsn) c.ConstantColumn(48);
                        c.ConstantColumn(28);           // qty
                        if (showMrp) c.ConstantColumn(44);
                        c.ConstantColumn(48);           // rate
                        if (showDisc) c.ConstantColumn(42);
                        c.ConstantColumn(52);           // taxable
                        c.ConstantColumn(30);           // gst %
                        if (intra) { c.ConstantColumn(42); c.ConstantColumn(42); } else c.ConstantColumn(48);
                        c.ConstantColumn(56);           // amount
                    });

                    t.Header(h =>
                    {
                        void H(string text, bool right = true) =>
                            h.Cell().Background(color).PaddingVertical(4).PaddingHorizontal(3)
                             .Element(e => right ? e.AlignRight() : e.AlignLeft()).Text(text).FontSize(7.5f).SemiBold().FontColor(Colors.White);
                        H("#", false); H("Item", false);
                        if (showHsn) H("HSN", false);
                        H("Qty");
                        if (showMrp) H("MRP");
                        H("Rate");
                        if (showDisc) H("Disc.");
                        H("Taxable");
                        H("GST");
                        if (intra) { H("CGST"); H("SGST"); } else H("IGST");
                        H("Amount");
                    });

                    var i = 1;
                    foreach (var l in lines)
                    {
                        var bg = i % 2 == 0 ? accent : "#FFFFFF";
                        IContainer C(bool right = true) => t.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#E5E7EB")
                            .PaddingVertical(4).PaddingHorizontal(3).Element(e => right ? e.AlignRight() : e.AlignLeft());
                        var rate = l.Quantity > 0 ? Math.Round((l.TaxableAmount + l.DiscountAmount) / l.Quantity, 2) : l.UnitPrice;
                        C(false).Text(i.ToString());
                        C(false).Text(l.ProductName).SemiBold();
                        if (showHsn) C(false).Text(l.HsnCode ?? "-");
                        C().Text($"{l.Quantity} {(l.Unit is "pcs" or "" ? "" : l.Unit)}".Trim());
                        if (showMrp) C().Text(l.MrpPrice > 0 ? Num(l.MrpPrice) : "-");
                        C().Text(Num(rate));
                        if (showDisc) C().Text(l.DiscountAmount > 0 ? Num(l.DiscountAmount) : "-");
                        C().Text(Num(l.TaxableAmount));
                        C().Text($"{l.GstRate:0.##}%");
                        if (intra) { C().Text(Num(l.CgstAmount)); C().Text(Num(l.SgstAmount)); } else C().Text(Num(l.IgstAmount));
                        C().Text(Num(l.LineTotal)).SemiBold();
                        i++;
                    }
                });
                if (showMrp || showDisc)
                    col.Item().Text("Rate is per unit excluding GST, before discount. MRP is inclusive of all taxes.").FontSize(6.5f).Italic().FontColor("#6B7280");

                // Totals + words / HSN summary
                col.Item().Row(r =>
                {
                    r.RelativeItem().PaddingRight(10).Column(c =>
                    {
                        c.Spacing(6);
                        if (s.ShowAmountInWords)
                            c.Item().Column(w =>
                            {
                                w.Item().Text("Amount in words").FontSize(7.5f).FontColor("#6B7280");
                                w.Item().Text(AmountInWords.Rupees(inv.TotalAmount)).SemiBold();
                            });
                        if (s.ShowHsnSummary && lines.Count > 0)
                            c.Item().Element(e => HsnSummary(e, lines, intra, color));
                    });
                    r.ConstantItem(200).Column(c =>
                    {
                        void Row(string k, string v, bool bold = false, string? fc = null) => c.Item().PaddingVertical(1.5f).Row(x =>
                        {
                            x.RelativeItem().Text(k).FontColor(fc ?? "#374151").Bold();
                            x.AutoItem().Text(v).FontColor(fc ?? "#111827").SemiBold();
                        });
                        Row("Taxable value", Rs(inv.Subtotal));
                        if (intra)
                        {
                            Row("CGST", Rs(inv.CgstAmount));
                            Row("SGST", Rs(inv.SgstAmount));
                        }
                        else Row("IGST", Rs(inv.IgstAmount));
                        if (inv.DeliveryCharge > 0) Row("Delivery charges", Rs(inv.DeliveryCharge));
                        if (inv.RoundOff != 0) Row("Round off", (inv.RoundOff > 0 ? "+" : "−") + Rs(Math.Abs(inv.RoundOff)));
                        c.Item().PaddingTop(3).Background(color).Padding(6).Row(x =>
                        {
                            x.RelativeItem().Text("Grand Total").FontSize(10.5f).Bold().FontColor(Colors.White);
                            x.AutoItem().Text(Rs(inv.TotalAmount)).FontSize(10.5f).Bold().FontColor(Colors.White);
                        });
                        if (inv.DiscountAmount > 0)
                            c.Item().PaddingTop(3).AlignRight().Text(
                                $"You saved {Rs(inv.DiscountAmount)}" + (inv.CouponDiscount > 0 ? $" (incl. coupon {Rs(inv.CouponDiscount)})" : ""))
                                .FontSize(7.5f).FontColor("#15803D").SemiBold();
                    });
                });

                // Bank / UPI
                var bankLines = s.ShowBankDetails && !Blank(s.BankAccount)
                    ? new[] { ("Bank", s.BankName), ("A/c No", s.BankAccount), ("IFSC", s.BankIfsc), ("Branch", s.BankBranch) }.Where(x => !Blank(x.Item2)).ToList()
                    : new List<(string, string?)>();
                if (bankLines.Count > 0 || upiQr != null)
                    col.Item().Border(0.7f).BorderColor("#D1D5DB").Padding(6).Row(r =>
                    {
                        if (bankLines.Count > 0)
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Bank details").SemiBold().FontColor(color);
                                foreach (var (k, v) in bankLines) c.Item().Text(t => { t.Span(k + ": ").FontColor("#6B7280"); t.Span(v!); });
                            });
                        if (upiQr != null)
                            r.ConstantItem(210).Row(q =>
                            {
                                q.ConstantItem(64).Height(64).Image(upiQr);
                                q.ConstantItem(6);
                                q.RelativeItem().AlignMiddle().Column(c =>
                                {
                                    c.Item().Text("Scan & pay with any UPI app").SemiBold();
                                    c.Item().Text(s.UpiId!).FontSize(7.5f);
                                    c.Item().Text(Rs(inv.TotalAmount)).Bold().FontColor(color);
                                });
                            });
                    });

                if (!Blank(inv.Notes))
                    col.Item().Text(t => { t.Span("Notes: ").SemiBold(); t.Span(inv.Notes!); });

                // Terms + signature
                col.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().PaddingRight(10).Column(c =>
                    {
                        if (!Blank(s.TermsAndConditions))
                        {
                            c.Item().Text("Terms & Conditions").SemiBold().FontColor(color);
                            c.Item().Text(s.TermsAndConditions!).FontSize(7.5f);
                        }
                        if (!Blank(s.Declaration))
                        {
                            c.Item().PaddingTop(4).Text("Declaration").SemiBold().FontColor(color);
                            c.Item().Text(s.Declaration!).FontSize(7.5f);
                        }
                    });
                    r.ConstantItem(170).Column(c =>
                    {
                        c.Item().AlignRight().Text($"For {s.CompanyName}").SemiBold();
                        c.Item().Height(52).AlignRight().Row(x =>
                        {
                            if (stamp != null) x.ConstantItem(52).Element(e => SafeImage(e, stamp));
                            if (signature != null) x.ConstantItem(100).Element(e => SafeImage(e, signature));
                        });
                        c.Item().AlignRight().Text(Blank(s.AuthorisedSignatory) ? "Authorised Signatory" : s.AuthorisedSignatory!).SemiBold();
                        if (!Blank(s.AuthorisedSignatory)) c.Item().AlignRight().Text("Authorised Signatory").FontSize(7).FontColor("#6B7280");
                    });
                });
            });

            // ───────────── Footer ─────────────
            page.Footer().Column(c =>
            {
                c.Item().LineHorizontal(0.6f).LineColor("#D1D5DB");
                c.Item().PaddingTop(3).Row(r =>
                {
                    r.RelativeItem().Text(Blank(s.FooterText) ? $"Thank you for shopping with {s.CompanyName}!" : s.FooterText!).FontSize(7.5f).FontColor(color).SemiBold();
                    r.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(7).FontColor("#6B7280"));
                        t.Span("This is a computer-generated invoice.  Page ");
                        t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
                    });
                });
            });
        }));

        return doc.GeneratePdf();
    }

    // ───────────── pieces ─────────────

    private static void PartyBox(IContainer e, string label, string name, PdfAddress? a, string? phone, string? email, string? gstin, string color) =>
        e.Border(0.7f).BorderColor("#D1D5DB").Padding(6).Column(c =>
        {
            c.Item().Text(label).FontSize(7.5f).SemiBold().FontColor(color);
            c.Item().Text(string.IsNullOrWhiteSpace(name) ? "-" : name).Bold();
            if (a != null)
            {
                if (!Blank(a.Line)) c.Item().Text(a.Line!);
                var cityLine = JoinNonEmpty(", ", a.City, a.State) + (Blank(a.Pincode) ? "" : $" - {a.Pincode}");
                if (!Blank(cityLine)) c.Item().Text(cityLine);
            }
            if (!Blank(phone)) c.Item().Text("Ph: " + phone);
            if (!Blank(email)) c.Item().Text(email!);
            if (!Blank(gstin)) c.Item().Text("GSTIN: " + gstin!.ToUpperInvariant()).SemiBold();
        });

    private static void HsnSummary(IContainer e, List<InvoiceLineItem> lines, bool intra, string color)
    {
        var groups = lines.GroupBy(l => (Hsn: string.IsNullOrWhiteSpace(l.HsnCode) ? "-" : l.HsnCode!, l.GstRate))
            .Select(g => new { g.Key.Hsn, g.Key.GstRate, Taxable = g.Sum(x => x.TaxableAmount), Cgst = g.Sum(x => x.CgstAmount), Sgst = g.Sum(x => x.SgstAmount), Igst = g.Sum(x => x.IgstAmount) })
            .ToList();
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(); c.RelativeColumn(); c.ConstantColumn(34);
                if (intra) { c.RelativeColumn(); c.RelativeColumn(); } else c.RelativeColumn();
                c.RelativeColumn();
            });
            t.Header(h =>
            {
                void H(string x) => h.Cell().BorderBottom(0.7f).BorderColor(color).PaddingVertical(2).AlignRight().Text(x).FontSize(7).SemiBold().FontColor(color);
                H("HSN/SAC"); H("Taxable"); H("Rate");
                if (intra) { H("CGST"); H("SGST"); } else H("IGST");
                H("Total tax");
            });
            foreach (var g in groups)
            {
                void C(string x) => t.Cell().PaddingVertical(1.5f).AlignRight().Text(x).FontSize(7);
                C(g.Hsn); C(Num(g.Taxable)); C($"{g.GstRate:0.##}%");
                if (intra) { C(Num(g.Cgst)); C(Num(g.Sgst)); } else C(Num(g.Igst));
                C(Num(g.Cgst + g.Sgst + g.Igst));
            }
        });
    }

    private static void SafeImage(IContainer e, byte[] bytes)
    {
        try { e.Image(bytes).FitArea(); }
        catch { /* corrupt image — leave the space empty */ }
    }

    // ───────────── helpers ─────────────

    private sealed record PdfAddress(string? Name, string? Phone, string? Line, string? City, string? State, string? Pincode);

    private static PdfAddress? ParseAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                var r = doc.RootElement;
                string? G(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var line = JoinNonEmpty(", ", G("addressLine"), G("landmark"));
                return new PdfAddress(G("name"), G("phone"), line, G("city"), G("state"), G("pincode"));
            }
            catch { /* fall through: print as text */ }
        }
        return new PdfAddress(null, null, t, null, null, null);
    }

    /// <summary>data:image/...;base64 or a path under wwwroot (e.g. /uploads/...). Remote URLs are ignored.</summary>
    public static byte[]? LoadImage(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        try
        {
            src = src.Trim();
            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                return comma > 0 && src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase) ? Convert.FromBase64String(src[(comma + 1)..]) : null;
            }
            if (src.StartsWith('/') && !src.StartsWith("//"))
            {
                var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
                var full = Path.GetFullPath(Path.Combine(root, src.TrimStart('/').Split('?')[0]));
                if (full.StartsWith(root + Path.DirectorySeparatorChar) && File.Exists(full) && new FileInfo(full).Length < 3_000_000)
                    return File.ReadAllBytes(full);
            }
        }
        catch { /* invalid — ignore */ }
        return null;
    }

    private static byte[] Qr(string payload)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }

    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
    private static bool IsHex(string? c) => c != null && System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$");
    private static string JoinNonEmpty(string sep, params string?[] parts) => string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
    private static readonly CultureInfo InCulture = CultureInfo.GetCultureInfo("en-IN");
    private static string Num(decimal v) => v.ToString("#,##0.00", InCulture);
    private static string Rs(decimal v) => "₹" + Num(v);

    private sealed class IST
    {
        private readonly TimeZoneInfo _tz = FindTz();
        private static TimeZoneInfo FindTz()
        {
            foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
        }
        public DateTime ToLocal(DateTime d) =>
            d.Kind == DateTimeKind.Local ? d : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), _tz);
    }
}

/// <summary>Indian-system amount in words: "Rupees One Lakh Twenty Thousand and Fifty Paise Only".</summary>
public static class AmountInWords
{
    private static readonly string[] Ones = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
    private static readonly string[] Tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

    public static string Rupees(decimal amount)
    {
        amount = Math.Round(Math.Abs(amount), 2);
        var rupees = (long)Math.Floor(amount);
        var paise = (int)Math.Round((amount - rupees) * 100);
        var sb = new StringBuilder("Rupees ");
        sb.Append(rupees == 0 ? "Zero" : Words(rupees));
        if (paise > 0) sb.Append(" and ").Append(Words(paise)).Append(" Paise");
        return sb.Append(" Only").ToString();
    }

    private static string Words(long n)
    {
        var parts = new List<string>();
        void Add(long v, string unit) { if (v > 0) parts.Add(Below100Words((int)v) + (unit.Length > 0 ? " " + unit : "")); }
        var crore = n / 10_000_000; n %= 10_000_000;
        if (crore > 0) parts.Add(Words(crore) + " Crore");
        Add(n / 100_000, "Lakh"); n %= 100_000;
        Add(n / 1000, "Thousand"); n %= 1000;
        Add(n / 100, "Hundred"); n %= 100;
        if (n > 0) parts.Add(Below100Words((int)n));
        return string.Join(" ", parts);
    }

    private static string Below100Words(int n) => n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
}
