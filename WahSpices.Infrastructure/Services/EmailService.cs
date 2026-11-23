using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Infrastructure.Email;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// Low-level sender. Sends multipart/alternative (text/plain + text/html) over SMTP, or — with
/// Email:DeliveryMode=Outbox — writes the HTML (and a .txt twin) to Logs/outbox instead of sending.
/// Most callers don't use this directly: they enqueue through <see cref="IEmailQueue"/> so requests never wait on SMTP.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var host      = _config["Email:Host"]      ?? "smtp.gmail.com";
        var port      = _config.GetValue<int>("Email:Port", 587);
        var enableSsl = _config.GetValue<bool>("Email:EnableSsl", true);
        var username  = _config["Email:Username"];
        var password  = _config["Email:Password"];
        var fromEmail = _config["Email:FromEmail"] ?? username;
        var fromName  = _config["Email:FromName"]  ?? "WAH! Spices";
        var text      = string.IsNullOrWhiteSpace(message.Text) ? HtmlToText.Convert(message.Html) : message.Text;

        // Development safety: "Outbox" mode writes the email to Logs/outbox instead of sending it
        if (string.Equals(_config["Email:DeliveryMode"], "Outbox", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "outbox");
            Directory.CreateDirectory(dir);
            var safeTo = string.Concat(message.ToEmail.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{safeTo}.html");
            var header = $"<!-- To: {message.ToName} <{message.ToEmail}> | From: {fromName} <{fromEmail}> | Subject: {message.Subject} -->\n";
            await File.WriteAllTextAsync(file, header + message.Html, ct);
            await File.WriteAllTextAsync(Path.ChangeExtension(file, ".txt"), $"Subject: {message.Subject}\n\n{text}", ct);
            foreach (var a in message.Attachments)
                await File.WriteAllBytesAsync(Path.ChangeExtension(file, null) + "_" + string.Concat(a.FileName.Split(Path.GetInvalidFileNameChars())), a.Content, ct);
            _logger.LogInformation("Email (outbox mode) to {Email}: {Subject} -> {File}", message.ToEmail, message.Subject, file);
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)
            || username == "YOUR_GMAIL@gmail.com")
        {
            _logger.LogWarning("Email not configured — skipping send to {Email}. Set Email:Username and Email:Password in appsettings.", message.ToEmail);
            return;
        }

        using var smtp = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl    = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000,
        };

        using var msg = new MailMessage
        {
            From            = new MailAddress(fromEmail!, fromName),
            Subject         = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            BodyEncoding    = Encoding.UTF8,
        };
        msg.To.Add(new MailAddress(message.ToEmail, message.ToName));
        if (message.Headers != null)
            foreach (var (name, value) in message.Headers)
                if (!string.IsNullOrWhiteSpace(name) && value != null && !name.Contains(':') && !value.Contains('\r') && !value.Contains('\n'))
                    msg.Headers.Add(name, value);
        // multipart/alternative: plain text first, HTML last (clients show the last part they support)
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(text, Encoding.UTF8, MediaTypeNames.Text.Plain));
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Html, Encoding.UTF8, MediaTypeNames.Text.Html));
        foreach (var a in message.Attachments)
            msg.Attachments.Add(new Attachment(new MemoryStream(a.Content), a.FileName, a.ContentType));

        try
        {
            await smtp.SendMailAsync(msg, ct);
            _logger.LogInformation("Email sent to {Email}: {Subject}", message.ToEmail, message.Subject);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP error sending to {Email}", message.ToEmail);
            throw;
        }
    }
}
