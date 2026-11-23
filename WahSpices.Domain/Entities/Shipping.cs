using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

/// <summary>
/// One connected shipping aggregator account (Shiprocket today; NimbusPost / iThink / Shipmozo … later).
/// Everything here is edited from Admin → Shipping → Partners, so nothing needs a code change when a password,
/// pickup address or default box size changes. Secret columns are encrypted at rest (see EncryptedFields).
/// </summary>
public class ShippingProviderAccount : BaseEntity
{
    /// <summary>Which adapter talks to this account: "shiprocket" (more adapters can be added later).</summary>
    public string ProviderCode { get; set; } = "shiprocket";
    public string DisplayName { get; set; } = "Shiprocket";
    public bool IsEnabled { get; set; } = true;
    /// <summary>Pre-selected partner when comparing rates / auto-shipping.</summary>
    public bool IsDefault { get; set; }

    public string ApiBaseUrl { get; set; } = "https://apiv2.shiprocket.in/v1/external";
    /// <summary>API user email (Shiprocket → Settings → API → API Users).</summary>
    public string? ApiUsername { get; set; }
    /// <summary>API user password / key — encrypted.</summary>
    public string? ApiPassword { get; set; }
    /// <summary>Cached bearer token — encrypted; refreshed automatically before <see cref="TokenExpiresAt"/>.</summary>
    public string? AuthToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    /// <summary>How long a token is valid (Shiprocket: 240 h). We refresh 12 h early.</summary>
    public int TokenLifetimeHours { get; set; } = 240;

    /// <summary>Pickup location NAME exactly as saved in the provider panel (e.g. "Primary").</summary>
    public string? PickupLocation { get; set; }
    /// <summary>Pickup pincode used for rate checks.</summary>
    public string? PickupPincode { get; set; }
    /// <summary>Optional custom channel id (Shiprocket "channel_id"); blank → provider default (CUSTOM).</summary>
    public string? ChannelId { get; set; }

    /// <summary>Courier tracking page; {awb} is replaced. e.g. https://shiprocket.co/tracking/{awb}</summary>
    public string? TrackingUrlTemplate { get; set; } = "https://shiprocket.co/tracking/{awb}";

    /// <summary>Shared secret the provider sends in the webhook header (x-api-key) — encrypted.</summary>
    public string? WebhookToken { get; set; }
    public DateTime? LastWebhookAt { get; set; }

    // Default parcel (used when an order has no better data; editable per shipment)
    public decimal DefaultLengthCm { get; set; } = 20;
    public decimal DefaultBreadthCm { get; set; } = 15;
    public decimal DefaultHeightCm { get; set; } = 10;
    /// <summary>Box + filler weight added on top of the product weight.</summary>
    public decimal PackagingWeightKg { get; set; } = 0.1m;
    public decimal MinWeightKg { get; set; } = 0.5m;
    /// <summary>HSN printed on the provider invoice for every line (spices: 0910).</summary>
    public string? DefaultHsn { get; set; } = "0910";
    /// <summary>GST % sent with each line (selling price is inclusive).</summary>
    public decimal DefaultTaxPercent { get; set; } = 5;

    // Behaviour
    /// <summary>After an AWB is assigned, request a pickup automatically.</summary>
    public bool AutoSchedulePickup { get; set; } = true;
    /// <summary>After an AWB is assigned, generate the shipping label automatically.</summary>
    public bool AutoGenerateLabel { get; set; } = true;
    /// <summary>Background poll of active shipments (minutes). 0 = webhooks only.</summary>
    public int TrackingPollMinutes { get; set; } = 120;
    /// <summary>Move the store order through Dispatched → In transit → Delivered from courier updates (and email the customer).</summary>
    public bool SyncOrderStatus { get; set; } = true;

    // Health
    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestMessage { get; set; }
}

/// <summary>A parcel created on a shipping provider for a store order.</summary>
public class Shipment : BaseEntity
{
    public int OrderId { get; set; }
    public int ProviderAccountId { get; set; }
    public string ProviderCode { get; set; } = "shiprocket";

    public string? ProviderOrderId { get; set; }
    public string? ProviderShipmentId { get; set; }
    public string? AwbCode { get; set; }
    public string? CourierCompanyId { get; set; }
    public string? CourierName { get; set; }

    /// <summary>Freight quoted when the courier was chosen (₹, incl. COD charge where applicable).</summary>
    public decimal? FreightCharge { get; set; }
    public string? EstimatedDeliveryDays { get; set; }
    public DateTime? Etd { get; set; }

    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal BreadthCm { get; set; }
    public decimal HeightCm { get; set; }
    public bool IsCod { get; set; }
    public decimal CodAmount { get; set; }

    /// <summary>Latest provider status text, e.g. "IN TRANSIT".</summary>
    public string? Status { get; set; }
    public string? StatusCode { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsDelivered { get; set; }
    public bool IsRto { get; set; }

    public string? LabelUrl { get; set; }
    public string? ManifestUrl { get; set; }
    public string? InvoiceUrl { get; set; }
    public DateTime? PickupScheduledAt { get; set; }
    public string? PickupTokenNumber { get; set; }
    public DateTime? LastTrackedAt { get; set; }
    public string? LastError { get; set; }

    public Order Order { get; set; } = null!;
    public ShippingProviderAccount ProviderAccount { get; set; } = null!;
    public ICollection<ShipmentEvent> Events { get; set; } = new List<ShipmentEvent>();
}

/// <summary>One courier scan (from webhook or polling) — the tracking timeline.</summary>
public class ShipmentEvent : BaseEntity
{
    public int ShipmentId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Activity { get; set; }
    public string? Location { get; set; }
    /// <summary>"webhook" | "poll" | "admin"</summary>
    public string Source { get; set; } = "poll";

    public Shipment Shipment { get; set; } = null!;
}
