using System.Globalization;
using System.Text;
using static WahSpices.Infrastructure.Email.EmailLayout;

namespace WahSpices.Infrastructure.Email;

/// <summary>One row of the compliance alert email.</summary>
public sealed record ComplianceAlertLine(string Kind, string Title, string Category, string? Number, DateTime ExpiryDate, int DaysLeft);

/// <summary>Store-inbox email for licences / registrations (and asset warranties) that are expiring or expired.</summary>
public static class CompanyAlertEmails
{
    private static string When(int days) => days switch
    {
        < -1 => $"Expired {-days} days ago",
        -1   => "Expired yesterday",
        0    => "Expires today",
        1    => "Expires tomorrow",
        _    => $"Expires in {days} days",
    };

    private static Tone ToneFor(int days) => days <= 0 ? Tone.Danger : days <= 7 ? Tone.Warning : Tone.Info;

    public static RenderedEmail ComplianceAlert(EmailBrand b, IReadOnlyList<ComplianceAlertLine> items)
    {
        var expired = items.Count(i => i.DaysLeft < 0 || (i.DaysLeft == 0 && i.Kind == "Licence"));
        var lic = items.Where(i => i.Kind == "Licence").OrderBy(i => i.DaysLeft).ToList();
        var war = items.Where(i => i.Kind != "Licence").OrderBy(i => i.DaysLeft).ToList();
        var first = items.OrderBy(i => i.DaysLeft).First();

        var title = items.Count == 1
            ? $"{first.Title}: {When(first.DaysLeft).ToLowerInvariant()}"
            : $"{items.Count} renewals need attention";
        var intro = expired > 0
            ? $"<strong>{expired}</strong> item(s) have expired or expire today — renew them to avoid penalties or interruptions."
            : "These licences, registrations or warranties are coming up for renewal.";

        var sb = new StringBuilder();
        sb.Append(Hero("&#128197;", "Company compliance", title, intro, expired > 0 ? Tone.Danger : Tone.Warning));
        if (lic.Count > 0) { sb.Append(SectionTitle("Licences & registrations")); foreach (var i in lic) sb.Append(Row(i)); }
        if (war.Count > 0) { sb.Append(SectionTitle("Asset warranties")); foreach (var i in war) sb.Append(Row(i)); }
        sb.Append(Buttons(("Open licences & alerts", b.Link("/admin/company/licences"), true)));
        sb.Append(Small("After renewing, use <strong>Renew</strong> on the licence in Admin &rarr; Company so the new period is tracked and these reminders stop."));

        var subject = items.Count == 1
            ? $"Renewal alert: {first.Title} — {When(first.DaysLeft).ToLowerInvariant()}"
            : $"Renewal alert: {items.Count} licences/warranties need attention";
        var pre = string.Join(" · ", items.OrderBy(i => i.DaysLeft).Take(3).Select(i => $"{i.Title} ({When(i.DaysLeft).ToLowerInvariant()})"));
        return Render(b, subject, pre, sb.ToString(), "this is the store inbox set in Admin → Settings and renewal alerts are sent from Admin → Company.", showHelp: false);
    }

    private static string Row(ComplianceAlertLine i)
    {
        var date = i.ExpiryDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var rows = new List<(string, string)>
        {
            ("Type", H(i.Category)),
            ("Expiry date", H(date)),
            ("Status", Pill(When(i.DaysLeft), ToneFor(i.DaysLeft))),
        };
        if (!string.IsNullOrWhiteSpace(i.Number)) rows.Insert(1, (i.Kind == "Licence" ? "Number" : "Serial no.", H(i.Number)));
        return Callout(KeyValues(rows, boxed: false), ToneFor(i.DaysLeft), i.Title);
    }
}
