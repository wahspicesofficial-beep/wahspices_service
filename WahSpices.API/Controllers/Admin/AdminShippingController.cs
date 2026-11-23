using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services.Shipping;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Shipping partners (aggregator accounts) and shipment operations: compare rates across partners, ship, pickup,
/// label, manifest, invoice, cancel, tracking. Partner settings need the Settings permission; shipping needs Orders.
/// </summary>
[Route("api/admin/shipping")]
[RequireModule("Orders")]
public class AdminShippingController(AppDbContext db, ShippingService shipping, ShippingProviderRegistry registry, IConfiguration config)
    : AdminBaseController
{
    // ══════════════════════ Partners ══════════════════════

    [HttpGet("providers")]
    [RequireModule("Settings", "Orders")]
    public IActionResult Providers()
        => Ok(ApiResponse<object>.Ok(registry.All.Select(p => new { code = p.Code, name = p.Name })));

    [HttpGet("partners")]
    [RequireModule("Settings", "Orders")]
    public async Task<IActionResult> Partners()
    {
        var list = await db.ShippingProviderAccounts.Where(a => !a.IsDeleted).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { partners = list.Select(MapPartner), webhookUrl = WebhookUrl() }));
    }

    [HttpPost("partners")]
    [RequireModule("Settings")]
    public async Task<IActionResult> CreatePartner([FromBody] ShippingPartnerRequest req)
    {
        var code = (req.ProviderCode ?? "").Trim().ToLowerInvariant();
        if (!registry.Has(code)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Choose a supported shipping partner.", "providerCode"));
        var a = new ShippingProviderAccount { ProviderCode = code, DisplayName = registry.Get(code).Name, CreatedBy = AdminName,
            WebhookToken = NewToken() };
        var err = Apply(a, req);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("VALIDATION", err));
        if (!await db.ShippingProviderAccounts.AnyAsync(x => !x.IsDeleted)) a.IsDefault = true;
        if (a.IsDefault) await ClearDefaultAsync(0);
        db.ShippingProviderAccounts.Add(a);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapPartner(a), "Shipping partner added"));
    }

    [HttpPut("partners/{id:int}")]
    [RequireModule("Settings")]
    public async Task<IActionResult> UpdatePartner(int id, [FromBody] ShippingPartnerRequest req)
    {
        var a = await db.ShippingProviderAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipping partner not found."));
        var err = Apply(a, req);
        if (err != null) return BadRequest(ApiResponse<object>.Fail("VALIDATION", err));
        if (req.RegenerateWebhookToken == true || string.IsNullOrWhiteSpace(a.WebhookToken)) a.WebhookToken = NewToken();
        if (a.IsDefault) await ClearDefaultAsync(a.Id);
        a.UpdatedAt = DateTime.UtcNow; a.UpdatedBy = AdminName;
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(MapPartner(a), "Saved"));
    }

    [HttpDelete("partners/{id:int}")]
    [RequireModule("Settings")]
    public async Task<IActionResult> DeletePartner(int id)
    {
        var a = await db.ShippingProviderAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipping partner not found."));
        a.IsDeleted = true; a.IsEnabled = false; a.ApiPassword = null; a.AuthToken = null; a.WebhookToken = null;
        a.UpdatedAt = DateTime.UtcNow; a.UpdatedBy = AdminName;
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id }, "Shipping partner removed"));
    }

    [HttpPost("partners/{id:int}/test")]
    [RequireModule("Settings")]
    public async Task<IActionResult> TestPartner(int id, CancellationToken ct)
    {
        var a = await db.ShippingProviderAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipping partner not found."));
        var r = await registry.Get(a.ProviderCode).TestAsync(a, ct);
        a.LastTestedAt = DateTime.UtcNow; a.LastTestOk = r.Ok; a.LastTestMessage = r.Message.Length > 500 ? r.Message[..500] : r.Message;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { ok = r.Ok, message = r.Message, partner = MapPartner(a) }));
    }

    [HttpGet("partners/{id:int}/pickup-locations")]
    [RequireModule("Settings", "Orders")]
    public async Task<IActionResult> PickupLocations(int id, CancellationToken ct)
    {
        var a = await db.ShippingProviderAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipping partner not found."));
        try
        {
            var list = await registry.Get(a.ProviderCode).GetPickupLocationsAsync(a, ct);
            await db.SaveChangesAsync(ct);
            return Ok(ApiResponse<object>.Ok(list));
        }
        catch (ShippingProviderException ex) { await db.SaveChangesAsync(ct); return Fail(ex); }
    }

    /// <summary>Create a pickup address in the partner account (e.g. a new warehouse) and optionally make it this partner's default.</summary>
    [HttpPost("partners/{id:int}/pickup-locations")]
    [RequireModule("Settings")]
    public async Task<IActionResult> AddPickupLocation(int id, [FromBody] PickupLocationRequest r, CancellationToken ct)
    {
        var a = await db.ShippingProviderAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipping partner not found."));
        var phone = new string((r.Phone ?? "").Where(char.IsDigit).ToArray());
        if (phone.Length == 12 && phone.StartsWith("91")) phone = phone[2..];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 36) errors.Add("Nickname is required (max 36 characters).");
        if (string.IsNullOrWhiteSpace(r.ContactName) || !System.Text.RegularExpressions.Regex.IsMatch(r.ContactName.Trim(), @"^[A-Za-z][A-Za-z .']{1,59}$"))
            errors.Add("Contact name must be a person's name in letters only (e.g. Yash Patel) — not a number.");
        if (string.IsNullOrWhiteSpace(r.Email) || !r.Email.Contains('@')) errors.Add("A valid email is required.");
        if (phone.Length != 10) errors.Add("Phone must be a 10-digit mobile number.");
        if (string.IsNullOrWhiteSpace(r.Address) || r.Address.Trim().Length < 10) errors.Add("Address line 1 must be at least 10 characters and include a house/shop/building number.");
        if (string.IsNullOrWhiteSpace(r.City) || string.IsNullOrWhiteSpace(r.State)) errors.Add("City and state are required.");
        if (r.Pincode == null || r.Pincode.Trim().Length != 6 || !r.Pincode.Trim().All(char.IsDigit)) errors.Add("Pincode must be 6 digits.");
        if (errors.Count > 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", string.Join(" ", errors)));
        try
        {
            var name = await registry.Get(a.ProviderCode).AddPickupLocationAsync(a, new NewPickupLocation(
                r.Name!, r.ContactName!, r.Email!, phone, r.Address!, r.Address2, r.City!, r.State!, r.Pincode!.Trim()), ct);
            if (r.MakeDefault)
            {
                a.PickupLocation = name;
                a.PickupPincode = r.Pincode!.Trim();
            }
            await db.SaveChangesAsync(ct);
            return Ok(ApiResponse<object>.Ok(new { name, partner = MapPartner(a) }));
        }
        catch (ShippingProviderException ex) { await db.SaveChangesAsync(ct); return Fail(ex); }
    }

    /// <summary>Reveal the webhook token so it can be pasted into the provider panel (Settings permission only).</summary>
    [HttpGet("partners/{id:int}/webhook-token")]
    [RequireModule("Settings")]
    public async Task<IActionResult> WebhookToken(int id)
    {
        var a = await db.ShippingProviderAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (a == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipping partner not found."));
        if (string.IsNullOrWhiteSpace(a.WebhookToken)) { a.WebhookToken = NewToken(); await db.SaveChangesAsync(); }
        return Ok(ApiResponse<object>.Ok(new { token = a.WebhookToken, url = WebhookUrl(), header = "x-api-key" }));
    }

    // ══════════════════════ Orders / shipments ══════════════════════

    /// <summary>Shipping panel for one order: shipments with timeline + estimated parcel.</summary>
    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> OrderShipping(int orderId, CancellationToken ct)
    {
        try
        {
            var order = await shipping.LoadOrderAsync(orderId, ct);
            var partners = await db.ShippingProviderAccounts.Where(a => !a.IsDeleted && a.IsEnabled).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToListAsync(ct);
            ParcelSpec? parcel = partners.Count > 0 ? await shipping.EstimateParcelAsync(order, partners[0], ct) : null;
            var shipments = await db.Shipments.Include(s => s.Events).Include(s => s.ProviderAccount)
                .Where(s => s.OrderId == orderId && !s.IsDeleted).OrderByDescending(s => s.Id).ToListAsync(ct);
            return Ok(ApiResponse<object>.Ok(new
            {
                orderId,
                order.OrderNumber,
                status = order.Status.ToString(),
                paymentMethod = order.PaymentMethod.ToString(),
                paymentStatus = order.PaymentStatus.ToString(),
                order.GrandTotal,
                deliveryPincode = order.Address?.Pincode,
                deliveryCity = order.Address?.City,
                parcel,
                partners = partners.Select(p => new { p.Id, p.DisplayName, p.ProviderCode, p.IsDefault, p.AutoSchedulePickup,
                    ready = !string.IsNullOrWhiteSpace(p.ApiUsername) && !string.IsNullOrWhiteSpace(p.ApiPassword) && !string.IsNullOrWhiteSpace(p.PickupLocation) }),
                shipments = shipments.Select(MapShipment),
            }));
        }
        catch (ShippingProviderException ex) { return Fail(ex); }
    }

    [HttpPost("orders/{orderId:int}/rates")]
    public async Task<IActionResult> Rates(int orderId, [FromBody] ShippingRatesRequest? req, CancellationToken ct)
    {
        try
        {
            ParcelSpec? over = req?.WeightKg is > 0
                ? new ParcelSpec(req.WeightKg.Value, req.LengthCm is > 0 ? req.LengthCm.Value : 20, req.BreadthCm is > 0 ? req.BreadthCm.Value : 15, req.HeightCm is > 0 ? req.HeightCm.Value : 10)
                : null;
            var (order, parcel, partners) = await shipping.CompareRatesAsync(orderId, over, ct);
            var all = partners.SelectMany(p => p.Quotes.Select(q => new { p, q })).ToList();
            var cheapest = all.OrderBy(x => x.q.Rate).FirstOrDefault();
            var fastest = all.Where(x => x.q.EstimatedDaysMax != null).OrderBy(x => x.q.EstimatedDaysMax).ThenBy(x => x.q.Rate).FirstOrDefault();
            // "Best value": cheap, quick and well rated (lower score is better)
            var best = all.OrderBy(x => (double)x.q.Rate * (1 + 0.08 * (x.q.EstimatedDaysMax ?? 6)) * (1.4 - 0.08 * (double)(x.q.Rating ?? 3))).FirstOrDefault();
            return Ok(ApiResponse<object>.Ok(new
            {
                orderId,
                order.OrderNumber,
                parcel,
                isCod = order.PaymentMethod == Domain.Enums.PaymentMethod.CashOnDelivery && order.PaymentStatus != Domain.Enums.PaymentStatus.Paid,
                deliveryPincode = order.Address.Pincode,
                partners = partners.Select(p => new { p.AccountId, p.ProviderCode, p.PartnerName, p.Ok, p.Error, count = p.Quotes.Count }),
                options = BuildOptions(partners),
            }));
        }
        catch (ShippingProviderException ex) { return Fail(ex); }
    }

    /// <summary>Rate calculator — any pickup/delivery pincode and parcel, compares every connected partner.</summary>
    [HttpPost("calculator")]
    public async Task<IActionResult> Calculator([FromBody] RateCalculatorRequest r, CancellationToken ct)
    {
        static bool Pin(string? p) => p != null && p.Trim().Length == 6 && p.Trim().All(char.IsDigit);
        if (!Pin(r.DeliveryPincode)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Delivery pincode must be 6 digits."));
        if (!string.IsNullOrWhiteSpace(r.PickupPincode) && !Pin(r.PickupPincode)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Pickup pincode must be 6 digits."));
        if (r.WeightKg is not > 0 || r.WeightKg > 500) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Enter the parcel weight in kg (0.01 – 500)."));
        decimal Dim(decimal? v, decimal d) => v is > 0 and <= 500 ? v.Value : d;
        var parcel = new ParcelSpec(r.WeightKg!.Value, Dim(r.LengthCm, 20), Dim(r.BreadthCm, 15), Dim(r.HeightCm, 10));
        var volumetric = Math.Round(parcel.LengthCm * parcel.BreadthCm * parcel.HeightCm / 5000m, 2);
        try
        {
            var partners = await shipping.CalculateRatesAsync(r.PickupPincode, r.DeliveryPincode!, parcel, r.IsCod, r.DeclaredValue is > 0 ? r.DeclaredValue.Value : 500, ct);
            return Ok(ApiResponse<object>.Ok(new
            {
                parcel,
                volumetricWeightKg = volumetric,
                chargeableWeightKg = Math.Max(parcel.WeightKg, volumetric),
                r.IsCod,
                partners = partners.Select(p => new { p.AccountId, p.ProviderCode, p.PartnerName, p.Ok, p.Error, count = p.Quotes.Count }),
                options = BuildOptions(partners),
            }));
        }
        catch (ShippingProviderException ex) { return Fail(ex); }
    }

    /// <summary>Flatten all partners' quotes, cheapest first, with Cheapest / Fastest / Best value flags.</summary>
    private static IEnumerable<object> BuildOptions(IReadOnlyList<PartnerQuotes> partners)
    {
        var all = partners.SelectMany(p => p.Quotes.Select(q => new { p, q })).ToList();
        var cheapest = all.OrderBy(x => x.q.Rate).FirstOrDefault();
        var fastest = all.Where(x => x.q.EstimatedDaysMax != null).OrderBy(x => x.q.EstimatedDaysMax).ThenBy(x => x.q.Rate).FirstOrDefault();
        // "Best value": cheap, quick and well rated (lower score is better)
        var best = all.OrderBy(x => (double)x.q.Rate * (1 + 0.08 * (x.q.EstimatedDaysMax ?? 6)) * (1.4 - 0.08 * (double)(x.q.Rating ?? 3))).FirstOrDefault();
        return all.Select(x => new
        {
            accountId = x.p.AccountId,
            partner = x.p.PartnerName,
            providerCode = x.p.ProviderCode,
            x.q.CourierCompanyId, x.q.CourierName, x.q.Rate, x.q.Freight, x.q.CodCharge, x.q.ChargeableWeightKg,
            x.q.EstimatedDays, x.q.EstimatedDaysMax, x.q.Etd, x.q.Rating, x.q.IsSurface, x.q.CodAvailable,
            x.q.PickupAvailability, x.q.Tracking,
            recommended = x.q.Recommended,
            cheapest = cheapest != null && ReferenceEquals(x, cheapest),
            fastest = fastest != null && ReferenceEquals(x, fastest),
            bestValue = best != null && ReferenceEquals(x, best),
        }).OrderBy(o => o.Rate).ToList<object>();
    }

    [HttpPost("orders/{orderId:int}/ship")]
    public async Task<IActionResult> Ship(int orderId, [FromBody] ShipRequest req, CancellationToken ct)
    {
        try
        {
            var s = await shipping.ShipAsync(orderId, req, AdminName, ct);
            await db.Entry(s).Reference(x => x.ProviderAccount).LoadAsync(ct);
            return Ok(ApiResponse<object>.Ok(MapShipment(s), $"Shipment booked — AWB {s.AwbCode}"));
        }
        catch (ShippingProviderException ex) { return Fail(ex); }
    }

    [HttpPost("shipments/{id:int}/{action}")]
    public async Task<IActionResult> ShipmentAction(int id, string action, CancellationToken ct)
    {
        var s = await db.Shipments.Include(x => x.ProviderAccount).Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (s == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Shipment not found."));
        var a = s.ProviderAccount;
        var p = registry.Get(a.ProviderCode);
        try
        {
            string? url = null;
            switch (action.ToLowerInvariant())
            {
                case "pickup":
                    if (s.AwbCode == null) throw new ShippingProviderException("Assign a courier (AWB) first.");
                    if (!await shipping.TrySchedulePickupAsync(s, a, ct)) throw new ShippingProviderException(s.LastError ?? "Pickup failed.");
                    break;
                case "label":
                    if (s.AwbCode == null) throw new ShippingProviderException("Assign a courier (AWB) first.");
                    url = s.LabelUrl = await p.GenerateLabelAsync(a, s.ProviderShipmentId!, ct);
                    break;
                case "manifest":
                    url = s.ManifestUrl = await p.GenerateManifestAsync(a, s.ProviderShipmentId!, s.ProviderOrderId, ct);
                    break;
                case "invoice":
                    url = s.InvoiceUrl = await p.PrintInvoiceAsync(a, s.ProviderOrderId!, ct);
                    break;
                case "track":
                    if (!await shipping.RefreshTrackingAsync(s, ct)) throw new ShippingProviderException("No tracking updates yet from the courier.");
                    break;
                case "cancel":
                    if (s.IsCancelled) throw new ShippingProviderException("Shipment is already cancelled.");
                    if (s.IsDelivered) throw new ShippingProviderException("A delivered shipment can't be cancelled.");
                    await shipping.CancelAsync(s, AdminName, ct);
                    break;
                default:
                    return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Unknown action."));
            }
            s.LastError = null;
            await db.SaveChangesAsync(ct);
            return Ok(ApiResponse<object>.Ok(new { url, shipment = MapShipment(s) }));
        }
        catch (ShippingProviderException ex)
        {
            s.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(ct);
            return Fail(ex);
        }
    }

    /// <summary>All shipments (Shipping board).</summary>
    [HttpGet("shipments")]
    public async Task<IActionResult> Shipments([FromQuery] string? filter, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 30)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 200);
        var q = db.Shipments.Include(s => s.Order).Include(s => s.ProviderAccount).Where(s => !s.IsDeleted);
        q = (filter ?? "").ToLowerInvariant() switch
        {
            "active" => q.Where(s => !s.IsCancelled && !s.IsDelivered && !s.IsRto),
            "delivered" => q.Where(s => s.IsDelivered),
            "rto" => q.Where(s => s.IsRto),
            "cancelled" => q.Where(s => s.IsCancelled),
            "issues" => q.Where(s => s.LastError != null && !s.IsCancelled),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(s => (s.AwbCode != null && s.AwbCode.Contains(search)) || s.Order.OrderNumber.Contains(search));
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(s => s.Id).Skip((page - 1) * limit).Take(limit).ToListAsync();
        var counts = await db.Shipments.Where(s => !s.IsDeleted).GroupBy(_ => 1).Select(g => new
        {
            all = g.Count(),
            active = g.Count(s => !s.IsCancelled && !s.IsDelivered && !s.IsRto),
            delivered = g.Count(s => s.IsDelivered),
            rto = g.Count(s => s.IsRto),
            cancelled = g.Count(s => s.IsCancelled),
            freight = g.Where(s => !s.IsCancelled).Sum(s => s.FreightCharge ?? 0),
        }).FirstOrDefaultAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            shipments = rows.Select(s => new
            {
                s.Id, s.OrderId, orderNumber = s.Order.OrderNumber, partner = s.ProviderAccount.DisplayName,
                s.AwbCode, s.CourierName, s.Status, s.FreightCharge, s.IsCod, s.CodAmount, s.IsCancelled, s.IsDelivered, s.IsRto,
                s.Etd, s.PickupScheduledAt, s.LabelUrl, s.LastError, s.CreatedAt, s.LastTrackedAt,
                trackingUrl = ShippingService.TrackingUrl(s.ProviderAccount, s.AwbCode),
            }),
            counts,
            pagination = new { total, page, limit },
        }));
    }

    // ══════════════════════ helpers ══════════════════════

    private IActionResult Fail(ShippingProviderException ex)
        => BadRequest(ApiResponse<object>.Fail("SHIPPING_ERROR", ex.Message));

    private string WebhookUrl()
    {
        var api = config["App:PublicApiUrl"];
        var baseUrl = !string.IsNullOrWhiteSpace(api) ? api.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl}/api/hooks/parcel-updates";
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private async Task ClearDefaultAsync(int keepId)
    {
        var others = await db.ShippingProviderAccounts.Where(x => x.IsDefault && x.Id != keepId).ToListAsync();
        foreach (var o in others) o.IsDefault = false;
    }

    /// <summary>Copies editable fields; null = keep. Password is only replaced when a new non-blank value is sent.</summary>
    private static string? Apply(ShippingProviderAccount a, ShippingPartnerRequest r)
    {
        if (r.DisplayName != null) a.DisplayName = r.DisplayName.Trim().Length is > 0 and <= 100 ? r.DisplayName.Trim() : a.DisplayName;
        if (r.IsEnabled != null) a.IsEnabled = r.IsEnabled.Value;
        if (r.IsDefault != null) a.IsDefault = r.IsDefault.Value;
        if (r.ApiBaseUrl != null)
        {
            var u = r.ApiBaseUrl.Trim();
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return "API base URL must be an https:// address.";
            a.ApiBaseUrl = u.TrimEnd('/');
        }
        if (r.ApiUsername != null)
        {
            var e = r.ApiUsername.Trim();
            if (e != a.ApiUsername) { a.AuthToken = null; a.TokenExpiresAt = null; }
            a.ApiUsername = e.Length == 0 ? null : e;
        }
        if (!string.IsNullOrWhiteSpace(r.ApiPassword)) { a.ApiPassword = r.ApiPassword; a.AuthToken = null; a.TokenExpiresAt = null; }
        if (r.TokenLifetimeHours is > 0) a.TokenLifetimeHours = Math.Min(r.TokenLifetimeHours.Value, 720);
        if (r.PickupLocation != null) a.PickupLocation = Blank(r.PickupLocation);
        if (r.PickupPincode != null)
        {
            var pin = r.PickupPincode.Trim();
            if (pin.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(pin, @"^\d{6}$")) return "Pickup pincode must be 6 digits.";
            a.PickupPincode = Blank(pin);
        }
        if (r.ChannelId != null) a.ChannelId = Blank(r.ChannelId);
        if (r.TrackingUrlTemplate != null)
        {
            var t = r.TrackingUrlTemplate.Trim();
            if (t.Length > 0 && !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "Tracking URL must start with https://";
            a.TrackingUrlTemplate = Blank(t);
        }
        if (r.DefaultLengthCm is > 0) a.DefaultLengthCm = r.DefaultLengthCm.Value;
        if (r.DefaultBreadthCm is > 0) a.DefaultBreadthCm = r.DefaultBreadthCm.Value;
        if (r.DefaultHeightCm is > 0) a.DefaultHeightCm = r.DefaultHeightCm.Value;
        if (r.PackagingWeightKg is >= 0) a.PackagingWeightKg = r.PackagingWeightKg.Value;
        if (r.MinWeightKg is > 0) a.MinWeightKg = r.MinWeightKg.Value;
        if (r.DefaultHsn != null) a.DefaultHsn = Blank(r.DefaultHsn);
        if (r.DefaultTaxPercent is >= 0 and <= 28) a.DefaultTaxPercent = r.DefaultTaxPercent.Value;
        if (r.AutoSchedulePickup != null) a.AutoSchedulePickup = r.AutoSchedulePickup.Value;
        if (r.AutoGenerateLabel != null) a.AutoGenerateLabel = r.AutoGenerateLabel.Value;
        if (r.SyncOrderStatus != null) a.SyncOrderStatus = r.SyncOrderStatus.Value;
        if (r.TrackingPollMinutes is >= 0) a.TrackingPollMinutes = r.TrackingPollMinutes.Value == 0 ? 0 : Math.Clamp(r.TrackingPollMinutes.Value, 15, 1440);
        return null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static object MapPartner(ShippingProviderAccount a) => new
    {
        a.Id, a.ProviderCode, a.DisplayName, a.IsEnabled, a.IsDefault, a.ApiBaseUrl, a.ApiUsername,
        hasPassword = !string.IsNullOrWhiteSpace(a.ApiPassword),
        connected = !string.IsNullOrWhiteSpace(a.AuthToken) && a.TokenExpiresAt > DateTime.UtcNow,
        a.TokenExpiresAt, a.TokenLifetimeHours,
        a.PickupLocation, a.PickupPincode, a.ChannelId, a.TrackingUrlTemplate,
        hasWebhookToken = !string.IsNullOrWhiteSpace(a.WebhookToken), a.LastWebhookAt,
        a.DefaultLengthCm, a.DefaultBreadthCm, a.DefaultHeightCm, a.PackagingWeightKg, a.MinWeightKg, a.DefaultHsn, a.DefaultTaxPercent,
        a.AutoSchedulePickup, a.AutoGenerateLabel, a.TrackingPollMinutes, a.SyncOrderStatus,
        a.LastTestedAt, a.LastTestOk, a.LastTestMessage,
    };

    private static object MapShipment(Shipment s) => new
    {
        s.Id, s.OrderId, s.ProviderAccountId, partner = s.ProviderAccount?.DisplayName, s.ProviderCode,
        s.ProviderOrderId, s.ProviderShipmentId, s.AwbCode, s.CourierCompanyId, s.CourierName,
        s.FreightCharge, s.EstimatedDeliveryDays, s.Etd, s.WeightKg, s.LengthCm, s.BreadthCm, s.HeightCm, s.IsCod, s.CodAmount,
        s.Status, s.IsCancelled, s.IsDelivered, s.IsRto, s.LabelUrl, s.ManifestUrl, s.InvoiceUrl,
        s.PickupScheduledAt, s.PickupTokenNumber, s.LastTrackedAt, s.LastError, s.CreatedAt,
        trackingUrl = s.ProviderAccount == null ? null : ShippingService.TrackingUrl(s.ProviderAccount, s.AwbCode),
        events = s.Events.OrderByDescending(e => e.OccurredAt).Select(e => new { e.OccurredAt, e.Status, e.Activity, e.Location, e.Source }),
    };
}

public sealed class ShippingPartnerRequest
{
    public string? ProviderCode { get; set; }
    public string? DisplayName { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsDefault { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? ApiUsername { get; set; }
    public string? ApiPassword { get; set; }
    public int? TokenLifetimeHours { get; set; }
    public string? PickupLocation { get; set; }
    public string? PickupPincode { get; set; }
    public string? ChannelId { get; set; }
    public string? TrackingUrlTemplate { get; set; }
    public decimal? DefaultLengthCm { get; set; }
    public decimal? DefaultBreadthCm { get; set; }
    public decimal? DefaultHeightCm { get; set; }
    public decimal? PackagingWeightKg { get; set; }
    public decimal? MinWeightKg { get; set; }
    public string? DefaultHsn { get; set; }
    public decimal? DefaultTaxPercent { get; set; }
    public bool? AutoSchedulePickup { get; set; }
    public bool? AutoGenerateLabel { get; set; }
    public bool? SyncOrderStatus { get; set; }
    public int? TrackingPollMinutes { get; set; }
    public bool? RegenerateWebhookToken { get; set; }
}

public sealed record ShippingRatesRequest(decimal? WeightKg, decimal? LengthCm, decimal? BreadthCm, decimal? HeightCm);

public class PickupLocationRequest
{
    public string? Name { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public bool MakeDefault { get; set; } = true;
}

public class RateCalculatorRequest
{
    public string? PickupPincode { get; set; }
    public string? DeliveryPincode { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? BreadthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public bool IsCod { get; set; }
    public decimal? DeclaredValue { get; set; }
}
