namespace WahSpices.Application.Interfaces.Services;

/// <summary>A ready-to-send email. <see cref="Text"/> is the plain-text alternative (multipart/alternative).</summary>
public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string Html, string? Text = null, string? Category = null)
{
    /// <summary>Files attached to the email (e.g. the invoice PDF).</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = Array.Empty<EmailAttachment>();

    /// <summary>Extra MIME headers (e.g. List-Unsubscribe on marketing mail). Names/values must be plain ASCII.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/pdf");

public interface IEmailService
{
    /// <summary>Sends immediately (SMTP or Outbox mode). Throws when the server rejects the message.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);

    Task SendAsync(string toEmail, string toName, string subject, string htmlBody) =>
        SendAsync(new EmailMessage(toEmail, toName, subject, htmlBody));
}
