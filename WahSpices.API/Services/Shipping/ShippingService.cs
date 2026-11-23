using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Services.Shipping;

public sealed record ParcelSpec(decimal WeightKg, decimal LengthCm, decimal BreadthCm, decimal HeightCm);

public sealed record PartnerQuotes(int AccountId, string ProviderCode, string PartnerName, bool Ok, string? Error, IReadOnlyList<CourierQuote> Quotes);

public sealed record ShipRequest(int AccountId, string? CourierCompanyId, decimal? FreightCharge, string? EstimatedDays,
    decimal? WeightKg, decimal? LengthCm, decimal? BreadthCm, decimal? HeightCm, bool? SchedulePickup);

/// <summary>
/// Shipping workflow on top of the provider adapters: compare rates across every enabled partner, create the
/// shipment + AWB + pickup + label in one go, and turn courier updates (webhook or polling) into store order
/// status changes (with the customer email) and a tracking timeline.
/// </summary>
public sealed class ShippingService(
    AppDbContext db,
    ShippingProviderRegistry registry,
    IOrderEmails orderEmails,
    IInvoiceService invoices,
    ILogger<ShippingService> logger)
{
    // ───────────────────────── parcel defaults ─────────────────────────

    /// <summary>Estimated parcel for an order: product weight (variant grams × qty) + packaging, account box size.</summary>
    public async Task<ParcelSpec> EstimateParcelAsync(Order order, ShippingProviderAccount account, CancellationToken ct = default)
    {
        var variantIds = order.Items.Where(i => i.VariantId != null).Select(i => i.VariantId!.Value).Distinct().ToList();
        var grams = variantIds.Count == 0 ? new Dictionary<int, int>()
            : await db.ProductVariants.Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.WeightGrams, ct);
        decimal totalGrams = 0;
        foreach (var i in order.Items.Where(i => !i.IsDeleted))
        {
            var g = i.VariantId != null && grams.TryGetValue(i.VariantId.Value, out var vg) && vg > 0 ? vg : ParseGrams(i.Weight);
            totalGrams += g * i.Quantity;
        }
        var kg = totalGrams / 1000m + account.PackagingWeightKg;
        kg = Math.Max(kg, account.MinWeightKg <= 0 ? 0.1m : account.MinWeightKg);
        return new ParcelSpec(Math.Round(kg, 3), account.DefaultLengthCm, account.DefaultBreadthCm, account.DefaultHeightCm);
    }

    /// <summary>"100g" / "1 kg" / "500 gm" → grams (100 when unknown).</summary>
    public static decimal ParseGrams(string? weight)
    {
        if (string.IsNullOrWhiteSpace(weight)) return 100;
        var m = Regex.Match(weight.ToLowerInvariant(), @"([\d.]+)\s*(kg|g|gm|gms|gram|grams)?");
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return 100;
        return m.Groups[2].Value == "kg" ? n * 1000 : n;
    }

    // ───────────────────────── rate comparison ─────────────────────────

    public async Task<(Order order, ParcelSpec parcel, List<PartnerQuotes> partners)> CompareRatesAsync(int orderId, ParcelSpec? overrideParcel, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var accounts = await db.ShippingProviderAccounts.Where(a => a.IsEnabled && !a.IsDeleted).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToListAsync(ct);
        if (accounts.Count == 0)
            throw new ShippingProviderException("No shipping partner is connected. Add one in Admin → Shipping → Partners.");

        var parcel = overrideParcel ?? await EstimateParcelAsync(order, accounts[0], ct);
        var isCod = order.PaymentMethod == PaymentMethod.CashOnDelivery && order.PaymentStatus != PaymentStatus.Paid;

        var partners = await QuoteAllAsync(accounts, null, order.Address.Pincode, parcel, isCod, order.GrandTotal, ct);
        return (order, parcel, partners);
    }

    /// <summary>Stand-alone rate calculator: any pincodes / parcel, no order needed.</summary>
    public async Task<List<PartnerQuotes>> CalculateRatesAsync(string? pickupPincode, string deliveryPincode, ParcelSpec parcel, bool isCod, decimal declaredValue, CancellationToken ct = default)
    {
        var accounts = await db.ShippingProviderAccounts.Where(a => a.IsEnabled && !a.IsDeleted).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToListAsync(ct);
        if (accounts.Count == 0)
            throw new ShippingProviderException("No shipping partner is connected. Add one in Admin → Shipping → Partners.");
        return await QuoteAllAsync(accounts, pickupPincode, deliveryPincode, parcel, isCod, declaredValue, ct);
    }

    /// <summary>Asks every partner in parallel; pickupPincode null = each partner's own saved pickup pincode.</summary>
    private async Task<List<PartnerQuotes>> QuoteAllAsync(List<ShippingProviderAccount> accounts, string? pickupPincode, string deliveryPincode,
        ParcelSpec parcel, bool isCod, decimal declaredValue, CancellationToken ct)
    {
        // All partners in parallel, each with its own time budget — one slow/broken partner never blocks the rest.
        var tasks = accounts.Select(async a =>
        {
            try
            {
                if (!registry.Has(a.ProviderCode)) return new PartnerQuotes(a.Id, a.ProviderCode, a.DisplayName, false, "No adapter installed for this partner.", []);
                var from = string.IsNullOrWhiteSpace(pickupPincode) ? a.PickupPincode?.Trim() : pickupPincode.Trim();
                if (string.IsNullOrWhiteSpace(from)) return new PartnerQuotes(a.Id, a.ProviderCode, a.DisplayName, false, "Pickup pincode is not set for this partner.", []);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                var quotes = await registry.Get(a.ProviderCode).GetRatesAsync(a, new RateRequest(from!, deliveryPincode.Trim(),
                    parcel.WeightKg, parcel.LengthCm, parcel.BreadthCm, parcel.HeightCm, isCod, declaredValue), cts.Token);
                return new PartnerQuotes(a.Id, a.ProviderCode, a.DisplayName, true, quotes.Count == 0 ? "No courier serves this pincode for this parcel." : null,
                    quotes.OrderBy(q => q.Rate).ToList());
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { return new PartnerQuotes(a.Id, a.ProviderCode, a.DisplayName, false, "Partner did not answer in time.", []); }
            catch (ShippingProviderException ex)
            { return new PartnerQuotes(a.Id, a.ProviderCode, a.DisplayName, false, ex.Message, []); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rate check failed for partner {Partner}", a.DisplayName);
                return new PartnerQuotes(a.Id, a.ProviderCode, a.DisplayName, false, "Unexpected error while fetching rates.", []);
            }
        }).ToList();
        var partners = (await Task.WhenAll(tasks)).ToList();
        await db.SaveChangesAsync(ct);   // refreshed tokens
        return partners;
    }

    // ───────────────────────── create shipment ─────────────────────────

    public async Task<Shipment> ShipAsync(int orderId, ShipRequest req, string adminName, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.Returned or OrderStatus.Refunded or OrderStatus.ReturnRequested)
            throw new ShippingProviderException($"A {order.Status} order can't be shipped.");
        if (order.PaymentMethod != PaymentMethod.CashOnDelivery && order.PaymentStatus != PaymentStatus.Paid)
            throw new ShippingProviderException("This online order is not paid yet — it can't be shipped.");

        var active = await db.Shipments.Where(s => s.OrderId == orderId && !s.IsCancelled && !s.IsDeleted).OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        if (active?.AwbCode != null)
            throw new ShippingProviderException($"This order already has AWB {active.AwbCode}. Cancel that shipment first to re-ship.");

        var account = await db.ShippingProviderAccounts.FirstOrDefaultAsync(a => a.Id == req.AccountId && !a.IsDeleted, ct)
                      ?? throw new ShippingProviderException("Shipping partner not found.");
        if (!account.IsEnabled) throw new ShippingProviderException($"{account.DisplayName} is disabled.");
        if (string.IsNullOrWhiteSpace(account.PickupLocation)) throw new ShippingProviderException($"Set the pickup location for {account.DisplayName} first.");
        var provider = registry.Get(account.ProviderCode);

        var est = await EstimateParcelAsync(order, account, ct);
        var parcel = new ParcelSpec(
            Positive(req.WeightKg) ?? est.WeightKg, Positive(req.LengthCm) ?? est.LengthCm,
            Positive(req.BreadthCm) ?? est.BreadthCm, Positive(req.HeightCm) ?? est.HeightCm);
        var isCod = order.PaymentMethod == PaymentMethod.CashOnDelivery && order.PaymentStatus != PaymentStatus.Paid;

        // Reuse a provider order that was created but never got an AWB (e.g. the courier step failed last time)
        var shipment = active != null && active.ProviderAccountId == account.Id ? active : null;
        if (active != null && shipment == null) { active.IsCancelled = true; active.Status = "SUPERSEDED"; }
        if (shipment == null)
        {
            var attempt = await db.Shipments.CountAsync(s => s.OrderId == orderId && s.ProviderAccountId == account.Id, ct);
            var orderRef = attempt == 0 ? order.OrderNumber : $"{order.OrderNumber}-{attempt + 1}";   // provider order ids must be unique
            var request = BuildRequest(order, account, parcel, isCod, orderRef);
            ProviderOrderResult created;
            try { created = await provider.CreateOrderAsync(account, request, ct); }
            finally { await db.SaveChangesAsync(ct); }   // keep a refreshed token even when the call fails

            shipment = new Shipment
            {
                OrderId = order.Id, ProviderAccountId = account.Id, ProviderCode = account.ProviderCode,
                ProviderOrderId = created.ProviderOrderId, ProviderShipmentId = created.ProviderShipmentId,
                Status = created.Status ?? "NEW", IsCod = isCod, CodAmount = isCod ? order.GrandTotal : 0,
                WeightKg = parcel.WeightKg, LengthCm = parcel.LengthCm, BreadthCm = parcel.BreadthCm, HeightCm = parcel.HeightCm,
                CreatedBy = adminName,
            };
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync(ct);
        }

        // Courier + AWB
        try
        {
            var awb = await provider.AssignAwbAsync(account, shipment.ProviderShipmentId!, req.CourierCompanyId, ct);
            shipment.AwbCode = awb.AwbCode;
            shipment.CourierCompanyId = awb.CourierCompanyId ?? req.CourierCompanyId;
            shipment.CourierName = awb.CourierName;
            shipment.FreightCharge = req.FreightCharge;
            shipment.EstimatedDeliveryDays = req.EstimatedDays;
            shipment.Status = "AWB ASSIGNED";
            shipment.LastError = null;
        }
        catch (ShippingProviderException ex)
        {
            shipment.LastError = ex.Message;
            await db.SaveChangesAsync(ct);
            throw;
        }

        order.Courier = shipment.CourierName ?? account.DisplayName;
        order.AwbNumber = shipment.AwbCode;
        order.CourierTrackingUrl = TrackingUrl(account, shipment.AwbCode);
        order.UpdatedAt = DateTime.UtcNow; order.UpdatedBy = adminName;
        AddEvent(shipment, DateTime.UtcNow, "AWB ASSIGNED", $"{shipment.CourierName} · AWB {shipment.AwbCode}", null, "admin");
        if (order.Status < OrderStatus.Packed)
            ChangeOrderStatus(order, OrderStatus.Packed, $"Shipment booked with {shipment.CourierName} via {account.DisplayName} (AWB {shipment.AwbCode})", adminName, email: false);
        else
            db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, Status = order.Status, UpdatedBy = adminName,
                Note = $"Shipment booked with {shipment.CourierName} via {account.DisplayName} (AWB {shipment.AwbCode})" });
        await db.SaveChangesAsync(ct);

        // Optional follow-ups — failures here never undo the AWB
        if (req.SchedulePickup ?? account.AutoSchedulePickup) await TrySchedulePickupAsync(shipment, account, ct);
        if (account.AutoGenerateLabel) await TryLabelAsync(shipment, account, ct);
        await db.SaveChangesAsync(ct);
        await FlushSideEffectsAsync(adminName);
        return shipment;
    }

    private ShipmentRequest BuildRequest(Order order, ShippingProviderAccount account, ParcelSpec parcel, bool isCod, string orderRef)
    {
        var lines = order.Items.Where(i => !i.IsDeleted).Select(i => new ShipmentLine(
            Name: string.IsNullOrWhiteSpace(i.Weight) ? i.ProductName : $"{i.ProductName} {i.Weight}",
            Sku: $"WAH-{i.ProductId}" + (i.VariantId != null ? $"-{i.VariantId}" : ""),
            Units: i.Quantity, SellingPrice: Math.Round(i.Price, 2), TaxPercent: account.DefaultTaxPercent, Hsn: account.DefaultHsn)).ToList();
        var subTotal = lines.Sum(l => l.SellingPrice * l.Units);
        // sub_total + shipping - discount must equal what the customer pays (= COD amount)
        var discount = Math.Max(0, Math.Round(subTotal + order.DeliveryCharge - order.GrandTotal, 2));
        var a = order.Address;
        var phone = new string((a.Phone ?? order.User.Phone ?? "").Where(char.IsDigit).ToArray());
        if (phone.Length > 10) phone = phone[^10..];
        var addr = string.Join(", ", new[] { a.AddressLine, a.Landmark }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ShipmentRequest
        {
            OrderRef = orderRef, OrderDate = order.CreatedAt,
            CustomerName = string.IsNullOrWhiteSpace(a.Name) ? order.User.Name : a.Name,
            Address = addr.Length >= 3 ? addr : $"{addr} {a.City}".Trim(),
            City = a.City, State = a.State, Pincode = a.Pincode.Trim(), Phone = phone,
            Email = string.IsNullOrWhiteSpace(order.User.Email) ? "orders@wahspices.in" : order.User.Email,
            Lines = lines, IsCod = isCod, SubTotal = subTotal, ShippingCharges = order.DeliveryCharge, TotalDiscount = discount,
            WeightKg = parcel.WeightKg, LengthCm = parcel.LengthCm, BreadthCm = parcel.BreadthCm, HeightCm = parcel.HeightCm,
        };
    }

    public async Task<bool> TrySchedulePickupAsync(Shipment s, ShippingProviderAccount account, CancellationToken ct)
    {
        try
        {
            var p = await registry.Get(account.ProviderCode).SchedulePickupAsync(account, s.ProviderShipmentId!, ct);
            s.PickupScheduledAt = p.ScheduledAt ?? s.PickupScheduledAt ?? DateTime.UtcNow;
            s.PickupTokenNumber = p.TokenNumber ?? s.PickupTokenNumber;
            if (s.Status is "AWB ASSIGNED" or "NEW") s.Status = "PICKUP SCHEDULED";
            AddEvent(s, DateTime.UtcNow, "PICKUP SCHEDULED", p.ScheduledAt != null ? $"Pickup on {p.ScheduledAt.Value.AddHours(5.5):dd MMM, hh:mm tt}" : p.Message, null, "admin");
            return true;
        }
        catch (ShippingProviderException ex) { s.LastError = ex.Message; return false; }
    }

    public async Task<bool> TryLabelAsync(Shipment s, ShippingProviderAccount account, CancellationToken ct)
    {
        try { s.LabelUrl = await registry.Get(account.ProviderCode).GenerateLabelAsync(account, s.ProviderShipmentId!, ct); return true; }
        catch (ShippingProviderException ex) { s.LastError = ex.Message; return false; }
    }

    public async Task CancelAsync(Shipment s, string adminName, CancellationToken ct)
    {
        var account = await db.ShippingProviderAccounts.FirstAsync(a => a.Id == s.ProviderAccountId, ct);
        if (!string.IsNullOrWhiteSpace(s.ProviderOrderId))
            await registry.Get(account.ProviderCode).CancelAsync(account, s.ProviderOrderId!, ct);
        s.IsCancelled = true;
        s.Status = "CANCELED";
        s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = adminName;
        AddEvent(s, DateTime.UtcNow, "CANCELED", $"Shipment cancelled by {adminName}", null, "admin");

        var order = await db.Orders.FirstAsync(o => o.Id == s.OrderId, ct);
        if (order.AwbNumber == s.AwbCode)
        {
            order.AwbNumber = null; order.Courier = null; order.CourierTrackingUrl = null;
            db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, Status = order.Status, UpdatedBy = adminName,
                Note = $"Shipment {s.AwbCode ?? s.ProviderOrderId} cancelled on {account.DisplayName}" });
        }
        await db.SaveChangesAsync(ct);
    }

    // ───────────────────────── tracking ─────────────────────────

    /// <summary>Pull the latest tracking for one shipment from its provider.</summary>
    public async Task<bool> RefreshTrackingAsync(Shipment s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.AwbCode)) return false;
        var account = s.ProviderAccount ?? await db.ShippingProviderAccounts.FirstAsync(a => a.Id == s.ProviderAccountId, ct);
        s.LastTrackedAt = DateTime.UtcNow;
        var update = await registry.Get(account.ProviderCode).TrackAsync(account, s.AwbCode!, ct);
        if (update != null) await ApplyUpdateAsync(s, account, update, "poll", ct);
        await db.SaveChangesAsync(ct);
        await FlushSideEffectsAsync($"{account.DisplayName} tracking");
        return update != null;
    }

    /// <summary>Webhook entry point: find the shipment by AWB and apply the update.</summary>
    public async Task<bool> ApplyWebhookAsync(ShippingProviderAccount account, TrackingUpdate update, CancellationToken ct)
    {
        var s = await db.Shipments.Include(x => x.Events).OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.AwbCode == update.Awb && x.ProviderAccountId == account.Id && !x.IsDeleted, ct);
        account.LastWebhookAt = DateTime.UtcNow;
        if (s == null) { await db.SaveChangesAsync(ct); return false; }
        await ApplyUpdateAsync(s, account, update, "webhook", ct);
        await db.SaveChangesAsync(ct);
        await FlushSideEffectsAsync($"{account.DisplayName} tracking");
        return true;
    }

    private async Task ApplyUpdateAsync(Shipment s, ShippingProviderAccount account, TrackingUpdate u, string source, CancellationToken ct)
    {
        if (s.Events.Count == 0 && s.Id > 0)
            await db.Entry(s).Collection(x => x.Events).LoadAsync(ct);

        foreach (var scan in u.Scans.OrderBy(x => x.OccurredAt))
        {
            var exists = s.Events.Any(e => Math.Abs((e.OccurredAt - scan.OccurredAt).TotalSeconds) < 1
                                           && string.Equals(e.Activity ?? e.Status, scan.Activity ?? scan.Status, StringComparison.OrdinalIgnoreCase));
            if (!exists) AddEvent(s, scan.OccurredAt, Short(scan.Status, 80), scan.Activity, scan.Location, source);
        }

        var status = (u.CurrentStatus ?? "").Trim().ToUpperInvariant();
        if (status.Length > 0)
        {
            if (!string.Equals(s.Status, status, StringComparison.OrdinalIgnoreCase) && !u.Scans.Any())
                AddEvent(s, DateTime.UtcNow, Short(status, 80), null, null, source);
            s.Status = Short(status, 80);
        }
        s.StatusCode = u.StatusCode ?? s.StatusCode;
        if (!string.IsNullOrWhiteSpace(u.CourierName)) s.CourierName = u.CourierName;
        if (u.Etd != null) s.Etd = u.Etd;
        s.LastTrackedAt = DateTime.UtcNow;
        if (status.Contains("CANCEL")) s.IsCancelled = true;
        if (u.IsReturn || status.StartsWith("RTO")) s.IsRto = true;
        if (status == "DELIVERED" && !s.IsRto) s.IsDelivered = true;

        if (!account.SyncOrderStatus || s.IsCancelled) return;
        var order = await db.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == s.OrderId, ct);
        if (order == null || order.AwbNumber != s.AwbCode) return;   // an older/replaced shipment never moves the order
        if (s.Etd != null) order.EstimatedDelivery = s.Etd;

        var target = MapStatus(status, s.IsRto);
        if (target == null) return;
        // Only move forward; never touch cancelled / returned / refunded orders
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned or OrderStatus.Refunded) return;
        if (target != OrderStatus.Returned && Rank(target.Value) <= Rank(order.Status)) return;
        if (target == OrderStatus.Returned && order.Status == OrderStatus.Returned) return;
        ChangeOrderStatus(order, target.Value, $"Courier update ({s.CourierName ?? account.DisplayName}): {status}", $"{account.DisplayName} tracking", email: true);
    }

    /// <summary>Provider status text → store order status (null = no change).</summary>
    public static OrderStatus? MapStatus(string status, bool isRto)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var s = status.ToUpperInvariant();
        if (isRto || s.StartsWith("RTO")) return s.Contains("DELIVERED") ? OrderStatus.Returned : null;
        if (s.Contains("CANCEL") || s.Contains("LOST") || s.Contains("DAMAGED") || s.Contains("DESTROYED")) return null;
        if (s == "DELIVERED") return OrderStatus.Delivered;
        if (s.Contains("OUT FOR DELIVERY")) return OrderStatus.OutForDelivery;
        if (s.Contains("PICKUP") && !s.Contains("COMPLETE")) return null;   // pickup scheduled / out for pickup / exception
        if (s.Contains("PICKED UP") || s == "SHIPPED" || s.Contains("PICKUP COMPLETE")) return OrderStatus.Dispatched;
        if (s.Contains("TRANSIT") || s.Contains("REACHED") || s.Contains("HUB") || s.Contains("MISROUTED")
            || s.Contains("UNDELIVERED") || s.Contains("DELAYED") || s.Contains("NDR"))
            return OrderStatus.InTransit;
        return null;
    }

    private static int Rank(OrderStatus s) => s switch
    {
        OrderStatus.Pending => 0, OrderStatus.Processing => 1, OrderStatus.Confirmed => 2, OrderStatus.Packed => 3,
        OrderStatus.Dispatched => 4, OrderStatus.InTransit => 5, OrderStatus.OutForDelivery => 6, OrderStatus.Delivered => 7,
        _ => 99,
    };

    /// <summary>Same side effects as the admin status change: history, timestamps, COD paid on delivery, invoice sync, email.</summary>
    private void ChangeOrderStatus(Order order, OrderStatus status, string note, string by, bool email)
    {
        order.Status = status;
        if (status == OrderStatus.Dispatched) order.DispatchedAt ??= DateTime.UtcNow;
        if (status == OrderStatus.Delivered) order.DeliveredAt = DateTime.UtcNow;
        if (status == OrderStatus.Delivered && order.PaymentMethod == PaymentMethod.CashOnDelivery && order.PaymentStatus == PaymentStatus.Pending)
            order.PaymentStatus = PaymentStatus.Paid;
        order.UpdatedAt = DateTime.UtcNow; order.UpdatedBy = by;
        db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, Status = status, Note = note, UpdatedBy = by });
        _pendingSideEffects.Add((order.Id, status, email && EmailAddressRules.IsDeliverable(order.User?.Email)));
    }

    private readonly List<(int orderId, OrderStatus status, bool email)> _pendingSideEffects = [];

    /// <summary>Run after SaveChanges: invoice sync + queued customer emails for status changes made above.</summary>
    public async Task FlushSideEffectsAsync(string by)
    {
        var work = _pendingSideEffects.ToList();
        _pendingSideEffects.Clear();
        foreach (var (orderId, status, email) in work)
        {
            try { await invoices.SyncWithOrderAsync(orderId, by); }
            catch (Exception ex) { logger.LogWarning(ex, "Invoice sync failed for order {OrderId}", orderId); }
            if (email && status is OrderStatus.Dispatched or OrderStatus.OutForDelivery or OrderStatus.Delivered or OrderStatus.InTransit)
                orderEmails.StatusChanged(orderId, status);
        }
    }

    // ───────────────────────── helpers ─────────────────────────

    public async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
        => await db.Orders.Include(o => o.User).Include(o => o.Address).Include(o => o.Items)
               .FirstOrDefaultAsync(o => o.Id == orderId, ct)
           ?? throw new ShippingProviderException("Order not found.");

    public static string? TrackingUrl(ShippingProviderAccount account, string? awb)
    {
        if (string.IsNullOrWhiteSpace(awb) || string.IsNullOrWhiteSpace(account.TrackingUrlTemplate)) return null;
        var t = account.TrackingUrlTemplate.Trim();
        if (!t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return null;
        return t.Replace("{awb}", Uri.EscapeDataString(awb.Trim()), StringComparison.OrdinalIgnoreCase);
    }

    private void AddEvent(Shipment s, DateTime at, string status, string? activity, string? location, string source)
    {
        var e = new ShipmentEvent { OccurredAt = at, Status = Short(status, 80), Activity = activity == null ? null : Short(activity, 400),
            Location = location == null ? null : Short(location, 200), Source = source };
        s.Events.Add(e);
    }

    private static string Short(string s, int n) => s.Length <= n ? s : s[..n];
    private static decimal? Positive(decimal? d) => d is > 0 ? d : null;
}
