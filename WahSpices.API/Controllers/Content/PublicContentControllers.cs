using WahSpices.API.Security;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Controllers.Content;

/// <summary>Public website content: every public block + the public SiteSettings subset + testimonials.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/site-content")]
[Produces("application/json")]
public class SiteContentController : ControllerBase
{
    private readonly AppDbContext _db;
    public SiteContentController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var settings = await SiteContentLogic.GetSettingsAsync(_db);
        var rows = await _db.SiteContentBlocks.AsNoTracking().Where(b => b.IsPublic && !b.IsDeleted).ToListAsync();

        var blocks = new JsonObject();
        foreach (var r in rows)
        {
            var node = SiteContentLogic.Parse(r.Json);
            if (node != null) blocks[r.Key] = node;
        }

        var featured = await FeedbackController.FeaturedTestimonials(_db);
        Response.Headers.CacheControl = "no-cache";
        return Ok(ApiResponse<object>.Ok(new
        {
            settings = SiteContentLogic.PublicSettings(settings),
            blocks,
            testimonials = featured,   // approved + featured feedback (empty → the site uses blocks.testimonials)
        }));
    }
}

public record ContactRequest(string? Name, string? Email, string? Phone, string? Subject, string? Message, string? Website);

[ApiController]
[AllowAnonymous]
[Route("api/contact")]
[Produces("application/json")]
public class ContactController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmailComposer _emails;
    private readonly ILogger<ContactController> _logger;
    public ContactController(AppDbContext db, IEmailComposer emails, ILogger<ContactController> logger) { _db = db; _emails = emails; _logger = logger; }

    [HttpPost]
    [RequireHumanProof("contact")]
    public async Task<IActionResult> Create([FromBody] ContactRequest req)
    {
        var name = req.Name?.Trim() ?? "";
        var email = req.Email?.Trim() ?? "";
        var phone = req.Phone?.Trim();
        var subject = req.Subject?.Trim() ?? "";
        var message = req.Message?.Trim() ?? "";

        if (name.Length < 2 || name.Length > 100) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Please enter your name (2–100 characters).", "name"));
        if (!SiteContentLogic.EmailRegex.IsMatch(email) || email.Length > 150) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Please enter a valid email address.", "email"));
        if (!string.IsNullOrEmpty(phone) && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9 \-]{7,15}$"))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Please enter a valid phone number.", "phone"));
        if (subject.Length == 0 || subject.Length > 150) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Please select a subject.", "subject"));
        if (message.Length < 10 || message.Length > 4000) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Message must be between 10 and 4000 characters.", "message"));

        var ticket = $"WAH-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(1000, 9999)}";

        // Honeypot: bots fill the hidden "website" field — pretend success, store nothing.
        if (!string.IsNullOrWhiteSpace(req.Website))
            return Ok(ApiResponse<object>.Ok(new { ticketId = ticket }, "Message received"));

        if (!PublicThrottle.Allow("contact", HttpContext.Connection.RemoteIpAddress?.ToString(), 5, TimeSpan.FromMinutes(10)))
            return StatusCode(429, ApiResponse<object>.Fail("TOO_MANY_REQUESTS", "You've sent several messages in a short time. Please wait a few minutes and try again."));

        var msg = new ContactMessage
        {
            Name = name, Email = email, Phone = string.IsNullOrEmpty(phone) ? null : phone,
            Subject = subject, Message = message, TicketId = ticket, Status = "New",
            UserId = PublicUser.CustomerId(User), CreatedBy = "website",
        };
        _db.ContactMessages.Add(msg);
        await _db.SaveChangesAsync();

        // Notify the store inbox (SiteSettings.Email) and acknowledge to the customer — both queued, so email can never
        // slow down or fail the customer's request.
        var s = await SiteContentLogic.GetSettingsAsync(_db);
        var storeInbox = string.IsNullOrWhiteSpace(s.Email) ? SiteContentLogic.DefaultEmail : s.Email;
        await _emails.QueueAsync(storeInbox, s.SiteName, "contact-store",
            b => EmailTemplates.ContactStoreNotification(b, ticket, name, email, phone, subject, message));
        await _emails.QueueAsync(email, name, "contact-ack",
            b => EmailTemplates.ContactAcknowledgement(b, ticket, name, subject, message));

        return Ok(ApiResponse<object>.Ok(new { ticketId = ticket, id = msg.Id }, "Message received"));
    }
}

public record FeedbackRequest(string? Name, string? Email, string? Product, int Rating, string? Message, string? Recommend, string? Website);

[ApiController]
[AllowAnonymous]
[Route("api/feedback")]
[Produces("application/json")]
public class FeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    public FeedbackController(AppDbContext db) => _db = db;

    [HttpPost]
    [RequireHumanProof("feedback")]
    public async Task<IActionResult> Create([FromBody] FeedbackRequest req)
    {
        var name = req.Name?.Trim();
        var email = req.Email?.Trim();
        var message = req.Message?.Trim() ?? "";
        if (req.Rating < 1 || req.Rating > 5) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Please select a star rating (1–5).", "rating"));
        if (message.Length < 10 || message.Length > 2000) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Feedback must be between 10 and 2000 characters.", "message"));
        if (!string.IsNullOrEmpty(email) && (!SiteContentLogic.EmailRegex.IsMatch(email) || email.Length > 150))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Please enter a valid email address.", "email"));
        if (name?.Length > 100) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Name is too long (max 100).", "name"));
        if (req.Product?.Length > 150 || req.Recommend?.Length > 100) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Invalid product or recommendation value."));

        if (!string.IsNullOrWhiteSpace(req.Website)) return Ok(ApiResponse<object>.Ok(new { id = 0 }, "Thank you for your feedback"));
        if (!PublicThrottle.Allow("feedback", HttpContext.Connection.RemoteIpAddress?.ToString(), 5, TimeSpan.FromMinutes(10)))
            return StatusCode(429, ApiResponse<object>.Fail("TOO_MANY_REQUESTS", "You've sent feedback several times in a short time. Please wait a few minutes and try again."));

        var userId = PublicUser.CustomerId(User);
        if (userId != null && (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email)))
        {
            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
            if (u != null) { name = string.IsNullOrEmpty(name) ? u.Name : name; email = string.IsNullOrEmpty(email) ? u.Email : email; }
        }

        var fb = new Feedback
        {
            Name = string.IsNullOrEmpty(name) ? null : name, Email = string.IsNullOrEmpty(email) ? null : email,
            Product = string.IsNullOrWhiteSpace(req.Product) ? null : req.Product.Trim(), Rating = req.Rating, Message = message,
            Recommend = string.IsNullOrWhiteSpace(req.Recommend) ? null : req.Recommend.Trim(),
            Status = "Pending", UserId = userId, CreatedBy = "website",
        };
        _db.Feedbacks.Add(fb);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id = fb.Id }, "Thank you for your feedback"));
    }

    /// <summary>Approved + featured feedback, newest first (max 6). Empty list → site shows its default testimonials.</summary>
    [HttpGet("testimonials")]
    public async Task<IActionResult> Testimonials() => Ok(ApiResponse<object>.Ok(await FeaturedTestimonials(_db)));

    /// <summary>
    /// "What Our Customers Say": real customer voices only, newest first, max 6.
    /// 1) feedback the admin featured, 2) product reviews the admin featured, then 3) topped up with the latest
    /// approved 4–5★ product reviews that have a written comment. Empty → the site falls back to blocks.testimonials.
    /// Names are shortened to "First L." for privacy.
    /// </summary>
    internal static async Task<List<object>> FeaturedTestimonials(AppDbContext db)
    {
        const int Max = 6;
        var list = new List<(string Key, DateTime At, string Name, string City, int Rating, string Text, string Product)>();

        var fb = await db.Feedbacks.AsNoTracking()
            .Where(f => !f.IsDeleted && f.IsFeatured && f.Status == "Approved")
            .OrderByDescending(f => f.CreatedAt).Take(Max)
            .Select(f => new { f.Id, f.Name, f.Rating, f.Message, f.Product, f.CreatedAt })
            .ToListAsync();
        list.AddRange(fb.Select(f => ($"f{f.Id}", f.CreatedAt, ShortName(f.Name), "Verified Customer", f.Rating, f.Message, f.Product ?? "")));

        var reviewQuery = db.Reviews.AsNoTracking()
            .Where(r => !r.IsDeleted && r.Status == "Approved" && r.Comment != null && r.Comment != "" && !r.Product.IsDeleted);
        var featuredReviews = await reviewQuery.Where(r => r.IsFeatured)
            .OrderByDescending(r => r.CreatedAt).Take(Max)
            .Select(r => new { r.Id, r.UserId, Name = r.User.Name, r.Rating, r.Title, r.Comment, Product = r.Product.Name, r.IsVerifiedPurchase, r.CreatedAt })
            .ToListAsync();
        var autoReviews = list.Count + featuredReviews.Count >= Max ? new() : await reviewQuery
            .Where(r => !r.IsFeatured && r.Rating >= 4 && r.Comment!.Length >= 20)
            .OrderByDescending(r => r.CreatedAt).Take(Max)
            .Select(r => new { r.Id, r.UserId, Name = r.User.Name, r.Rating, r.Title, r.Comment, Product = r.Product.Name, r.IsVerifiedPurchase, r.CreatedAt })
            .ToListAsync();

        var reviews = featuredReviews.Concat(autoReviews).ToList();
        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var cities = userIds.Count == 0 ? new Dictionary<int, string>() : (await db.Addresses.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId) && !a.IsDeleted && a.City != "")
                .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
                .Select(a => new { a.UserId, a.City }).ToListAsync())
            .GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.First().City);

        foreach (var r in reviews)
        {
            var city = cities.TryGetValue(r.UserId, out var c) && !string.IsNullOrWhiteSpace(c) ? c.Trim()
                     : r.IsVerifiedPurchase ? "Verified Buyer" : "Customer";
            list.Add(($"r{r.Id}", r.CreatedAt, ShortName(r.Name), city, r.Rating, r.Comment!, r.Product));
        }

        return list.Take(Max).Select(t => (object)new
        {
            id = t.Key,
            name = t.Name,
            city = t.City,
            rating = Math.Clamp(t.Rating, 1, 5),
            text = t.Text.Length > 280 ? t.Text[..277].TrimEnd() + "…" : t.Text,
            product = t.Product,
        }).ToList();
    }

    private static string ShortName(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[0].Contains('@') || parts[0].All(char.IsDigit)) return "Happy Customer";
        return parts.Length == 1 ? parts[0] : $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }
}

internal static class PublicUser
{
    /// <summary>Website customer id from a customer JWT (admin tokens are ignored).</summary>
    public static int? CustomerId(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        if (!user.IsInRole("Customer") || user.FindFirst("admin_id") != null) return null;
        return int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
