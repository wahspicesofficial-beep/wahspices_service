using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

// ═══════════════════════════════════════════════════════════════════════════════
//  Customer communications: outbound campaigns (Email / WhatsApp, throttled) and
//  in-app broadcasts (banner + notification bell, real time over SignalR).
//  Status / channel / audience values are short strings (see the *Values classes)
//  so they read naturally in SQL and in the admin API.
// ═══════════════════════════════════════════════════════════════════════════════

public static class MessagingChannels
{
    public const string Email = "Email";
    public const string WhatsApp = "WhatsApp";
    public static readonly string[] All = { Email, WhatsApp };
}

public static class AudienceTypes
{
    /// <summary>Every eligible customer at the moment sending/publishing starts.</summary>
    public const string All = "All";
    /// <summary>Only the user ids picked by the admin.</summary>
    public const string Selected = "Selected";
}

public static class CampaignStatuses
{
    public const string Draft = "Draft";
    public const string Scheduled = "Scheduled";
    public const string Sending = "Sending";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class RecipientStatuses
{
    public const string Pending = "Pending";
    /// <summary>Claimed by the dispatcher, SMTP/API call in flight.</summary>
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    /// <summary>Not sent on purpose: opted out, no deliverable address, account disabled.</summary>
    public const string Skipped = "Skipped";
    public const string Cancelled = "Cancelled";
}

public static class BroadcastStatuses
{
    public const string Draft = "Draft";
    public const string Scheduled = "Scheduled";
    public const string Published = "Published";
    /// <summary>Ended early (or by the admin) — no more banners; stays in users' notification lists until read.</summary>
    public const string Ended = "Ended";
}

/// <summary>An outbound marketing message (offer, launch, sale …) sent by email and/or WhatsApp.</summary>
public class MarketingCampaign : BaseEntity
{
    /// <summary>Master-data value from the "CampaignType" list (Offer, NewProduct, Sale, Announcement …).</summary>
    public string Type { get; set; } = "Offer";
    public string Title { get; set; } = string.Empty;          // email subject + heading
    public string Body { get; set; } = string.Empty;           // plain text, line breaks kept (never HTML)
    public string? ImageUrl { get; set; }                      // "/uploads/marketing/…"
    public string? ButtonLabel { get; set; }
    public string? ButtonUrl { get; set; }                     // site path "/products" or https URL
    public string? ProductIds { get; set; }                    // comma-separated product ids shown as cards
    public string? CouponCode { get; set; }

    public bool SendEmail { get; set; } = true;
    public bool SendWhatsApp { get; set; }

    public string AudienceType { get; set; } = AudienceTypes.All;
    /// <summary>JSON array of user ids when AudienceType = Selected.</summary>
    public string? SelectedUserIds { get; set; }

    public string Status { get; set; } = CampaignStatuses.Draft;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

/// <summary>One user × one channel of a campaign. The dispatcher drains Pending rows under the channel caps.</summary>
public class CampaignRecipient
{
    public long Id { get; set; }
    public int CampaignId { get; set; }
    public int UserId { get; set; }
    public string Channel { get; set; } = MessagingChannels.Email;
    /// <summary>Email address or phone (E.164 digits) captured when the campaign started.</summary>
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = RecipientStatuses.Pending;
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    /// <summary>Time of the most recent send attempt — the throttle counts these (rolling minute / hour / 24 h).</summary>
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public MarketingCampaign? Campaign { get; set; }
}

/// <summary>Per-channel sending limits (editable in Admin → Marketing). One row per channel.</summary>
public class MessagingChannelSetting
{
    public int Id { get; set; }
    public string Channel { get; set; } = MessagingChannels.Email;
    public int PerMinute { get; set; }
    public int PerHour { get; set; }
    public int PerDay { get; set; }
    /// <summary>Hard daily limit of the provider account (Gmail ≈ 500 / rolling 24 h). 0 = not used.</summary>
    public int ProviderDailyLimit { get; set; }
    /// <summary>Sends per 24 h kept free for transactional mail (OTP, orders) — campaigns never use them.</summary>
    public int TransactionalReserve { get; set; }
    /// <summary>Set when the provider reports a quota error (or by the admin); campaigns resume after it.</summary>
    public DateTime? PausedUntil { get; set; }
    public string? PauseReason { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>An in-app message: optional banner popup + entry in the customer's notification bell.</summary>
public class Broadcast : BaseEntity
{
    public string Type { get; set; } = "Offer";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;        // plain text
    public string? ImageUrl { get; set; }
    public string? ButtonLabel { get; set; }
    public string? ButtonUrl { get; set; }
    public string AudienceType { get; set; } = AudienceTypes.All;
    public string? SelectedUserIds { get; set; }
    public bool ShowAsBanner { get; set; } = true;
    /// <summary>Publish time for scheduled broadcasts; banner/late-joiner window start.</summary>
    public DateTime? ActiveFrom { get; set; }
    /// <summary>After this the banner is no longer shown (the bell entry stays until read).</summary>
    public DateTime? ActiveUntil { get; set; }
    public string Status { get; set; } = BroadcastStatuses.Draft;
    public DateTime? PublishedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

/// <summary>Per-user state of a broadcast.</summary>
public class BroadcastRecipient
{
    public long Id { get; set; }
    public int BroadcastId { get; set; }
    public int UserId { get; set; }
    public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
    /// <summary>User closed the banner — never shown as a banner again.</summary>
    public DateTime? BannerSeenAt { get; set; }
    /// <summary>User read it in the bell — removed from their notification list.</summary>
    public DateTime? ReadAt { get; set; }

    public Broadcast? Broadcast { get; set; }
}
