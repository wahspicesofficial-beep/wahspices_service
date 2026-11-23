using WahSpices.API.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Enums;
using WahSpices.Infrastructure.Data;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Admin → Website Users: customers registered on the website (Users table, role Customer).
/// "Blocked" = disabled by an admin (IsActive = false + IsLocked = true with no LockedUntil); the login API then
/// answers "Your account has been disabled" and order placement is refused.
/// </summary>
[Route("api/admin/website-users")]
[RequireModule("WebsiteUsers")]
public class AdminWebsiteUsersController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminWebsiteUsersController(AppDbContext db) => _db = db;

    private static readonly PaymentStatus[] PaidStatuses = { PaymentStatus.Paid, PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded };

    private static string StatusOf(bool isActive, bool isLocked, DateTime? lockedUntil, bool verified) =>
        !isActive && isLocked && lockedUntil == null ? "Blocked"
        : !isActive && !verified ? "Unverified"
        : !isActive ? "Disabled"
        : "Active";

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);
        var users = _db.Users.AsNoTracking().Where(u => !u.IsDeleted && u.Role == UserRole.Customer);

        // KPI tiles (whole customer base, not the filtered page)
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var validOrders = _db.Orders.Where(o => !o.IsDeleted && o.Status != OrderStatus.Cancelled);
        var summary = new
        {
            TotalUsers = await users.CountAsync(),
            NewThisMonth = await users.CountAsync(u => u.CreatedAt >= monthStart),
            ActiveBuyers = await validOrders.Select(o => o.UserId).Distinct().CountAsync(u => users.Any(x => x.Id == u)),
            BuyersLast90Days = await validOrders.Where(o => o.CreatedAt >= DateTime.UtcNow.AddDays(-90)).Select(o => o.UserId).Distinct().CountAsync(),
            Blocked = await users.CountAsync(u => !u.IsActive && u.IsLocked && u.LockedUntil == null),
            Unverified = await users.CountAsync(u => !u.IsEmailVerified),
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            users = users.Where(u => u.Name.Contains(s) || u.Email.Contains(s) || u.Phone.Contains(s));
        }
        switch ((status ?? "").ToLowerInvariant())
        {
            case "blocked": users = users.Where(u => !u.IsActive && u.IsLocked && u.LockedUntil == null); break;
            case "active": users = users.Where(u => u.IsActive); break;
            case "unverified": users = users.Where(u => !u.IsEmailVerified); break;
            case "buyers": users = users.Where(u => validOrders.Any(o => o.UserId == u.Id)); break;
            case "new": users = users.Where(u => u.CreatedAt >= monthStart); break;
        }

        var projected = users.Select(u => new
        {
            u.Id, u.Name, u.Email, u.Phone, u.CreatedAt, u.LastLoginAt, u.IsActive, u.IsLocked, u.LockedUntil,
            u.IsEmailVerified, u.IsPhoneVerified, u.LoyaltyPoints,
            Orders = validOrders.Count(o => o.UserId == u.Id),
            Spent = validOrders.Where(o => o.UserId == u.Id && PaidStatuses.Contains(o.PaymentStatus)).Sum(o => (decimal?)o.GrandTotal) ?? 0,
            LastOrderAt = validOrders.Where(o => o.UserId == u.Id).Max(o => (DateTime?)o.CreatedAt),
            Returns = _db.ReturnOrders.Count(r => r.UserId == u.Id && !r.IsDeleted && r.Status != ReturnStatus.Cancelled),
        });
        projected = (sort ?? "").ToLowerInvariant() switch
        {
            "spent" => projected.OrderByDescending(x => x.Spent),
            "orders" => projected.OrderByDescending(x => x.Orders),
            "lastorder" => projected.OrderByDescending(x => x.LastOrderAt),
            "name" => projected.OrderBy(x => x.Name),
            _ => projected.OrderByDescending(x => x.CreatedAt),
        };

        var total = await projected.CountAsync();
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync();
        var items = rows.Select(u => new
        {
            u.Id, u.Name, u.Email, u.Phone, u.CreatedAt, u.LastLoginAt, u.IsEmailVerified, u.IsPhoneVerified, u.LoyaltyPoints,
            Status = StatusOf(u.IsActive, u.IsLocked, u.LockedUntil, u.IsEmailVerified),
            OrderCount = u.Orders, TotalSpent = u.Spent, u.LastOrderAt, ReturnCount = u.Returns,
        });
        return Ok(ApiResponse<object>.Ok(new { Items = items, Summary = summary, Pagination = new { total, page, limit } }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && x.Role == UserRole.Customer);
        if (u == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Website user not found"));

        var addresses = await _db.Addresses.AsNoTracking().Where(a => a.UserId == id && !a.IsDeleted)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Name, a.Phone, a.AddressLine, a.Landmark, a.City, a.State, a.Pincode, Type = a.Type.ToString(), a.IsDefault })
            .ToListAsync();
        var orders = await _db.Orders.AsNoTracking().Where(o => o.UserId == id && !o.IsDeleted)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.CreatedAt, o.GrandTotal,
                Status = o.Status.ToString(), PaymentStatus = o.PaymentStatus.ToString(), PaymentMethod = o.PaymentMethod.ToString(),
                ItemCount = o.Items.Count(i => !i.IsDeleted), Units = o.Items.Where(i => !i.IsDeleted).Sum(i => i.Quantity),
                Items = o.Items.Where(i => !i.IsDeleted).Select(i => i.ProductName + " " + i.Weight + " ×" + i.Quantity).ToList(),
            }).ToListAsync();
        var returns = await _db.ReturnOrders.AsNoTracking().Where(r => r.UserId == id && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.ReturnNumber, OrderNumber = r.Order.OrderNumber, Status = r.Status.ToString(), r.Reason,
                RefundAmount = r.GrandRefundAmount, r.RefundCompletedAt, r.CreatedAt,
            }).ToListAsync();

        var valid = orders.Where(o => o.Status != nameof(OrderStatus.Cancelled)).ToList();
        var paid = new[] { nameof(PaymentStatus.Paid), nameof(PaymentStatus.PartiallyRefunded), nameof(PaymentStatus.Refunded) };
        var refunded = returns.Where(r => r.Status == nameof(ReturnStatus.Refunded)).Sum(r => r.RefundAmount)
                       + await _db.Refunds.Where(r => r.Order.UserId == id && !r.IsDeleted && r.ReturnOrderId == null
                                                      && r.Status != RefundStatus.Failed && r.Status != RefundStatus.Cancelled).SumAsync(r => (decimal?)r.Amount) ?? 0;
        var spent = valid.Where(o => paid.Contains(o.PaymentStatus)).Sum(o => o.GrandTotal);

        return Ok(ApiResponse<object>.Ok(new
        {
            u.Id, u.Name, u.Email, u.Phone, u.AvatarUrl, u.CreatedAt, u.LastLoginAt, u.LoyaltyPoints,
            u.IsEmailVerified, u.IsPhoneVerified, LoginMethod = u.GoogleId != null ? "Google" : "Email",
            Status = StatusOf(u.IsActive, u.IsLocked, u.LockedUntil, u.IsEmailVerified),
            Stats = new
            {
                Orders = valid.Count,
                CancelledOrders = orders.Count - valid.Count,
                TotalSpent = spent,
                Refunded = refunded,
                NetSpent = spent - refunded,
                AverageOrder = valid.Count == 0 ? 0 : Math.Round(valid.Average(o => o.GrandTotal), 2),
                PendingPayment = valid.Where(o => o.PaymentStatus is nameof(PaymentStatus.Pending) or nameof(PaymentStatus.Initiated)).Sum(o => o.GrandTotal),
                Returns = returns.Count(r => r.Status != nameof(ReturnStatus.Cancelled)),
                FirstOrderAt = orders.LastOrDefault()?.CreatedAt,
                LastOrderAt = orders.FirstOrDefault()?.CreatedAt,
            },
            Addresses = addresses,
            Orders = orders,
            Returns = returns,
        }));
    }

    public record BlockRequest(bool Blocked, string? Reason);

    /// <summary>Block / unblock a website user. Blocking also signs them out (refresh token cleared).</summary>
    [HttpPut("{id:int}/block")]
    public async Task<IActionResult> Block(int id, [FromBody] BlockRequest req)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && x.Role == UserRole.Customer);
        if (u == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Website user not found"));
        var blocked = !u.IsActive && u.IsLocked && u.LockedUntil == null;
        if (req.Blocked == blocked)
            return BadRequest(ApiResponse<object>.Fail("NO_CHANGE", blocked ? "This user is already blocked." : "This user is not blocked."));

        if (req.Blocked)
        {
            u.IsActive = false;
            u.IsLocked = true;
            u.LockedUntil = null;
            u.RefreshToken = null;
            u.RefreshTokenExpiry = null;
        }
        else
        {
            u.IsActive = true;
            u.IsLocked = false;
            u.FailedLoginAttempts = 0;
        }
        u.UpdatedAt = DateTime.UtcNow;
        u.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { u.Id, Status = StatusOf(u.IsActive, u.IsLocked, u.LockedUntil, u.IsEmailVerified) },
            req.Blocked ? $"{u.Name} has been blocked" : $"{u.Name} has been unblocked"));
    }
}
