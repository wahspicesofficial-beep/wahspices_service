using System.Text.Json;
using WahSpices.Domain.Entities;

namespace WahSpices.API.Services.Shipping;

/// <summary>
/// Adapter for one shipping aggregator. Every provider (Shiprocket, NimbusPost, iThink, Shipmozo …) implements this
/// so the admin can compare rates side by side and ship with any of them. Account settings/credentials are passed
/// in on every call — nothing provider-specific is read from appsettings, so it is all editable from the admin.
/// Implementations may refresh <see cref="ShippingProviderAccount.AuthToken"/>; the caller saves the account.
/// </summary>
public interface IShippingProvider
{
    string Code { get; }
    string Name { get; }

    Task<ProviderTestResult> TestAsync(ShippingProviderAccount account, CancellationToken ct = default);
    Task<IReadOnlyList<PickupLocationInfo>> GetPickupLocationsAsync(ShippingProviderAccount account, CancellationToken ct = default);
    /// <summary>Create a new pickup (warehouse) address in the provider account; returns its name.</summary>
    Task<string> AddPickupLocationAsync(ShippingProviderAccount account, NewPickupLocation location, CancellationToken ct = default);
    Task<IReadOnlyList<CourierQuote>> GetRatesAsync(ShippingProviderAccount account, RateRequest request, CancellationToken ct = default);
    Task<ProviderOrderResult> CreateOrderAsync(ShippingProviderAccount account, ShipmentRequest request, CancellationToken ct = default);
    Task<AwbResult> AssignAwbAsync(ShippingProviderAccount account, string providerShipmentId, string? courierCompanyId, CancellationToken ct = default);
    Task<PickupResult> SchedulePickupAsync(ShippingProviderAccount account, string providerShipmentId, CancellationToken ct = default);
    Task<string> GenerateLabelAsync(ShippingProviderAccount account, string providerShipmentId, CancellationToken ct = default);
    Task<string> GenerateManifestAsync(ShippingProviderAccount account, string providerShipmentId, string? providerOrderId, CancellationToken ct = default);
    Task<string> PrintInvoiceAsync(ShippingProviderAccount account, string providerOrderId, CancellationToken ct = default);
    Task CancelAsync(ShippingProviderAccount account, string providerOrderId, CancellationToken ct = default);
    Task<TrackingUpdate?> TrackAsync(ShippingProviderAccount account, string awb, CancellationToken ct = default);
    /// <summary>Parse a webhook body pushed by the provider; null when it isn't a tracking update.</summary>
    TrackingUpdate? ParseWebhook(JsonElement body);
}

/// <summary>A provider call failed — the message is safe to show the admin (never contains credentials).</summary>
public sealed class ShippingProviderException(string message, int? httpStatus = null) : Exception(message)
{
    public int? HttpStatus { get; } = httpStatus;
}

public sealed record ProviderTestResult(bool Ok, string Message, int PickupLocations = 0);

public sealed record NewPickupLocation(string Name, string ContactName, string Email, string Phone, string Address,
    string? Address2, string City, string State, string Pincode, string Country = "India");

public sealed record PickupLocationInfo(string Name, string? Pincode, string? City, string? State, string? Address, bool IsPrimary, bool IsActive);

public sealed record RateRequest(string PickupPincode, string DeliveryPincode, decimal WeightKg, decimal LengthCm, decimal BreadthCm,
    decimal HeightCm, bool IsCod, decimal DeclaredValue);

public sealed record CourierQuote
{
    public string CourierCompanyId { get; init; } = "";
    public string CourierName { get; init; } = "";
    /// <summary>Total the account will be charged (freight + COD + other charges).</summary>
    public decimal Rate { get; init; }
    public decimal Freight { get; init; }
    public decimal CodCharge { get; init; }
    public decimal ChargeableWeightKg { get; init; }
    public string? EstimatedDays { get; init; }
    public int? EstimatedDaysMax { get; init; }
    public string? Etd { get; init; }
    public decimal? Rating { get; init; }
    public bool IsSurface { get; init; }
    public bool CodAvailable { get; init; } = true;
    public bool Recommended { get; init; }
    public string? PickupAvailability { get; init; }
    public string? Tracking { get; init; }
}

public sealed record ShipmentLine(string Name, string Sku, int Units, decimal SellingPrice, decimal TaxPercent, string? Hsn);

public sealed record ShipmentRequest
{
    public string OrderRef { get; init; } = "";
    public DateTime OrderDate { get; init; }
    public string CustomerName { get; init; } = "";
    public string Address { get; init; } = "";
    public string? Address2 { get; init; }
    public string City { get; init; } = "";
    public string State { get; init; } = "";
    public string Pincode { get; init; } = "";
    public string Country { get; init; } = "India";
    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public IReadOnlyList<ShipmentLine> Lines { get; init; } = [];
    public bool IsCod { get; init; }
    public decimal SubTotal { get; init; }
    public decimal ShippingCharges { get; init; }
    public decimal TotalDiscount { get; init; }
    public decimal WeightKg { get; init; }
    public decimal LengthCm { get; init; }
    public decimal BreadthCm { get; init; }
    public decimal HeightCm { get; init; }
    public string? Comment { get; init; }
}

public sealed record ProviderOrderResult(string ProviderOrderId, string ProviderShipmentId, string? Status);
public sealed record AwbResult(string AwbCode, string? CourierCompanyId, string? CourierName);
public sealed record PickupResult(DateTime? ScheduledAt, string? TokenNumber, string? Message);

public sealed record TrackingScan(DateTime OccurredAt, string Status, string? Activity, string? Location);

public sealed record TrackingUpdate
{
    public string Awb { get; init; } = "";
    public string? CurrentStatus { get; init; }
    public string? StatusCode { get; init; }
    public string? CourierName { get; init; }
    public DateTime? Etd { get; init; }
    public bool IsReturn { get; init; }
    public IReadOnlyList<TrackingScan> Scans { get; init; } = [];
}

/// <summary>Finds the adapter for an account's ProviderCode.</summary>
public sealed class ShippingProviderRegistry(IEnumerable<IShippingProvider> providers)
{
    private readonly Dictionary<string, IShippingProvider> _byCode =
        providers.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IShippingProvider> All => _byCode.Values;

    public IShippingProvider Get(string code) =>
        _byCode.TryGetValue(code, out var p) ? p
            : throw new ShippingProviderException($"No adapter is installed for shipping partner '{code}'.");

    public bool Has(string code) => _byCode.ContainsKey(code);
}
