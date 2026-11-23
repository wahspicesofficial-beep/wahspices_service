using System.Text;
using static WahSpices.Infrastructure.Email.EmailLayout;

namespace WahSpices.Infrastructure.Email;

/// <summary>A product card shown in a campaign email.</summary>
public sealed record CampaignEmailProduct(string Name, string? Weight, decimal Price, decimal Mrp, string? ImagePath, string? Slug);

/// <summary>Everything the campaign template needs. All text is plain text (HTML-encoded on render).</summary>
public sealed class CampaignEmailModel
{
    public string TypeLabel { get; init; } = "Offer";
    public string TypeValue { get; init; } = "Offer";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? ImagePath { get; init; }
    public string? ButtonLabel { get; init; }
    /// <summary>Site path ("/products") or absolute https URL — already validated by the caller.</summary>
    public string? ButtonUrl { get; init; }
    public string? CouponCode { get; init; }
    public IReadOnlyList<CampaignEmailProduct> Products { get; init; } = Array.Empty<CampaignEmailProduct>();
    public string RecipientName { get; init; } = "";
    /// <summary>Signed one-click unsubscribe page. Null only in admin previews.</summary>
    public string? UnsubscribeUrl { get; init; }
}

/// <summary>
/// Branded marketing email (offers, launches, sales, announcements). Same shell as the transactional templates
/// (EmailLayout.Render) plus: optional full-width image, coupon code box, product cards, one call-to-action
/// button and a visible unsubscribe link in the footer.
/// </summary>
public static class CampaignEmails
{
    private const string Reason = "you have an account with us and haven't opted out of offers and product news.";

    private static string Emoji(string type) => type.ToLowerInvariant() switch
    {
        "newproduct" or "launch" => "&#127881;",   // party popper
        "sale" => "&#128293;",                    // fire
        "announcement" => "&#128227;",            // megaphone
        _ => "&#127873;",                         // gift
    };

    public static string ResolveLink(EmailBrand b, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var u = url.Trim();
        return u.StartsWith('/') ? b.Link(u) : u;
    }

    public static RenderedEmail Render(EmailBrand b, CampaignEmailModel m)
    {
        var sb = new StringBuilder();

        var image = b.Asset(m.ImagePath);
        if (!string.IsNullOrWhiteSpace(image))
            sb.Append($@"<!--notext--><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:0 0 22px""><tr><td align=""center"">" +
                      $@"<img src=""{Attr(image)}"" width=""528"" alt=""{Attr(m.Title)}"" style=""display:block;width:100%;max-width:528px;height:auto;border:0;border-radius:14px;background:{Cream}""></td></tr></table><!--/notext-->");

        var first = FirstName(m.RecipientName);
        sb.Append(Hero(Emoji(m.TypeValue), m.TypeLabel, m.Title,
            string.IsNullOrWhiteSpace(first) ? null : $"Hi {H(first)},", Tone.Success));
        sb.Append(Spacer(14));

        foreach (var para in SplitParagraphs(m.Body))
            sb.Append(PHtml(HMultiline(para)));

        if (!string.IsNullOrWhiteSpace(m.CouponCode))
            sb.Append(CodeBox(m.CouponCode.Trim().ToUpperInvariant(), "Use this code at checkout"));

        if (m.Products.Count > 0)
        {
            sb.Append(SectionTitle(m.Products.Count == 1 ? "Featured product" : "Featured products"));
            sb.Append(ProductCards(b, m.Products));
        }

        var buttonUrl = ResolveLink(b, m.ButtonUrl);
        if (!string.IsNullOrWhiteSpace(buttonUrl))
            sb.Append(Buttons((string.IsNullOrWhiteSpace(m.ButtonLabel) ? "Shop now" : m.ButtonLabel!, buttonUrl, true)));
        else if (m.Products.Count == 0)
            sb.Append(Buttons(("Shop now", b.Link("/products"), true)));

        var footer = string.IsNullOrWhiteSpace(m.UnsubscribeUrl)
            ? "Don't want these emails? <span style=\"text-decoration:underline\">Unsubscribe</span> (link added when sent)."
            : $@"Don't want these emails? <a href=""{Attr(m.UnsubscribeUrl)}"" target=""_blank"" style=""color:{Muted};text-decoration:underline"">Unsubscribe</a> &mdash; order and account emails will still reach you.";

        var preheader = Preheader(m.Body);
        return EmailLayout.Render(b, m.Title, preheader, sb.ToString(), Reason, showHelp: false, footerHtml: footer);
    }

    private static string ProductCards(EmailBrand b, IReadOnlyList<CampaignEmailProduct> products)
    {
        var sb = new StringBuilder();
        // two per row on desktop, stacked on phones
        for (var i = 0; i < products.Count; i += 2)
        {
            var left = Card(b, products[i]);
            var right = i + 1 < products.Count ? Card(b, products[i + 1]) : "";
            sb.Append(right == "" ? $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:8px 0""><tr><td class=""stack"" valign=""top"" width=""50%"" style=""width:50%;padding:0 8px 0 0"">{left}</td><td class=""stack hide-sm"" width=""50%"" style=""width:50%"">&nbsp;</td></tr></table>"
                                  : TwoColumns(left, right));
        }
        return sb.ToString();
    }

    private static string Card(EmailBrand b, CampaignEmailProduct p)
    {
        var url = string.IsNullOrWhiteSpace(p.Slug) ? b.Link("/products") : b.Link("/product/" + Uri.EscapeDataString(p.Slug));
        var img = b.Asset(p.ImagePath);
        var imgHtml = string.IsNullOrWhiteSpace(img) ? "" :
            $@"<!--notext--><a href=""{Attr(url)}"" target=""_blank"" style=""text-decoration:none""><img src=""{Attr(img)}"" width=""120"" height=""120"" alt=""{Attr(p.Name)}"" style=""display:block;margin:0 auto 10px;width:120px;height:120px;object-fit:contain;border:0;border-radius:10px;background:#ffffff""></a><!--/notext-->";
        var price = p.Mrp > p.Price
            ? $@"<strong style=""color:{GreenDark}"">{Money(p.Price)}</strong> <span style=""color:{Muted};text-decoration:line-through;font-size:12px"">{Money(p.Mrp)}</span>"
            : $@"<strong style=""color:{GreenDark}"">{Money(p.Price)}</strong>";
        return $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:{Cream};border:1px solid {Line};border-radius:12px;border-collapse:separate""><tr><td align=""center"" style=""padding:14px 12px;font-family:{Font}"">" +
               imgHtml +
               $@"<a href=""{Attr(url)}"" target=""_blank"" style=""font-family:{Font};font-size:14px;line-height:20px;font-weight:700;color:{Ink};text-decoration:none"">{H(p.Name)}</a>" +
               (string.IsNullOrWhiteSpace(p.Weight) ? "" : $@"<div style=""font-family:{Font};font-size:12px;line-height:18px;color:{Muted}"">{H(p.Weight)}</div>") +
               $@"<div style=""font-family:{Font};font-size:14px;line-height:22px;padding-top:4px"">{price}</div>" +
               "</td></tr></table>";
    }

    private static IEnumerable<string> SplitParagraphs(string body) =>
        (body ?? "").Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim('\n', ' '))
            .Where(p => p.Length > 0);

    private static string Preheader(string body)
    {
        var flat = string.Join(' ', (body ?? "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length > 110 ? flat[..110].TrimEnd() + "…" : flat;
    }

    private static string FirstName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return "";
        var sp = n.IndexOf(' ');
        return sp > 0 ? n[..sp] : n;
    }
}
