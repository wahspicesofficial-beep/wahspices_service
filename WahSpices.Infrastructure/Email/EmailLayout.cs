using System.Globalization;
using System.Net;
using System.Text;

namespace WahSpices.Infrastructure.Email;

public sealed record RenderedEmail(string Subject, string Html, string Text);

public enum Tone { Info, Success, Warning, Danger, Neutral }

/// <summary>
/// Building blocks for WAH! Spices emails: table-based, 600px, fully inline-styled (the small &lt;style&gt; block only adds
/// mobile tweaks as progressive enhancement), no flexbox/grid/JS, bulletproof buttons. Every helper that takes plain text
/// HTML-encodes it; helpers named *Html take markup the caller already built from encoded parts.
/// </summary>
public static class EmailLayout
{
    // ── Brand palette ─────────────────────────────────────────────
    public const string Green = "#1a5c2e", GreenDark = "#0f3d1e", Orange = "#f5a623", Red = "#c8371e";
    public const string Cream = "#fff9f0", Sand = "#f5e8d0", Ink = "#1d2a20", Muted = "#6d736b", Line = "#efe3cf";
    public const string Font = "'Poppins', Arial, Helvetica, sans-serif";

    private static readonly CultureInfo InCulture = CultureInfo.GetCultureInfo("en-IN");
    private static readonly TimeZoneInfo Ist = FindIst();

    private static TimeZoneInfo FindIst()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }

    // ── Formatting ────────────────────────────────────────────────
    public static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
    /// <summary>Encode and keep line breaks.</summary>
    public static string HMultiline(string? s) => H(s).Replace("\r\n", "\n").Replace("\n", "<br>");
    public static string Attr(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    public static string Money(decimal v) =>
        "₹" + (v == Math.Round(v) ? v.ToString("#,##,##0", InCulture) : v.ToString("#,##,##0.00", InCulture));

    public static DateTime ToIst(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ist);
    public static string Date(DateTime utc) => ToIst(utc).ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string DateTimeIst(DateTime utc) => ToIst(utc).ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture) + " IST";

    // ── Text blocks ───────────────────────────────────────────────
    /// <summary>Emoji badge, small uppercase eyebrow, big heading and an optional intro paragraph (HTML).</summary>
    public static string Hero(string emoji, string? eyebrow, string title, string? introHtml = null, Tone tone = Tone.Success)
    {
        var (bg, fg) = ToneColors(tone);
        var sb = new StringBuilder();
        sb.Append($@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td align=""center"" style=""padding:0 0 6px"">");
        sb.Append($@"<!--notext--><table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td align=""center"" valign=""middle"" width=""64"" height=""64"" style=""width:64px;height:64px;border-radius:32px;background:{bg};font-size:30px;line-height:64px;text-align:center"">{emoji}</td></tr></table><!--/notext-->");
        if (!string.IsNullOrWhiteSpace(eyebrow))
            sb.Append($@"<div style=""font-family:{Font};font-size:12px;line-height:16px;font-weight:700;letter-spacing:1.5px;text-transform:uppercase;color:{fg};padding-top:16px"">{H(eyebrow)}</div>");
        sb.Append($@"<h1 class=""h1"" style=""margin:0;padding-top:8px;font-family:{Font};font-size:26px;line-height:34px;font-weight:700;color:{Ink}"">{H(title)}</h1>");
        if (!string.IsNullOrWhiteSpace(introHtml))
            sb.Append($@"<p style=""margin:12px 0 0;font-family:{Font};font-size:15px;line-height:24px;color:{Muted}"">{introHtml}</p>");
        sb.Append("</td></tr></table>");
        return sb.ToString();
    }

    public static string P(string text, string? style = null) => PHtml(H(text), style);
    public static string PHtml(string html, string? style = null) =>
        $@"<p style=""margin:0 0 14px;font-family:{Font};font-size:15px;line-height:24px;color:{Ink};{style}"">{html}</p>";
    public static string Small(string html, string align = "left") =>
        $@"<p style=""margin:0 0 10px;font-family:{Font};font-size:12px;line-height:18px;color:{Muted};text-align:{align}"">{html}</p>";
    public static string B(string? text) => $"<strong style=\"color:{Ink}\">{H(text)}</strong>";
    public static string A(string url, string text, string color = Green) =>
        $@"<a href=""{Attr(url)}"" style=""color:{color};font-weight:600;text-decoration:underline"">{H(text)}</a>";

    public static string SectionTitle(string text) =>
        $@"<div style=""font-family:{Font};font-size:12px;line-height:16px;font-weight:700;letter-spacing:1.2px;text-transform:uppercase;color:{Green};padding:22px 0 10px"">{H(text)}</div>";

    public static string Spacer(int px) => $@"<div style=""height:{px}px;line-height:{px}px;font-size:0"">&nbsp;</div>";
    public static string Divider() => $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td style=""padding:18px 0""><div style=""height:1px;line-height:1px;font-size:0;background:{Line}"">&nbsp;</div></td></tr></table>";

    // ── Buttons (bulletproof: padded table cell + inline-block link; stack on mobile) ──
    public static string Button(string text, string url, bool primary = true, string? color = null)
    {
        var bg = color ?? (primary ? Green : "#ffffff");
        var fg = primary ? "#ffffff" : Green;
        var border = primary ? bg : Green;
        return $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" class=""btn"" style=""margin:0 auto;border-collapse:separate""><tr><td align=""center"" bgcolor=""{bg}"" style=""border-radius:10px;background:{bg};border:2px solid {border}"">" +
               $@"<a href=""{Attr(url)}"" target=""_blank"" style=""display:inline-block;padding:12px 24px;font-family:{Font};font-size:15px;line-height:20px;font-weight:600;color:{fg};text-decoration:none;border-radius:10px;mso-padding-alt:0"">{H(text)}</a></td></tr></table>";
    }

    /// <summary>One or two buttons side by side (they stack full-width on phones).</summary>
    public static string Buttons(params (string Text, string Url, bool Primary)[] buttons)
    {
        var list = buttons.Where(b => !string.IsNullOrWhiteSpace(b.Url)).ToList();
        if (list.Count == 0) return "";
        var sb = new StringBuilder($@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""center"" style=""margin:24px auto 8px""><tr>");
        for (var i = 0; i < list.Count; i++)
        {
            var pad = list.Count > 1 ? (i == 0 ? "padding:0 6px 0 0" : "padding:0 0 0 6px") : "padding:0";
            sb.Append($@"<td class=""btn-cell"" align=""center"" valign=""top"" style=""{pad}"">{Button(list[i].Text, list[i].Url, list[i].Primary)}</td>");
        }
        sb.Append("</tr></table>");
        return sb.ToString();
    }

    // ── Boxes ─────────────────────────────────────────────────────
    public static (string Bg, string Fg) ToneColors(Tone t) => t switch
    {
        Tone.Success => ("#e8f3ec", Green),
        Tone.Warning => ("#fdf1dc", "#9a5b00"),
        Tone.Danger  => ("#fbe9e5", Red),
        Tone.Neutral => ("#f4f1ec", Muted),
        _            => (Cream, GreenDark),
    };

    public static string Callout(string html, Tone tone = Tone.Info, string? title = null)
    {
        var (bg, fg) = ToneColors(tone);
        var t = string.IsNullOrWhiteSpace(title) ? "" :
            $@"<div style=""font-family:{Font};font-size:14px;line-height:20px;font-weight:700;color:{fg};padding-bottom:4px"">{H(title)}</div>";
        return $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:18px 0;border-collapse:separate""><tr><td style=""background:{bg};border-left:4px solid {fg};border-radius:10px;padding:14px 16px"">{t}<div style=""font-family:{Font};font-size:14px;line-height:22px;color:{Ink}"">{html}</div></td></tr></table>";
    }

    /// <summary>Label / value rows (values are HTML).</summary>
    public static string KeyValues(IEnumerable<(string Key, string ValueHtml)> rows, bool boxed = true)
    {
        var sb = new StringBuilder();
        var style = boxed ? $"background:{Cream};border:1px solid {Line};border-radius:12px;" : "";
        sb.Append($@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""{style}margin:8px 0;border-collapse:separate""><tr><td style=""padding:{(boxed ? "8px 16px" : "0")}"">");
        sb.Append(@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">");
        foreach (var (k, v) in rows)
            sb.Append($@"<tr><td valign=""top"" style=""padding:7px 12px 7px 0;font-family:{Font};font-size:13px;line-height:20px;color:{Muted};width:42%"">{H(k)}</td><td valign=""top"" style=""padding:7px 0;font-family:{Font};font-size:14px;line-height:20px;color:{Ink};font-weight:600"">{v}</td></tr>");
        sb.Append("</table></td></tr></table>");
        return sb.ToString();
    }

    /// <summary>Two columns on desktop, stacked on phones.</summary>
    public static string TwoColumns(string leftHtml, string rightHtml) =>
        $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:8px 0""><tr>" +
        $@"<td class=""stack"" valign=""top"" width=""50%"" style=""width:50%;padding:0 8px 0 0"">{leftHtml}</td>" +
        $@"<td class=""stack stack-gap"" valign=""top"" width=""50%"" style=""width:50%;padding:0 0 0 8px"">{rightHtml}</td></tr></table>";

    public static string InfoCard(string title, string bodyHtml) =>
        $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:{Cream};border:1px solid {Line};border-radius:12px;border-collapse:separate""><tr><td style=""padding:14px 16px"">" +
        $@"<div style=""font-family:{Font};font-size:11px;line-height:16px;font-weight:700;letter-spacing:1.2px;text-transform:uppercase;color:{Green};padding-bottom:6px"">{H(title)}</div>" +
        $@"<div style=""font-family:{Font};font-size:14px;line-height:21px;color:{Ink}"">{bodyHtml}</div></td></tr></table>";

    /// <summary>Big, readable one-time code.</summary>
    public static string CodeBox(string code, string caption) =>
        $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""center"" style=""margin:24px auto 8px;border-collapse:separate""><tr><td align=""center"" style=""background:{Cream};border:2px dashed {Green};border-radius:14px;padding:18px 28px"">" +
        $@"<div class=""otp"" style=""font-family:'Courier New',Courier,monospace;font-size:38px;line-height:44px;font-weight:700;letter-spacing:12px;color:{GreenDark};padding-left:12px"">{H(code)}</div>" +
        $@"<div style=""font-family:{Font};font-size:12px;line-height:18px;color:{Muted};padding-top:6px"">{H(caption)}</div></td></tr></table>";

    // ── Order building blocks ─────────────────────────────────────
    public static readonly string[] TrackerSteps = { "Placed", "Confirmed", "Packed", "Shipped", "Out for delivery", "Delivered" };

    /// <summary>Six-step progress tracker; <paramref name="current"/> is the index of the active step (0–5).</summary>
    public static string Tracker(int current, IReadOnlyList<string>? labels = null)
    {
        var steps = labels ?? TrackerSteps;
        current = Math.Clamp(current, 0, steps.Count - 1);
        var sb = new StringBuilder();
        sb.Append($@"<!--notext--><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:22px 0 6px;background:{Cream};border-radius:12px""><tr><td style=""padding:16px 6px 12px""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""table-layout:fixed""><tr>");
        for (var i = 0; i < steps.Count; i++)
        {
            var finished = current == steps.Count - 1;   // last step reached = journey complete, show it as done
            var done = i < current || finished; var active = i == current && !finished;
            var left = i == 0 ? "transparent" : (i <= current ? Green : "#e6dccb");
            var right = i == steps.Count - 1 ? "transparent" : (i < current ? Green : "#e6dccb");
            var (bg, fg, mark) = done ? (Green, "#ffffff", "&#10003;") : active ? (Orange, "#ffffff", "&#9679;") : ("#ffffff", "#b5ad9f", (i + 1).ToString());
            var ring = done || active ? $"border:2px solid {bg}" : "border:2px solid #e6dccb";
            var labelColor = active ? GreenDark : done ? Green : "#a39b8d";
            var weight = active ? "700" : "600";
            sb.Append($@"<td align=""center"" valign=""top"" style=""padding:0"">" +
                      $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr>" +
                      $@"<td style=""padding:0""><div style=""height:3px;line-height:3px;font-size:0;background:{left}"">&nbsp;</div></td>" +
                      $@"<td width=""26"" style=""padding:0;width:26px""><div style=""width:22px;height:22px;line-height:22px;border-radius:13px;{ring};background:{bg};color:{fg};font-family:Arial,Helvetica,sans-serif;font-size:12px;font-weight:700;text-align:center"">{mark}</div></td>" +
                      $@"<td style=""padding:0""><div style=""height:3px;line-height:3px;font-size:0;background:{right}"">&nbsp;</div></td>" +
                      "</tr></table>" +
                      $@"<div class=""trk"" style=""font-family:{Font};font-size:11px;line-height:14px;font-weight:{weight};color:{labelColor};padding:8px 2px 0"">{H(steps[i])}</div></td>");
        }
        sb.Append("</tr></table></td></tr></table><!--/notext-->");
        return sb.ToString();
    }

    public sealed record ItemRow(string Name, string? Detail, int Qty, decimal? UnitPrice, decimal LineTotal, string? ImageUrl, string? Url);

    /// <summary>Line items with thumbnails. <paramref name="detailed"/> shows "₹x each".</summary>
    public static string Items(IEnumerable<ItemRow> items, bool detailed)
    {
        var sb = new StringBuilder(@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">");
        foreach (var i in items)
        {
            var img = !string.IsNullOrWhiteSpace(i.ImageUrl)
                ? $@"<img src=""{Attr(i.ImageUrl)}"" width=""56"" height=""56"" alt=""{Attr(i.Name)}"" style=""display:block;width:56px;height:56px;border:1px solid {Line};border-radius:10px;background:{Cream};font-family:{Font};font-size:10px;color:{Muted};object-fit:cover"">"
                : $@"<div style=""width:56px;height:56px;line-height:56px;border-radius:10px;background:{Sand};text-align:center;font-size:24px"">&#127798;</div>";
            if (!string.IsNullOrWhiteSpace(i.Url)) img = $@"<a href=""{Attr(i.Url)}"" target=""_blank"" style=""text-decoration:none"">{img}</a>";
            img = "<!--notext-->" + img + "<!--/notext-->";
            var name = string.IsNullOrWhiteSpace(i.Url)
                ? $@"<span style=""font-family:{Font};font-size:14px;line-height:20px;font-weight:600;color:{Ink}"">{H(i.Name)}</span>"
                : $@"<a data-text-label=""1"" href=""{Attr(i.Url)}"" target=""_blank"" style=""font-family:{Font};font-size:14px;line-height:20px;font-weight:600;color:{Ink};text-decoration:none"">{H(i.Name)}</a>";
            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(i.Detail)) meta.Add(H(i.Detail));
            meta.Add($"Qty {i.Qty}");
            if (detailed && i.UnitPrice.HasValue) meta.Add($"{Money(i.UnitPrice.Value)} each");
            sb.Append($@"<tr><td width=""70"" valign=""top"" style=""width:70px;padding:12px 14px 12px 0;border-bottom:1px solid {Line}"">{img}</td>" +
                      $@"<td valign=""top"" style=""padding:14px 0 12px;border-bottom:1px solid {Line}"">{name}<div style=""font-family:{Font};font-size:13px;line-height:19px;color:{Muted};padding-top:3px"">{string.Join(" &middot; ", meta)}</div></td>" +
                      $@"<td align=""right"" valign=""top"" style=""padding:14px 0 12px 10px;border-bottom:1px solid {Line};font-family:{Font};font-size:14px;line-height:20px;font-weight:600;color:{Ink};white-space:nowrap"">{Money(i.LineTotal)}</td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>Totals block: rows of (label, value-html, isDiscount); the last line is the grand total.</summary>
    public static string Totals(IEnumerable<(string Label, string ValueHtml, bool Highlight)> rows, string grandLabel, string grandValue)
    {
        var sb = new StringBuilder($@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin-top:6px"">");
        foreach (var (label, value, hl) in rows)
            sb.Append($@"<tr><td style=""padding:5px 0;font-family:{Font};font-size:14px;line-height:20px;color:{Muted}"">{H(label)}</td><td align=""right"" style=""padding:5px 0;font-family:{Font};font-size:14px;line-height:20px;color:{(hl ? Green : Ink)};font-weight:{(hl ? "700" : "500")};white-space:nowrap"">{value}</td></tr>");
        sb.Append($@"<tr><td colspan=""2"" style=""padding:8px 0 0""><div style=""height:2px;line-height:2px;font-size:0;background:{Sand}"">&nbsp;</div></td></tr>");
        sb.Append($@"<tr><td style=""padding:10px 0 0;font-family:{Font};font-size:16px;line-height:22px;font-weight:700;color:{Ink}"">{H(grandLabel)}</td><td align=""right"" style=""padding:10px 0 0;font-family:{Font};font-size:20px;line-height:24px;font-weight:700;color:{GreenDark};white-space:nowrap"">{H(grandValue)}</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    public static string Pill(string text, Tone tone)
    {
        var (bg, fg) = ToneColors(tone);
        return $@"<span style=""display:inline-block;background:{bg};color:{fg};border-radius:20px;padding:2px 10px;font-family:{Font};font-size:12px;line-height:18px;font-weight:700"">{H(text)}</span>";
    }

    // ── Document shell ────────────────────────────────────────────
    /// <param name="reason">Completes "You're receiving this email because …".</param>
    /// <param name="footerHtml">Optional extra line under the "You're receiving this email because …" note (e.g. an unsubscribe link).</param>
    public static RenderedEmail Render(EmailBrand b, string subject, string preheader, string bodyHtml, string reason, bool showHelp = true, string? footerHtml = null)
    {
        var name = b.Name;
        var sp = name.IndexOf(' ');
        var wordmark = sp > 0
            ? $@"<span style=""color:{Orange}"">{H(name[..sp])}</span>{H(name[sp..])}"
            : $@"<span style=""color:#ffffff"">{H(name)}</span>";

        var help = showHelp ? HelpBlock(b) : "";
        var html = $@"<!DOCTYPE html>
<html lang=""en"" xmlns=""http://www.w3.org/1999/xhtml"" xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:o=""urn:schemas-microsoft-com:office:office"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
<meta name=""x-apple-disable-message-reformatting"">
<meta name=""format-detection"" content=""telephone=no,address=no,email=no,date=no"">
<meta name=""color-scheme"" content=""light"">
<meta name=""supported-color-schemes"" content=""light"">
<title>{H(subject)}</title>
<!--[if mso]><noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript><![endif]-->
<style>
  body {{ margin:0 !important; padding:0 !important; width:100% !important; -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }}
  table {{ border-collapse:collapse; mso-table-lspace:0pt; mso-table-rspace:0pt; }}
  img {{ -ms-interpolation-mode:bicubic; }}
  a[x-apple-data-detectors] {{ color:inherit !important; text-decoration:none !important; }}
  @media only screen and (max-width:620px) {{
    .wrap {{ width:100% !important; max-width:100% !important; }}
    .outer {{ padding:12px 8px !important; }}
    .px {{ padding-left:20px !important; padding-right:20px !important; }}
    .stack {{ display:block !important; width:100% !important; padding-left:0 !important; padding-right:0 !important; box-sizing:border-box; }}
    .stack-gap {{ padding-top:12px !important; }}
    .h1 {{ font-size:22px !important; line-height:30px !important; }}
    .otp {{ font-size:30px !important; letter-spacing:8px !important; }}
    .btn-cell {{ display:block !important; width:100% !important; padding:0 0 10px 0 !important; }}
    .btn {{ width:100% !important; }}
    .btn a {{ display:block !important; }}
    .trk {{ font-size:9px !important; line-height:12px !important; }}
    .hide-sm {{ display:none !important; }}
  }}
</style>
</head>
<body style=""margin:0;padding:0;background:{Sand};"">
<!--notext--><div style=""display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;color:{Sand}"">{H(preheader)}{string.Concat(Enumerable.Repeat("&#8199;&#65279;&#847; ", 40))}</div><!--/notext-->
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" bgcolor=""{Sand}"" style=""background:{Sand}"">
<tr><td class=""outer"" align=""center"" style=""padding:28px 12px"">
<!--[if mso]><table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td><![endif]-->
<table role=""presentation"" class=""wrap"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""width:600px;max-width:600px;background:#ffffff;border-radius:18px;overflow:hidden"">
  <tr><td align=""center"" bgcolor=""{Green}"" style=""background:{Green};padding:26px 24px 22px"">
    <!--notext--><a href=""{Attr(b.FrontendUrl)}"" target=""_blank"" style=""text-decoration:none""><img src=""{Attr(b.LogoUrl)}"" width=""64"" height=""64"" alt=""{Attr(name)} logo"" style=""display:block;margin:0 auto 10px;width:64px;height:64px;border:3px solid {Orange};border-radius:50%;background:{Cream};font-family:{Font};font-size:10px;line-height:14px;color:{GreenDark};text-align:center""></a><!--/notext-->
    <a href=""{Attr(b.FrontendUrl)}"" target=""_blank"" style=""text-decoration:none;font-family:{Font};font-size:28px;line-height:34px;font-weight:800;letter-spacing:0.5px;color:#ffffff"">{wordmark}</a>
    {(string.IsNullOrWhiteSpace(b.Tagline) ? "" : $@"<div style=""font-family:{Font};font-size:13px;line-height:18px;font-style:italic;color:{Sand};padding-top:4px"">{H(b.Tagline)}</div>")}
  </td></tr>
  <tr><td height=""5"" bgcolor=""{Orange}"" style=""height:5px;line-height:5px;font-size:0;background:{Orange}"">&nbsp;</td></tr>
  <tr><td class=""px"" style=""padding:32px 36px 12px;font-family:{Font};color:{Ink}"">
{bodyHtml}
  </td></tr>
{help}
  <tr><td class=""px"" bgcolor=""{GreenDark}"" style=""background:{GreenDark};padding:24px 36px 22px"">
{Footer(b)}
  </td></tr>
</table>
<!--[if mso]></td></tr></table><![endif]-->
<table role=""presentation"" class=""wrap"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""width:600px;max-width:600px"">
  <tr><td class=""px"" align=""center"" style=""padding:16px 28px 4px;font-family:{Font};font-size:11px;line-height:17px;color:{Muted}"">
    You're receiving this email because {H(reason)}<br>{(string.IsNullOrWhiteSpace(footerHtml) ? "" : footerHtml + "<br>")}
    &copy; {DateTime.UtcNow.Year} {H(name)}. All rights reserved.
  </td></tr>
</table>
</td></tr>
</table>
</body>
</html>";
        return new RenderedEmail(subject, html, HtmlToText.Convert(html));
    }

    private static string HelpBlock(EmailBrand b)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(b.Phone)) parts.Add($@"call <a href=""{Attr(b.PhoneHref)}"" style=""color:{Green};font-weight:600;text-decoration:underline;white-space:nowrap"">{H(b.Phone)}</a>");
        if (!string.IsNullOrWhiteSpace(b.Email)) parts.Add($@"write to {A("mailto:" + b.Email, b.Email)}");
        var how = parts.Count == 0 ? $"visit {A(b.Link("/help"), "our Help Centre")}" : string.Join(" or ", parts);
        return $@"  <tr><td class=""px"" style=""padding:18px 36px 30px"">
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:{Cream};border:1px solid {Line};border-radius:12px;border-collapse:separate""><tr><td style=""padding:14px 16px;font-family:{Font};font-size:13px;line-height:20px;color:{Muted}"">
      <strong style=""color:{Ink}"">Need help?</strong> We're happy to help — {how}. You can also visit our {A(b.Link("/help"), "Help Centre")}.
    </td></tr></table>
  </td></tr>";
    }

    private static string Footer(EmailBrand b)
    {
        var fl = $"font-family:{Font};font-size:12px;line-height:19px;color:#cfe0d3";
        var link = "color:#ffffff;text-decoration:underline";
        var sb = new StringBuilder();
        sb.Append($@"<div style=""font-family:{Font};font-size:15px;line-height:22px;font-weight:700;color:#ffffff"">{H(b.Name)}</div>");
        if (!string.IsNullOrWhiteSpace(b.Address)) sb.Append($@"<div style=""{fl};padding-top:4px"">{H(b.Address)}</div>");
        var contact = new List<string>();
        if (!string.IsNullOrWhiteSpace(b.Phone)) contact.Add($@"<a href=""{Attr(b.PhoneHref)}"" style=""{link}"">{H(b.Phone)}</a>");
        if (!string.IsNullOrWhiteSpace(b.Email)) contact.Add($@"<a href=""mailto:{Attr(b.Email)}"" style=""{link}"">{H(b.Email)}</a>");
        if (contact.Count > 0) sb.Append($@"<div style=""{fl};padding-top:4px"">{string.Join(" &nbsp;&middot;&nbsp; ", contact)}</div>");
        var legal = new List<string>();
        if (!string.IsNullOrWhiteSpace(b.Fssai)) legal.Add($"FSSAI Lic. No. {H(b.Fssai)}");
        if (!string.IsNullOrWhiteSpace(b.Udyam)) legal.Add($"Udyam Reg. No. {H(b.Udyam)}");
        if (!string.IsNullOrWhiteSpace(b.Gstin)) legal.Add($"GSTIN {H(b.Gstin)}");
        if (legal.Count > 0) sb.Append($@"<div style=""{fl};padding-top:8px;color:#a9c4b0"">{string.Join(" &nbsp;|&nbsp; ", legal)}</div>");

        var links = new List<string>
        {
            $@"<a href=""{Attr(b.Link("/products"))}"" style=""{link}"">Shop</a>",
            $@"<a href=""{Attr(b.Link("/track-order"))}"" style=""{link}"">Track order</a>",
            $@"<a href=""{Attr(b.Link("/help"))}"" style=""{link}"">Help</a>",
        };
        foreach (var (platform, url, handle) in b.Social)
            links.Add($@"<a href=""{Attr(url)}"" target=""_blank"" style=""{link}"">{H(platform)}{(string.IsNullOrWhiteSpace(handle) ? "" : " " + H(handle))}</a>");
        sb.Append($@"<div style=""{fl};padding-top:12px"">{string.Join(" &nbsp;&middot;&nbsp; ", links)}</div>");
        return sb.ToString();
    }
}
