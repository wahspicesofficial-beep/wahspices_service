using System.Net;
using System.Text.RegularExpressions;

namespace WahSpices.Infrastructure.Email;

/// <summary>
/// Turns one of our email HTML documents into a readable plain-text alternative.
/// Blocks wrapped in &lt;!--notext--&gt;…&lt;!--/notext--&gt; (preheader, logo, decorative tracker) are dropped;
/// links become "label: url" so every button still works in text-only clients.
/// </summary>
public static class HtmlToText
{
    private static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;
    private static readonly Regex NoText   = new(@"<!--notext-->.*?<!--/notext-->", O);
    private static readonly Regex Head     = new(@"<head\b.*?</head>|<style\b.*?</style>|<script\b.*?</script>", O);
    private static readonly Regex Comments = new(@"<!--.*?-->", O);
    private static readonly Regex Anchor   = new(@"<a\b[^>]*?href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>", O);
    private static readonly Regex Breaks   = new(@"<br\s*/?>", O);
    private static readonly Regex BlockEnd = new(@"</(p|div|h[1-6]|tr|table|li|ul|ol)>", O);
    private static readonly Regex BlockStart = new(@"<(div|p|h[1-6]|tr)\b", O);
    private static readonly Regex Cell     = new(@"</t[dh]>", O);
    private static readonly Regex Li       = new(@"<li\b[^>]*>", O);
    private static readonly Regex Tags     = new(@"<[^>]+>", O);
    private static readonly Regex Spaces   = new(@"[ \t ]+", RegexOptions.Compiled);
    private static readonly Regex Blank    = new(@"\n{3,}", RegexOptions.Compiled);

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = NoText.Replace(html, "");
        s = Head.Replace(s, "");
        s = Comments.Replace(s, "");
        s = Anchor.Replace(s, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            var label = WebUtility.HtmlDecode(Tags.Replace(m.Groups[2].Value, "")).Trim();
            if (m.Value.Contains("data-text-label", StringComparison.Ordinal)) return label;   // label only (e.g. product names)
            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || href.Length == 0 || string.Equals(label, href, StringComparison.OrdinalIgnoreCase))
                return label.Length > 0 ? label : href;
            return label.Length > 0 ? $"{label}: {href}" : href;
        });
        s = Breaks.Replace(s, "\n");
        s = Li.Replace(s, "\n• ");
        s = BlockEnd.Replace(s, "\n");
        s = BlockStart.Replace(s, "\n$0");
        s = Cell.Replace(s, "  ");
        s = Tags.Replace(s, "");
        s = WebUtility.HtmlDecode(s).Replace("\r", "");
        var lines = s.Split('\n').Select(l => Spaces.Replace(l, " ").Trim());
        s = string.Join("\n", lines);
        return Blank.Replace(s, "\n\n").Trim() + "\n";
    }
}
