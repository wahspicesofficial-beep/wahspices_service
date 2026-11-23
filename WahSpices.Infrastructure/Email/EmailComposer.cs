using Microsoft.Extensions.Logging;
using WahSpices.Application.Interfaces.Services;

namespace WahSpices.Infrastructure.Email;

/// <summary>
/// Renders a template with the current business details and either queues it (customer notifications — never blocks
/// or fails the request) or sends it right away (admin actions that must report success/failure, e.g. "Reply" buttons).
/// </summary>
public interface IEmailComposer
{
    Task<EmailBrand> BrandAsync(CancellationToken ct = default);

    /// <summary>Render now, deliver in the background with retries. Never throws.</summary>
    Task QueueAsync(string toEmail, string toName, string category, Func<EmailBrand, RenderedEmail> build, IReadOnlyList<EmailAttachment>? attachments = null);

    /// <summary>Render and send synchronously (throws if SMTP rejects it).</summary>
    Task SendNowAsync(string toEmail, string toName, string category, Func<EmailBrand, RenderedEmail> build, CancellationToken ct = default, IReadOnlyList<EmailAttachment>? attachments = null);
}

public sealed class EmailComposer : IEmailComposer
{
    private readonly IEmailBrandProvider _brand;
    private readonly IEmailQueue _queue;
    private readonly IEmailService _email;
    private readonly ILogger<EmailComposer> _log;
    private readonly ITransactionalEmailCounter? _counter;

    public EmailComposer(IEmailBrandProvider brand, IEmailQueue queue, IEmailService email, ILogger<EmailComposer> log, ITransactionalEmailCounter? counter = null)
    {
        _brand = brand; _queue = queue; _email = email; _log = log; _counter = counter;
    }

    public Task<EmailBrand> BrandAsync(CancellationToken ct = default) => _brand.GetAsync(ct);

    public async Task QueueAsync(string toEmail, string toName, string category, Func<EmailBrand, RenderedEmail> build, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        try
        {
            if (!EmailAddressRules.IsDeliverable(toEmail)) { _log.LogInformation("Email [{Category}] skipped — no deliverable address", category); return; }
            var r = build(await _brand.GetAsync());
            _queue.Enqueue(EmailJob.Ready(category, new EmailMessage(toEmail, toName, r.Subject, r.Html, r.Text, category) { Attachments = attachments ?? Array.Empty<EmailAttachment>() }));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not compose email [{Category}]", category);
        }
    }

    public async Task SendNowAsync(string toEmail, string toName, string category, Func<EmailBrand, RenderedEmail> build, CancellationToken ct = default, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var r = build(await _brand.GetAsync(ct));
        await _email.SendAsync(new EmailMessage(toEmail, toName, r.Subject, r.Html, r.Text, category) { Attachments = attachments ?? Array.Empty<EmailAttachment>() }, ct);
        _counter?.Record();   // counts against the Gmail daily budget the campaign throttle shares
    }
}

public static class EmailAddressRules
{
    /// <summary>False for empty addresses and the placeholder "{10 digits}@wahspices.com" created for phone-OTP sign-ups.</summary>
    public static bool IsDeliverable(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return false;
        var e = email.Trim();
        var at = e.IndexOf('@');
        var local = e[..at];
        return !(e.EndsWith("@wahspices.com", StringComparison.OrdinalIgnoreCase) && local.Length >= 10 && local.All(char.IsDigit));
    }
}
