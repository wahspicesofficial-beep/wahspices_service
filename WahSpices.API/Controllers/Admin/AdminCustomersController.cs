using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Common;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/customers")]
[RequireModule("Customers", "WebsiteUsers")]
public class AdminCustomersController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminCustomersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var q = _db.Users.Where(u => !u.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => u.Name.Contains(search) || u.Email.Contains(search) || (u.Phone != null && u.Phone.Contains(search)));
        var total = await q.CountAsync();
        var users = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        var ids = users.Select(u => u.Id).ToList();
        var orderCounts = await _db.Orders.Where(o => ids.Contains(o.UserId)).GroupBy(o => o.UserId).Select(g => new { UserId = g.Key, Count = g.Count(), Total = g.Sum(o => o.GrandTotal) }).ToListAsync();
        var result = users.Select(u => {
            var stats = orderCounts.FirstOrDefault(o => o.UserId == u.Id);
            return new { u.Id, u.Name, u.Email, u.Phone, u.CreatedAt, OrderCount = stats?.Count ?? 0, TotalSpent = stats?.Total ?? 0 };
        });
        return Ok(ApiResponse<object>.Ok(new { customers = result, pagination = new { total, page, limit } }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null || u.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
        var orders = await _db.Orders.Where(o => o.UserId == id).OrderByDescending(o => o.CreatedAt).Take(10).Select(o => new { o.Id, o.OrderNumber, o.GrandTotal, Status = o.Status.ToString(), o.CreatedAt }).ToListAsync();
        // Project — never serialize the tracked Address entities: EF fix-up would attach the User (PasswordHash, RefreshToken…)
        var addresses = await _db.Addresses.AsNoTracking().Where(a => a.UserId == id && !a.IsDeleted)
            .Select(a => new { a.Id, a.Name, a.Phone, a.AddressLine, a.City, a.State, a.Pincode, a.Landmark, Type = a.Type.ToString(), a.IsDefault })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { u.Id, u.Name, u.Email, u.Phone, u.CreatedAt, Orders = orders, Addresses = addresses }));
    }

    [HttpPut("{id:int}/block")]
    public async Task<IActionResult> Block(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
        u.IsDeleted = true; u.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Customer blocked."));
    }
}
