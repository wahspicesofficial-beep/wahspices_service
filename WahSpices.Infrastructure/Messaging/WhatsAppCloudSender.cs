using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WahSpices.Infrastructure.Messaging;

/// <summary>How one send attempt ended — drives retry / pause decisions in the campaign dispatcher.</summary>
public enum SendOutcomeKind
{
    Sent,
    /// <summary>Temporary problem (network, 4xx "try later", 5xx) — retry with back-off.</summary>
    Transient,
    /// <summary>This recipient can't be reached (bad address, not on WhatsApp …) — mark Failed.</summary>
    Permanent,
    /// <summary>Account-level limit / auth problem — stop the whole channel until <see cref="SendOutcome.PauseFor"/> passes.</summary>
    ChannelBlocked,
}

public sealed record SendOutcome(SendOutcomeKind Kind, string? Error = null, TimeSpan? PauseFor = null)
{
    public static readonly SendOutcome Ok = new(SendOutcomeKind.Sent);
}

/// <summary>Config section "WhatsApp" (secrets via user-secrets / environment variables, never committed).</summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    /// <summary>Permanent System User access token from Meta Business Manager (WhatsApp__AccessToken).</summary>
    public string? AccessToken { get; set; }
    /// <summary>Phone number ID of the sending number (WhatsApp Manager → API setup).</summary>
    public string? PhoneNumberId { get; set; }
    /// <summary>Name of an APPROVED marketing template.</summary>
    public string? TemplateName { get; set; }
    public string TemplateLanguage { get; set; } = "en";
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>Body parameters, in order, for {{1}}, {{2}} … — any of: name, title, body, link, coupon.</summary>
    public string[] TemplateParams { get; set; } = { "name", "title" };
    /// <summary>The template has an IMAGE header: the campaign image is sent as the header (campaigns without an image are skipped for WhatsApp).</summary>
    public bool TemplateImageHeader { get; set; }
}

public sealed record WhatsAppTemplateData(string Name, string Title, string Body, string? Link, string? Coupon, string? ImageUrl);

public interface IWhatsAppSender
{
    /// <summary>True only when enabled and every required setting is present (not a placeholder).</summary>
    bool IsConfigured { get; }
    /// <summary>Human-readable reason when not configured (shown in the admin).</summary>
    string? NotConfiguredReason { get; }
    bool RequiresImage { get; }
    Task<SendOutcome> SendTemplateAsync(string phoneDigits, WhatsAppTemplateData data, CancellationToken ct = default);

    /// <summary>"9876543210" / "+91 98765-43210" / "919876543210" → "919876543210"; null when not a valid Indian mobile.</summary>
    static string? NormalizeIndianMobile(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var d = new string(phone.Where(char.IsDigit).ToArray());
        if (d.Length == 11 && d[0] == '0') d = d[1..];
        if (d.Length == 10 && d[0] is >= '6' and <= '9') return "91" + d;
        if (d.Length == 12 && d.StartsWith("91") && d[2] is >= '6' and <= '9') return d;
        return null;
    }
}

/// <summary>
/// Meta WhatsApp Cloud API (graph.facebook.com/{version}/{phone-number-id}/messages) — template messages only,
/// because business-initiated marketing messages must use an approved template. Disabled until configured.
/// </summary>
public sealed class WhatsAppCloudSender : IWhatsAppSender
{
    private readonly HttpClient _http;
    private readonly WhatsAppOptions _opt;
    private readonly ILogger<WhatsAppCloudSender> _log;

    public WhatsAppCloudSender(HttpClient http, IConfiguration config, ILogger<WhatsAppCloudSender> log)
    {
        _http = http;
        _log = log;
        _opt = config.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
    }

    private static bool Missing(string? v) =>
        string.IsNullOrWhiteSpace(v) || v.StartsWith("SET-", StringComparison.OrdinalIgnoreCase) || v.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);

    public string? NotConfiguredReason =>
        !_opt.Enabled ? "WhatsApp is turned off (WhatsApp:Enabled = false)."
        : Missing(_opt.AccessToken) ? "WhatsApp access token is not set (WhatsApp:AccessToken)."
        : Missing(_opt.PhoneNumberId) ? "WhatsApp phone number ID is not set (WhatsApp:PhoneNumberId)."
        : Missing(_opt.TemplateName) ? "No approved template name is set (WhatsApp:TemplateName)."
        : null;

    public bool IsConfigured => NotConfiguredReason == null;
    public bool RequiresImage => _opt.TemplateImageHeader;

    public async Task<SendOutcome> SendTemplateAsync(string phoneDigits, WhatsAppTemplateData data, CancellationToken ct = default)
    {
        if (!IsConfigured) return new SendOutcome(SendOutcomeKind.ChannelBlocked, NotConfiguredReason, TimeSpan.FromHours(1));

        var bodyParams = _opt.TemplateParams.Select(p => new
        {
            type = "text",
            text = Clip(p.ToLowerInvariant() switch
            {
                "name" => string.IsNullOrWhiteSpace(data.Name) ? "there" : data.Name.Split(' ')[0],
                "title" => data.Title,
                "body" => string.Join(' ', data.Body.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)),
                "link" => data.Link ?? "",
                "coupon" => data.Coupon ?? "",
                _ => "",
            }, 900),
        }).ToList();

        var components = new List<object>();
        if (_opt.TemplateImageHeader && !string.IsNullOrWhiteSpace(data.ImageUrl))
            components.Add(new { type = "header", parameters = new object[] { new { type = "image", image = new { link = data.ImageUrl } } } });
        if (bodyParams.Count > 0)
            components.Add(new { type = "body", parameters = bodyParams });

        var payload = new
        {
            messaging_product = "whatsapp",
            to = phoneDigits,
            type = "template",
            template = new { name = _opt.TemplateName, language = new { code = _opt.TemplateLanguage }, components },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{_opt.ApiVersion}/{_opt.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.AccessToken);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return SendOutcome.Ok;
            var text = await res.Content.ReadAsStringAsync(ct);
            return Classify(res.StatusCode, text);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SendOutcome(SendOutcomeKind.Transient, "WhatsApp API timed out");
        }
        catch (HttpRequestException ex)
        {
            return new SendOutcome(SendOutcomeKind.Transient, "Network error: " + ex.Message);
        }
    }

    /// <summary>Meta error codes: https://developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes</summary>
    private SendOutcome Classify(HttpStatusCode status, string body)
    {
        int code = 0;
        string msg = $"HTTP {(int)status}";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                if (e.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci)) code = ci;
                if (e.TryGetProperty("message", out var m)) msg = $"{code}: {m.GetString()}";
            }
        }
        catch (JsonException) { }
        _log.LogWarning("WhatsApp send rejected: {Error}", msg);

        return code switch
        {
            // auth / permission / account problems — nothing will go out until someone fixes the config
            190 or 10 or 200 or 131031 or 131005 or 368 => new SendOutcome(SendOutcomeKind.ChannelBlocked, msg, TimeSpan.FromHours(6)),
            // throughput / spam rate limits on the business account
            4 or 80007 or 130429 or 131048 => new SendOutcome(SendOutcomeKind.ChannelBlocked, msg, TimeSpan.FromHours(1)),
            // per-recipient pair rate limit, temporary service errors
            131056 or 131000 or 131016 or 133004 => new SendOutcome(SendOutcomeKind.Transient, msg),
            _ when (int)status == 429 => new SendOutcome(SendOutcomeKind.ChannelBlocked, msg, TimeSpan.FromMinutes(30)),
            _ when (int)status >= 500 => new SendOutcome(SendOutcomeKind.Transient, msg),
            // template / recipient problems (131026 not on WhatsApp, 131030 not allowed, 132xxx template)
            _ => new SendOutcome(SendOutcomeKind.Permanent, msg),
        };
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
