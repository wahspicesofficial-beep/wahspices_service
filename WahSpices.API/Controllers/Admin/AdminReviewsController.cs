using WahSpices.API.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Services;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Product review moderation (module "Reviews"). Customers' reviews arrive as Pending; only Approved
/// reviews are public and count toward Product.Rating / ReviewCount, which are recomputed on every change.
/// </summary>
[Route("api/admin/reviews")]
[RequireModule("Reviews")]
public class AdminReviewsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminReviewsController(AppDbContext db) => _db = db;

    private static readonly string[] Statuses = { "Pending", "Approved", "Rejected" };

    /// <summary>List reviews with filters + KPI summary.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status, [FromQuery] int? rating, [FromQuery] int? productId,
        [FromQuery] string? search, [FromQuery] bool? verified, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);
        var q = _db.Reviews.AsNoTracking().Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var st = Statuses.FirstOrDefault(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));
            if (st == null) return BadRequest(ApiResponse<object>.Fail("INVALID_STATUS", $"Status must be one of {string.Join(", ", Statuses)}.", "status"));
            q = q.Where(r => r.Status == st);
        }
        if (rating is >= 1 and <= 5) q = q.Where(r => r.Rating == rating.Value);
        if (productId.HasValue) q = q.Where(r => r.ProductId == productId.Value);
        if (verified.HasValue) q = q.Where(r => r.IsVerifiedPurchase == verified.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => (r.Title != null && r.Title.Contains(s)) || (r.Comment != null && r.Comment.Contains(s))
                || r.User.Name.Contains(s) || r.User.Email.Contains(s) || r.Product.Name.Contains(s));
        }

        q = sort switch
        {
            "oldest"  => q.OrderBy(r => r.CreatedAt),
            "highest" => q.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "lowest"  => q.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _         => q.OrderByDescending(r => r.CreatedAt),
        };

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * limit).Take(limit)
            .Select(r => new
            {
                r.Id, r.ProductId, ProductName = r.Product.Name, ProductSlug = r.Product.Slug, ProductImage = r.Product.ImageUrl,
                r.UserId, UserName = r.User.Name, UserEmail = r.User.Email,
                Rating = (int)r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.HelpfulCount,
                r.Status, r.AdminReply, r.RejectionReason, r.ModeratedAt, r.ModeratedBy, r.CreatedAt, r.UpdatedAt, r.IsFeatured,
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Items = items,
            Pagination = new { total, page, limit, pages = (int)Math.Ceiling(total / (double)limit) },
            Summary = await SummaryAsync(),
        }));
    }

    /// <summary>KPI tiles: counts per status, average approved rating, star distribution, settings.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary() => Ok(ApiResponse<object>.Ok(await SummaryAsync()));

    private async Task<object> SummaryAsync()
    {
        var all = _db.Reviews.AsNoTracking().Where(r => !r.IsDeleted);
        var byStatus = await all.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var approved = all.Where(r => r.Status == "Approved");
        var dist = await approved.GroupBy(r => r.Rating).Select(g => new { Star = (int)g.Key, Count = g.Count() }).ToListAsync();
        var approvedCount = dist.Sum(d => d.Count);
        var avg = approvedCount == 0 ? 0 : Math.Round(dist.Sum(d => d.Star * d.Count) / (double)approvedCount, 2);
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var settings = await ProductReviewStats.GetSettingsAsync(_db);
        int C(string s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        return new
        {
            Total = byStatus.Sum(x => x.Count),
            Pending = C("Pending"), Approved = C("Approved"), Rejected = C("Rejected"),
            AverageRating = avg,
            Verified = await all.CountAsync(r => r.IsVerifiedPurchase),
            NewThisWeek = await all.CountAsync(r => r.CreatedAt >= weekAgo),
            Distribution = Enumerable.Range(1, 5).Reverse().Select(s => new { Star = s, Count = dist.FirstOrDefault(d => d.Star == s)?.Count ?? 0 }),
            settings.AutoApproveVerified,
        };
    }

    /// <summary>Products that have at least one review (for the product filter).</summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetReviewedProducts()
    {
        var rows = await _db.Reviews.AsNoTracking().Where(r => !r.IsDeleted)
            .GroupBy(r => new { r.ProductId, r.Product.Name })
            .Select(g => new { Id = g.Key.ProductId, g.Key.Name, Count = g.Count(), Pending = g.Count(x => x.Status == "Pending") })
            .OrderBy(x => x.Name).ToListAsync();
        return Ok(ApiResponse<object>.Ok(rows));
    }

    public record ModerateRequest(string? Reason);
    public record ReplyRequest(string? Reply);
    public record BulkRequest(List<int>? Ids, string? Action, string? Reason);
    public record SettingsRequest(bool AutoApproveVerified);
    public record FeatureRequest(bool Featured);

    /// <summary>Show / hide an approved review in "What Our Customers Say" on the home page.</summary>
    [HttpPut("{id:int}/feature")]
    public async Task<IActionResult> Feature(int id, [FromBody] FeatureRequest req)
    {
        var r = await _db.Reviews.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Review not found."));
        if (req.Featured && r.Status != "Approved")
            return BadRequest(ApiResponse<object>.Fail("NOT_APPROVED", "Approve the review first — only approved reviews can be shown on the home page."));
        if (req.Featured && string.IsNullOrWhiteSpace(r.Comment) && string.IsNullOrWhiteSpace(r.Title))
            return BadRequest(ApiResponse<object>.Fail("NO_TEXT", "This review has no written comment to show."));
        r.IsFeatured = req.Featured;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { r.Id, r.IsFeatured }, req.Featured ? "Shown on the home page" : "Removed from the home page"));
    }

    [HttpPut("{id:int}/approve")]
    public Task<IActionResult> Approve(int id) => SetStatus(new[] { id }, "Approved", null, single: true);

    [HttpPut("{id:int}/reject")]
    public Task<IActionResult> Reject(int id, [FromBody] ModerateRequest? req) => SetStatus(new[] { id }, "Rejected", req?.Reason, single: true);

    /// <summary>Bulk approve / reject / delete. Body: { ids: [..], action: "approve"|"reject"|"delete", reason? }</summary>
    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] BulkRequest req)
    {
        var ids = (req?.Ids ?? new()).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Select at least one review.", "ids"));
        if (ids.Count > 500) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Too many reviews in one request (max 500).", "ids"));
        return (req!.Action ?? "").ToLowerInvariant() switch
        {
            "approve" => await SetStatus(ids, "Approved", null, single: false),
            "reject"  => await SetStatus(ids, "Rejected", req.Reason, single: false),
            "delete"  => await DeleteMany(ids),
            _ => BadRequest(ApiResponse<object>.Fail("INVALID_ACTION", "Action must be approve, reject or delete.", "action")),
        };
    }

    private async Task<IActionResult> SetStatus(IReadOnlyCollection<int> ids, string status, string? reason, bool single)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (status == "Rejected" && reason == null)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Please give a reason for rejecting.", "reason"));
        if (reason?.Length > 500)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Reason is too long (max 500 characters).", "reason"));

        var reviews = await _db.Reviews.Where(r => ids.Contains(r.Id) && !r.IsDeleted).ToListAsync();
        if (reviews.Count == 0) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", single ? "Review not found." : "No matching reviews found."));

        foreach (var r in reviews)
        {
            r.Status = status;
            r.IsApproved = status == "Approved";
            r.RejectionReason = status == "Rejected" ? reason : null;
            r.ModeratedAt = DateTime.UtcNow;
            r.ModeratedBy = AdminName;
        }
        await _db.SaveChangesAsync();
        await ProductReviewStats.RecomputeAsync(_db, reviews.Select(r => r.ProductId));

        var verb = status == "Approved" ? "approved" : "rejected";
        return Ok(ApiResponse<object>.Ok(new { Updated = reviews.Count, Status = status, Ids = reviews.Select(r => r.Id) },
            single ? $"Review {verb}." : $"{reviews.Count} review(s) {verb}."));
    }

    /// <summary>Public reply shown under the review on the product page. Empty reply removes it.</summary>
    [HttpPut("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyRequest req)
    {
        var r = await _db.Reviews.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (r == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Review not found."));
        var reply = req?.Reply?.Trim();
        if (reply?.Length > 1000) return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Reply is too long (max 1000 characters).", "reply"));
        r.AdminReply = string.IsNullOrEmpty(reply) ? null : reply;
        r.UpdatedAt = DateTime.UtcNow;
        r.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { r.Id, r.AdminReply, RepliedAt = r.UpdatedAt }, r.AdminReply == null ? "Reply removed." : "Reply saved."));
    }

    /// <summary>
    /// Permanently delete a review (hard delete so the customer may write a new one —
    /// the table allows a single review per customer per product).
    /// </summary>
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id) => DeleteMany(new[] { id });

    private async Task<IActionResult> DeleteMany(IReadOnlyCollection<int> ids)
    {
        var reviews = await _db.Reviews.Where(r => ids.Contains(r.Id)).ToListAsync();
        if (reviews.Count == 0) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Review not found."));
        var productIds = reviews.Select(r => r.ProductId).ToList();
        _db.Reviews.RemoveRange(reviews);
        await _db.SaveChangesAsync();
        await ProductReviewStats.RecomputeAsync(_db, productIds);
        return Ok(ApiResponse<object>.Ok(new { Deleted = reviews.Count }, reviews.Count == 1 ? "Review deleted." : $"{reviews.Count} reviews deleted."));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(ApiResponse<object>.Ok(await ProductReviewStats.GetSettingsAsync(_db)));

    /// <summary>Moderation settings (stored in SiteContentBlock "reviewSettings", not public).</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest req)
    {
        var s = new ProductReviewStats.ReviewSettings { AutoApproveVerified = req?.AutoApproveVerified ?? false };
        await ProductReviewStats.SaveSettingsAsync(_db, s, AdminName);
        return Ok(ApiResponse<object>.Ok(s, s.AutoApproveVerified
            ? "Verified-purchase reviews will now be published automatically."
            : "All new reviews will wait for approval."));
    }
}
