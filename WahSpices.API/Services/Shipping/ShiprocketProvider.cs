using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WahSpices.Domain.Entities;

namespace WahSpices.API.Services.Shipping;

/// <summary>
/// Shiprocket adapter (https://apidocs.shiprocket.in). Uses an API user (Settings → API → API Users) whose
/// email/password are stored encrypted on the account. The bearer token (valid 240 h) is cached encrypted on the
/// account and refreshed 12 h before expiry, or immediately when Shiprocket answers 401.
/// Note: every call except rates/tracking/pickup-list changes real data in the Shiprocket account.
/// </summary>
public sealed class ShiprocketProvider(IHttpClientFactory httpFactory, ILogger<ShiprocketProvider> logger) : IShippingProvider
{
    public const string ProviderCode = "shiprocket";
    public const string HttpClientName = "shipping";
    public string Code => ProviderCode;
    public string Name => "Shiprocket";

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> LoginLocks = new();
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    // ───────────────────────── auth ─────────────────────────

    private async Task<string> TokenAsync(ShippingProviderAccount a, bool force, CancellationToken ct)
    {
        if (!force && !string.IsNullOrWhiteSpace(a.AuthToken) && !a.AuthToken.StartsWith("enc:", StringComparison.Ordinal)
            && a.TokenExpiresAt > DateTime.UtcNow.AddHours(12))
            return a.AuthToken;

        var gate = LoginLocks.GetOrAdd(a.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!force && !string.IsNullOrWhiteSpace(a.AuthToken) && a.TokenExpiresAt > DateTime.UtcNow.AddHours(12))
                return a.AuthToken!;
            if (string.IsNullOrWhiteSpace(a.ApiUsername) || string.IsNullOrWhiteSpace(a.ApiPassword))
                throw new ShippingProviderException("Shiprocket API email and password are not set. Add them in Admin → Shipping → Partners.");

            using var req = new HttpRequestMessage(HttpMethod.Post, Url(a, "/auth/login"))
            {
                Content = Json(new { email = a.ApiUsername.Trim(), password = a.ApiPassword }),
            };
            using var res = await Client().SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                a.AuthToken = null; a.TokenExpiresAt = null;
                var reason = ErrorText(body, res.StatusCode);
                var hint = reason.Contains("inactive", StringComparison.OrdinalIgnoreCase)
                    ? " The API User is switched off — in Shiprocket → Settings → API → Configure, set it to Active (or create a new API User) and save its email/password here."
                    : res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                        ? $" Check the API email ({a.ApiUsername.Trim()}) and password — use an API User from Shiprocket → Settings → API (its email must differ from your main Shiprocket login)."
                        : "";
                throw new ShippingProviderException($"Shiprocket login failed: {reason}.{hint}", (int)res.StatusCode);
            }
            using var doc = JsonDocument.Parse(body);
            var token = Str(doc.RootElement, "token");
            if (string.IsNullOrWhiteSpace(token)) throw new ShippingProviderException("Shiprocket login returned no token.");
            a.AuthToken = token;
            a.TokenExpiresAt = DateTime.UtcNow.AddHours(Math.Clamp(a.TokenLifetimeHours, 1, 24 * 30));
            return token;
        }
        finally { gate.Release(); }
    }

    private HttpClient Client() => httpFactory.CreateClient(HttpClientName);

    private static string Url(ShippingProviderAccount a, string path)
        => (string.IsNullOrWhiteSpace(a.ApiBaseUrl) ? "https://apiv2.shiprocket.in/v1/external" : a.ApiBaseUrl.Trim()).TrimEnd('/') + path;

    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    /// <summary>Authenticated call; retries once with a fresh token on 401.</summary>
    private async Task<JsonElement> CallAsync(ShippingProviderAccount a, HttpMethod method, string path, object? body, CancellationToken ct, bool allowError = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await TokenAsync(a, force: attempt > 0, ct);
            using var req = new HttpRequestMessage(method, Url(a, path));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body != null) req.Content = Json(body);

            HttpResponseMessage res;
            try { res = await Client().SendAsync(req, ct); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            { throw new ShippingProviderException("Shiprocket did not answer in time. Please try again."); }
            catch (HttpRequestException ex)
            { throw new ShippingProviderException($"Could not reach Shiprocket ({ex.Message})."); }

            using (res)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { a.AuthToken = null; continue; }
                if (!res.IsSuccessStatusCode && !allowError)
                {
                    logger.LogWarning("Shiprocket {Method} {Path} → {Status}: {Body}", method, path, (int)res.StatusCode, Trim(text, 500));
                    throw new ShippingProviderException(ErrorText(text, res.StatusCode), (int)res.StatusCode);
                }
                if (string.IsNullOrWhiteSpace(text)) return default;
                try { return JsonDocument.Parse(text).RootElement.Clone(); }
                catch (JsonException) { throw new ShippingProviderException($"Shiprocket sent an unexpected reply ({(int)res.StatusCode})."); }
            }
        }
    }

    // ───────────────────────── operations ─────────────────────────

    public async Task<ProviderTestResult> TestAsync(ShippingProviderAccount a, CancellationToken ct = default)
    {
        try
        {
            await TokenAsync(a, force: true, ct);
            var pickups = await GetPickupLocationsAsync(a, ct);
            var msg = pickups.Count == 0
                ? "Connected, but no pickup address exists in Shiprocket yet (Settings → Pickup Addresses)."
                : $"Connected. {pickups.Count} pickup address(es): {string.Join(", ", pickups.Select(p => p.Name))}.";
            if (!string.IsNullOrWhiteSpace(a.PickupLocation) && pickups.Count > 0
                && !pickups.Any(p => string.Equals(p.Name, a.PickupLocation.Trim(), StringComparison.OrdinalIgnoreCase)))
                msg += $" Warning: pickup location '{a.PickupLocation}' was not found — pick one of the above.";
            return new ProviderTestResult(true, msg, pickups.Count);
        }
        catch (ShippingProviderException ex) { return new ProviderTestResult(false, ex.Message); }
    }

    public async Task<IReadOnlyList<PickupLocationInfo>> GetPickupLocationsAsync(ShippingProviderAccount a, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Get, "/settings/company/pickup", null, ct);
        var list = new List<PickupLocationInfo>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty("shipping_address", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
                list.Add(new PickupLocationInfo(
                    Str(p, "pickup_location") ?? "",
                    Str(p, "pin_code"), Str(p, "city"), Str(p, "state"),
                    string.Join(", ", new[] { Str(p, "address"), Str(p, "address_2") }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    Int(p, "is_primary_location") == 1,
                    Int(p, "status") is null or 1 or 2));
        }
        return list;
    }

    public async Task<string> AddPickupLocationAsync(ShippingProviderAccount a, NewPickupLocation l, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Post, "/settings/company/addpickup", new Dictionary<string, object?>
        {
            ["pickup_location"] = l.Name.Trim(),
            ["name"] = l.ContactName.Trim(),
            ["email"] = l.Email.Trim(),
            ["phone"] = l.Phone.Trim(),
            ["address"] = l.Address.Trim(),
            ["address_2"] = l.Address2?.Trim() ?? "",
            ["city"] = l.City.Trim(),
            ["state"] = l.State.Trim(),
            ["country"] = string.IsNullOrWhiteSpace(l.Country) ? "India" : l.Country.Trim(),
            ["pin_code"] = l.Pincode.Trim(),
        }, ct, allowError: true);
        var ok = root.ValueKind == JsonValueKind.Object && (Bool(root, "success") || root.TryGetProperty("address", out _));
        if (!ok)
        {
            var why = ErrorText(root.ValueKind == JsonValueKind.Undefined ? "" : root.GetRawText(), HttpStatusCode.BadRequest);
            // Shiprocket's field names → the labels the admin sees
            foreach (var (key, label) in new[] { ("pickup_location", "Nickname"), ("name", "Contact name"), ("phone", "Phone"), ("email", "Email"),
                         ("address_2", "Address line 2"), ("address", "Address line 1"), ("pin_code", "Pincode"), ("city", "City"), ("state", "State") })
                why = System.Text.RegularExpressions.Regex.Replace(why, $@"(^|· ){key}:", $"$1{label}:");
            throw new ShippingProviderException($"Shiprocket did not add the pickup address: {why}");
        }
        return l.Name.Trim();
    }

    public async Task<IReadOnlyList<CourierQuote>> GetRatesAsync(ShippingProviderAccount a, RateRequest r, CancellationToken ct = default)
    {
        var q = new StringBuilder("/courier/serviceability/?");
        q.Append("pickup_postcode=").Append(Uri.EscapeDataString(r.PickupPincode));
        q.Append("&delivery_postcode=").Append(Uri.EscapeDataString(r.DeliveryPincode));
        q.Append("&weight=").Append(r.WeightKg.ToString("0.###", CultureInfo.InvariantCulture));
        q.Append("&cod=").Append(r.IsCod ? 1 : 0);
        q.Append("&length=").Append(Num(r.LengthCm)).Append("&breadth=").Append(Num(r.BreadthCm)).Append("&height=").Append(Num(r.HeightCm));
        q.Append("&declared_value=").Append(Num(r.DeclaredValue));

        var root = await CallAsync(a, HttpMethod.Get, q.ToString(), null, ct, allowError: true);
        if (root.ValueKind != JsonValueKind.Object) return [];
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            var status = Int(root, "status") ?? Int(root, "status_code");
            if (status is 404 or 200) return [];   // "no courier serviceable"
            throw new ShippingProviderException(ErrorText(root.GetRawText(), HttpStatusCode.BadRequest));
        }
        var recommended = Str(data, "shiprocket_recommended_courier_id") ?? Str(data, "recommended_courier_company_id");
        var list = new List<CourierQuote>();
        if (data.TryGetProperty("available_courier_companies", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                var id = Str(c, "courier_company_id") ?? "";
                var freight = Dec(c, "freight_charge") ?? 0;
                var cod = Dec(c, "cod_charges") ?? 0;
                var rate = Dec(c, "rate") ?? freight + cod;
                var days = Str(c, "estimated_delivery_days");
                list.Add(new CourierQuote
                {
                    CourierCompanyId = id,
                    CourierName = Str(c, "courier_name") ?? $"Courier {id}",
                    Rate = Math.Round(rate, 2),
                    Freight = Math.Round(freight, 2),
                    CodCharge = Math.Round(cod, 2),
                    ChargeableWeightKg = Dec(c, "charge_weight") ?? r.WeightKg,
                    EstimatedDays = days,
                    EstimatedDaysMax = ParseMaxDays(days),
                    Etd = Str(c, "etd"),
                    Rating = Dec(c, "rating"),
                    IsSurface = Bool(c, "is_surface"),
                    CodAvailable = Int(c, "cod") is null or 1,
                    Recommended = id.Length > 0 && id == recommended,
                    PickupAvailability = Str(c, "pickup_availability"),
                    Tracking = Str(c, "realtime_tracking"),
                });
            }
        }
        return list;
    }

    public async Task<ProviderOrderResult> CreateOrderAsync(ShippingProviderAccount a, ShipmentRequest r, CancellationToken ct = default)
    {
        var (first, last) = SplitName(r.CustomerName);
        var body = new Dictionary<string, object?>
        {
            ["order_id"] = r.OrderRef,
            ["order_date"] = (r.OrderDate + Ist).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["pickup_location"] = a.PickupLocation?.Trim(),
            ["comment"] = r.Comment,
            ["billing_customer_name"] = first,
            ["billing_last_name"] = last,
            ["billing_address"] = r.Address,
            ["billing_address_2"] = r.Address2 ?? "",
            ["billing_city"] = r.City,
            ["billing_pincode"] = r.Pincode,
            ["billing_state"] = r.State,
            ["billing_country"] = r.Country,
            ["billing_email"] = r.Email,
            ["billing_phone"] = r.Phone,
            ["shipping_is_billing"] = true,
            ["order_items"] = r.Lines.Select(l => new Dictionary<string, object?>
            {
                ["name"] = l.Name, ["sku"] = l.Sku, ["units"] = l.Units,
                ["selling_price"] = l.SellingPrice, ["discount"] = 0, ["tax"] = l.TaxPercent, ["hsn"] = l.Hsn ?? "",
            }).ToList(),
            ["payment_method"] = r.IsCod ? "COD" : "Prepaid",
            ["shipping_charges"] = r.ShippingCharges,
            ["giftwrap_charges"] = 0,
            ["transaction_charges"] = 0,
            ["total_discount"] = r.TotalDiscount,
            ["sub_total"] = r.SubTotal,
            ["length"] = r.LengthCm,
            ["breadth"] = r.BreadthCm,
            ["height"] = r.HeightCm,
            ["weight"] = r.WeightKg,
        };
        if (!string.IsNullOrWhiteSpace(a.ChannelId)) body["channel_id"] = a.ChannelId.Trim();

        var root = await CallAsync(a, HttpMethod.Post, "/orders/create/adhoc", body, ct);
        var orderId = Str(root, "order_id");
        var shipmentId = Str(root, "shipment_id");
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(shipmentId))
            throw new ShippingProviderException($"Shiprocket did not create the order: {ErrorText(root.GetRawText(), HttpStatusCode.BadRequest)}");
        return new ProviderOrderResult(orderId, shipmentId, Str(root, "status"));
    }

    public async Task<AwbResult> AssignAwbAsync(ShippingProviderAccount a, string shipmentId, string? courierCompanyId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["shipment_id"] = shipmentId };
        if (!string.IsNullOrWhiteSpace(courierCompanyId)) body["courier_id"] = courierCompanyId;
        var root = await CallAsync(a, HttpMethod.Post, "/courier/assign/awb", body, ct);
        var data = Path(root, "response", "data");
        var awb = data is { } d ? Str(d, "awb_code") : null;
        if (Int(root, "awb_assign_status") != 1 || string.IsNullOrWhiteSpace(awb))
        {
            var err = (data is { } dd ? Str(dd, "awb_assign_error") : null) ?? Str(root, "message") ?? "Courier could not be assigned.";
            throw new ShippingProviderException($"AWB not assigned: {err}");
        }
        return new AwbResult(awb!, Str(data!.Value, "courier_company_id"), Str(data.Value, "courier_name"));
    }

    public async Task<PickupResult> SchedulePickupAsync(ShippingProviderAccount a, string shipmentId, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Post, "/courier/generate/pickup", new { shipment_id = new[] { ToIdValue(shipmentId) } }, ct, allowError: true);
        var resp = Path(root, "response");
        if (Int(root, "pickup_status") == 1 && resp is { } r)
            return new PickupResult(ParseIst(Str(r, "pickup_scheduled_date")), Str(r, "pickup_token_number"), Str(r, "data"));
        var msg = Str(root, "message") ?? (resp is { } rr ? Str(rr, "data") : null) ?? "Pickup was not scheduled.";
        if (msg.Contains("already", StringComparison.OrdinalIgnoreCase)) return new PickupResult(null, null, msg);
        throw new ShippingProviderException($"Pickup not scheduled: {msg}");
    }

    public async Task<string> GenerateLabelAsync(ShippingProviderAccount a, string shipmentId, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Post, "/courier/generate/label", new { shipment_id = new[] { ToIdValue(shipmentId) } }, ct);
        var url = Str(root, "label_url");
        if (string.IsNullOrWhiteSpace(url)) throw new ShippingProviderException($"Label not created: {Str(root, "response") ?? Str(root, "message") ?? "unknown error"}");
        return url;
    }

    public async Task<string> GenerateManifestAsync(ShippingProviderAccount a, string shipmentId, string? providerOrderId, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Post, "/manifests/generate", new { shipment_id = new[] { ToIdValue(shipmentId) } }, ct, allowError: true);
        var url = Str(root, "manifest_url");
        if (!string.IsNullOrWhiteSpace(url)) return url;
        if (!string.IsNullOrWhiteSpace(providerOrderId))   // already generated → print it instead
        {
            var printed = await CallAsync(a, HttpMethod.Post, "/manifests/print", new { order_ids = new[] { ToIdValue(providerOrderId) } }, ct, allowError: true);
            url = Str(printed, "manifest_url");
            if (!string.IsNullOrWhiteSpace(url)) return url;
        }
        throw new ShippingProviderException($"Manifest not generated: {Str(root, "message") ?? "unknown error"}");
    }

    public async Task<string> PrintInvoiceAsync(ShippingProviderAccount a, string providerOrderId, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Post, "/orders/print/invoice", new { ids = new[] { providerOrderId } }, ct);
        var url = Str(root, "invoice_url");
        if (string.IsNullOrWhiteSpace(url)) throw new ShippingProviderException("Shiprocket did not return an invoice.");
        return url;
    }

    public async Task CancelAsync(ShippingProviderAccount a, string providerOrderId, CancellationToken ct = default)
        => await CallAsync(a, HttpMethod.Post, "/orders/cancel", new { ids = new[] { ToIdValue(providerOrderId) } }, ct);

    public async Task<TrackingUpdate?> TrackAsync(ShippingProviderAccount a, string awb, CancellationToken ct = default)
    {
        var root = await CallAsync(a, HttpMethod.Get, $"/courier/track/awb/{Uri.EscapeDataString(awb)}", null, ct, allowError: true);
        if (root.ValueKind != JsonValueKind.Object) return null;
        JsonElement td;
        if (root.TryGetProperty("tracking_data", out var t)) td = t;
        else if (root.TryGetProperty(awb, out var w) && w.ValueKind == JsonValueKind.Object && w.TryGetProperty("tracking_data", out var t2)) td = t2;
        else return null;
        if (td.ValueKind != JsonValueKind.Object) return null;

        string? current = null, courier = null; DateTime? etd = ParseIst(Str(td, "etd"));
        if (td.TryGetProperty("shipment_track", out var st) && st.ValueKind == JsonValueKind.Array)
            foreach (var s in st.EnumerateArray())
            {
                current ??= Str(s, "current_status");
                courier ??= Str(s, "courier_name");
                etd ??= ParseIst(Str(s, "edd"));
            }
        var scans = new List<TrackingScan>();
        if (td.TryGetProperty("shipment_track_activities", out var acts) && acts.ValueKind == JsonValueKind.Array)
            foreach (var s in acts.EnumerateArray())
            {
                var when = ParseIst(Str(s, "date"));
                if (when == null) continue;
                var label = Str(s, "sr-status-label");
                scans.Add(new TrackingScan(when.Value, label is null or "NA" ? (Str(s, "status") ?? "") : label, Str(s, "activity"), Str(s, "location")));
            }
        if (current == null && scans.Count == 0) return null;
        return new TrackingUpdate
        {
            Awb = awb, CurrentStatus = current, StatusCode = Str(td, "shipment_status"), CourierName = courier, Etd = etd,
            IsReturn = current?.Contains("RTO", StringComparison.OrdinalIgnoreCase) == true, Scans = scans,
        };
    }

    public TrackingUpdate? ParseWebhook(JsonElement b)
    {
        if (b.ValueKind != JsonValueKind.Object) return null;
        var awb = Str(b, "awb");
        if (string.IsNullOrWhiteSpace(awb)) return null;
        var scans = new List<TrackingScan>();
        if (b.TryGetProperty("scans", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var s in arr.EnumerateArray())
            {
                var when = ParseIst(Str(s, "date"));
                if (when == null) continue;
                var label = Str(s, "sr-status-label");
                scans.Add(new TrackingScan(when.Value, label is null or "NA" ? (Str(s, "status") ?? "") : label, Str(s, "activity"), Str(s, "location")));
            }
        return new TrackingUpdate
        {
            Awb = awb!,
            CurrentStatus = Str(b, "current_status") ?? Str(b, "shipment_status"),
            StatusCode = Str(b, "current_status_id") ?? Str(b, "shipment_status_id"),
            CourierName = Str(b, "courier_name"),
            Etd = ParseIst(Str(b, "etd")),
            IsReturn = Int(b, "is_return") == 1,
            Scans = scans,
        };
    }

    // ───────────────────────── helpers ─────────────────────────

    private static object ToIdValue(string id) => long.TryParse(id, out var n) ? n : id;
    private static string Num(decimal d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n];

    private static (string first, string last) SplitName(string name)
    {
        var n = (name ?? "").Trim();
        var i = n.IndexOf(' ');
        return i <= 0 ? (n.Length == 0 ? "Customer" : n, "") : (n[..i], n[(i + 1)..].Trim());
    }

    private static int? ParseMaxDays(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var nums = System.Text.RegularExpressions.Regex.Matches(s, @"\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        return nums.Count == 0 ? null : nums.Max();
    }

    /// <summary>Shiprocket dates are IST ("2023-05-19 11:59:16", "23 05 2023 11:43:52", "Mar 10, 2024") → UTC.</summary>
    internal static DateTime? ParseIst(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "dd MM yyyy HH:mm:ss", "dd-MM-yyyy HH:mm:ss",
                            "MMM dd, yyyy", "MMM d, yyyy", "dd MMM yyyy, hh:mm tt", "yyyy-MM-ddTHH:mm:ss"];
        if (DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d))
            return DateTime.SpecifyKind(d - Ist, DateTimeKind.Utc);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var g) ? g : null;
    }

    private static JsonElement? Path(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(n, out var next)) return null;
            e = next;
        }
        return e;
    }

    internal static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static decimal? Dec(JsonElement e, string name)
        => decimal.TryParse(Str(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? Int(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (s == "true") return 1;
        if (s == "false") return 0;
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (int)d : null;
    }

    private static bool Bool(JsonElement e, string name) => Int(e, name) == 1;

    /// <summary>Readable error from a Shiprocket error body: "message" plus field errors.</summary>
    private static string ErrorText(string body, HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var parts = new List<string>();
            // "message" may be plain text, an object of field errors, or a JSON string of one.
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message", out var m))
            {
                var inner = m;
                if (m.ValueKind == JsonValueKind.String && m.GetString()!.TrimStart().StartsWith('{'))
                {
                    try { inner = JsonDocument.Parse(m.GetString()!).RootElement.Clone(); } catch (JsonException) { }
                }
                if (inner.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in inner.EnumerateObject())
                        parts.Add(p.Value.ValueKind == JsonValueKind.Array
                            ? $"{p.Name}: {string.Join(" ", p.Value.EnumerateArray().Select(x => x.ToString()))}"
                            : $"{p.Name}: {p.Value}");
                }
                else if (inner.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(inner.GetString())) parts.Add(inner.GetString()!);
            }
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Object)
                foreach (var p in errs.EnumerateObject())
                    parts.Add(p.Value.ValueKind == JsonValueKind.Array
                        ? $"{p.Name}: {string.Join(" ", p.Value.EnumerateArray().Select(x => x.ToString()))}"
                        : $"{p.Name}: {p.Value}");
            if (parts.Count > 0) return string.Join(" · ", parts);
        }
        catch (JsonException) { }
        return $"Shiprocket error {(int)status}.";
    }
}
