using WahSpices.API.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.API.Services;
using WahSpices.Application.Common;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Controllers.Admin;

public record SiteContentSaveRequest(JsonElement Data, bool? IsPublic);

/// <summary>Admin editor for website content blocks (header, footer, contact, help, about, policies …).</summary>
[Route("api/admin/site-content")]
[RequireModule("Website")]
public class AdminSiteContentController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminSiteContentController(AppDbContext db) => _db = db;

    private static object Map(SiteContentBlock b) => new
    {
        key = b.Key,
        data = SiteContentLogic.Parse(b.Json),
        isPublic = b.IsPublic,
        updatedAt = b.UpdatedAt ?? b.CreatedAt,
        updatedBy = b.UpdatedBy ?? b.CreatedBy,
        hasDefault = SiteContentDefaults.Blocks.ContainsKey(b.Key),
    };

    /// <summary>All blocks (incl. non-public) + public settings, so the editor can show placeholders' current values.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rows = await _db.SiteContentBlocks.AsNoTracking().Where(b => !b.IsDeleted && b.Key != AdminSecuritySettingsService.BlockKey).OrderBy(b => b.Key).ToListAsync();
        var settings = await SiteContentLogic.GetSettingsAsync(_db);
        return Ok(ApiResponse<object>.Ok(new
        {
            blocks = rows.Select(Map),
            settings = SiteContentLogic.PublicSettings(settings),
        }));
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        if (AdminSecuritySettingsService.IsReservedKey(key)) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Content section '{key}' not found"));
        var b = await _db.SiteContentBlocks.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key && !x.IsDeleted);
        if (b == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Content section '{key}' not found"));
        return Ok(ApiResponse<object>.Ok(Map(b)));
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Save(string key, [FromBody] SiteContentSaveRequest req)
    {
        key = key.Trim().ToLowerInvariant();
        if (AdminSecuritySettingsService.IsReservedKey(key)) return BadRequest(ApiResponse<object>.Fail("RESERVED_KEY", "This section is managed in Settings → Security."));
        if (!SiteContentLogic.KeyRegex.IsMatch(key))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Section key must be lowercase letters, digits or dashes (max 64)."));
        if (req.Data.ValueKind != JsonValueKind.Object)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Section data must be a JSON object."));

        var json = req.Data.GetRawText();
        if (json.Length > 200_000)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Section content is too large (max 200 KB)."));
        var linkError = SiteContentLogic.ValidateLinks(JsonNode.Parse(json));
        if (linkError != null) return BadRequest(ApiResponse<object>.Fail("INVALID_URL", linkError));

        var b = await _db.SiteContentBlocks.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Key == key);
        if (b == null)
        {
            b = new SiteContentBlock { Key = key, CreatedBy = AdminName };
            _db.SiteContentBlocks.Add(b);
        }
        b.Json = json;
        b.IsDeleted = false;
        if (req.IsPublic.HasValue) b.IsPublic = req.IsPublic.Value;
        b.UpdatedAt = DateTime.UtcNow;
        b.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(b), "Saved"));
    }

    /// <summary>Restore a section to the text the website shipped with.</summary>
    [HttpPost("{key}/reset")]
    public async Task<IActionResult> Reset(string key)
    {
        if (!SiteContentDefaults.Blocks.TryGetValue(key, out var json))
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"No default content for '{key}'"));
        var b = await _db.SiteContentBlocks.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Key == key);
        if (b == null) { b = new SiteContentBlock { Key = key, CreatedBy = AdminName }; _db.SiteContentBlocks.Add(b); }
        b.Json = json.Trim();
        b.IsDeleted = false;
        b.IsPublic = true;
        b.UpdatedAt = DateTime.UtcNow;
        b.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(b), "Restored to default"));
    }
}

public record StatusRequest(string? Status);
public record ReplyRequest(string? Message, bool? MarkResolved, string? Subject = null);

/// <summary>Website contact-form messages: list / detail / status / reply (email) / delete.</summary>
[Route("api/admin/contact-messages")]
[RequireModule("Messages")]
public class AdminContactMessagesController : AdminBaseController
{
    private static readonly string[] Statuses = { "New", "InProgress", "Resolved", "Spam" };
    private readonly AppDbContext _db;
    private readonly IEmailComposer _email;
    private readonly ILogger<AdminContactMessagesController> _logger;
    public AdminContactMessagesController(AppDbContext db, IEmailComposer email, ILogger<AdminContactMessagesController> logger)
    { _db = db; _email = email; _logger = logger; }

    private static object Map(ContactMessage m) => new
    {
        id = m.Id, ticketId = m.TicketId, name = m.Name, email = m.Email, phone = m.Phone,
        subject = m.Subject, message = m.Message, status = m.Status, isResolved = m.IsResolved,
        adminReply = m.AdminReply, repliedAt = m.RepliedAt, repliedBy = m.RepliedBy, userId = m.UserId,
        createdAt = m.CreatedAt, updatedAt = m.UpdatedAt,
        defaultReplySubject = EmailTemplates.ContactReplySubject(m.Subject, m.TicketId),
    };

    private async Task<object> MapWithReplies(ContactMessage m)
    {
        var replies = await _db.ContactMessageReplies.AsNoTracking()
            .Where(r => r.ContactMessageId == m.Id).OrderBy(r => r.CreatedAt)
            .Select(r => new { id = r.Id, toEmail = r.ToEmail, subject = r.Subject, body = r.Body, sentBy = r.SentBy, sentAt = r.CreatedAt })
            .ToListAsync();
        // Replies sent before reply history existed only live in AdminReply — show that one so nothing disappears.
        if (replies.Count == 0 && !string.IsNullOrWhiteSpace(m.AdminReply))
            replies.Add(new { id = 0, toEmail = m.Email, subject = EmailTemplates.ContactReplySubject(m.Subject, m.TicketId), body = m.AdminReply!, sentBy = m.RepliedBy, sentAt = m.RepliedAt ?? m.UpdatedAt ?? m.CreatedAt });
        var dict = System.Text.Json.JsonSerializer.SerializeToNode(Map(m))!.AsObject();
        dict["replies"] = System.Text.Json.JsonSerializer.SerializeToNode(replies);
        return dict;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 200);
        var q = _db.ContactMessages.AsNoTracking().Where(m => !m.IsDeleted);
        var counts = await q.GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase)) q = q.Where(m => m.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(m => m.Name.Contains(s) || m.Email.Contains(s) || m.Subject.Contains(s) || m.Message.Contains(s)
                             || (m.TicketId != null && m.TicketId.Contains(s)) || (m.Phone != null && m.Phone.Contains(s)));
        }
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            items = items.Select(Map), total, page, limit, totalPages = (int)Math.Ceiling(total / (double)limit),
            counts = Statuses.ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0),
            statuses = Statuses,
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var m = await _db.ContactMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return m == null ? NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Message not found")) : Ok(ApiResponse<object>.Ok(await MapWithReplies(m)));
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusRequest req)
    {
        var status = Statuses.FirstOrDefault(s => s.Equals(req.Status?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (status == null) return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Status must be one of: {string.Join(", ", Statuses)}"));
        var m = await _db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (m == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Message not found"));
        m.Status = status;
        m.IsResolved = status == "Resolved";
        m.UpdatedAt = DateTime.UtcNow; m.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(await MapWithReplies(m), $"Marked {status}"));
    }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyRequest req)
    {
        var text = req.Message?.Trim() ?? "";
        var subjectIn = System.Text.RegularExpressions.Regex.Replace(req.Subject ?? "", @"[\r\n]+", " ").Trim();
        if (text.Length < 2 || text.Length > 5000) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Reply must be between 2 and 5000 characters.", "message"));
        if (subjectIn.Length > 200) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Subject can be at most 200 characters.", "subject"));
        var m = await _db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (m == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Message not found"));
        var subject = subjectIn.Length > 0 ? subjectIn : EmailTemplates.ContactReplySubject(m.Subject, m.TicketId);

        try
        {
            await _email.SendNowAsync(m.Email, m.Name, "contact-reply",
                b => EmailTemplates.ContactReply(b, m.Name, m.TicketId, m.Subject, m.Message, text, subject));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reply email failed for contact message {Id}", id);
            return StatusCode(502, ApiResponse<object>.Fail("EMAIL_FAILED", $"Could not send the email to {m.Email} — the mail server rejected it or could not be reached (details are in the server log)."));
        }

        _db.ContactMessageReplies.Add(new ContactMessageReply
        {
            ContactMessageId = m.Id, ToEmail = m.Email, Subject = subject, Body = text, SentBy = AdminName, CreatedBy = AdminName,
        });
        m.AdminReply = text; m.RepliedAt = DateTime.UtcNow; m.RepliedBy = AdminName;
        if (req.MarkResolved != false) { m.Status = "Resolved"; m.IsResolved = true; }
        else if (m.Status == "New") m.Status = "InProgress";
        m.UpdatedAt = DateTime.UtcNow; m.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(await MapWithReplies(m), $"Reply emailed to {m.Email}"));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var m = await _db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (m == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Message not found"));
        _db.ContactMessages.Remove(m);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id }, "Message deleted"));
    }
}

public record FeatureRequest(bool Featured);

/// <summary>Customer feedback: moderation (approve/reject), featuring as testimonial, reply (email), delete, KPIs.</summary>
[Route("api/admin/feedback")]
[RequireModule("Feedback")]
public class AdminFeedbackController : AdminBaseController
{
    private static readonly string[] Statuses = { "Pending", "Approved", "Rejected" };
    private readonly AppDbContext _db;
    private readonly IEmailComposer _email;
    private readonly ILogger<AdminFeedbackController> _logger;
    public AdminFeedbackController(AppDbContext db, IEmailComposer email, ILogger<AdminFeedbackController> logger)
    { _db = db; _email = email; _logger = logger; }

    private static object Map(Feedback f) => new
    {
        id = f.Id, name = f.Name, email = f.Email, product = f.Product, rating = f.Rating, message = f.Message,
        recommend = f.Recommend, status = f.Status, isFeatured = f.IsFeatured, adminReply = f.AdminReply,
        repliedAt = f.RepliedAt, repliedBy = f.RepliedBy, userId = f.UserId, createdAt = f.CreatedAt, updatedAt = f.UpdatedAt,
    };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? rating, [FromQuery] bool? featured,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        page = Math.Max(1, page); limit = Math.Clamp(limit, 1, 200);
        var all = _db.Feedbacks.AsNoTracking().Where(f => !f.IsDeleted);

        var byStatus = await all.GroupBy(f => f.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var byRating = await all.GroupBy(f => f.Rating).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var totalAll = byStatus.Sum(x => x.Count);
        var ratingSum = byRating.Sum(x => x.Key * x.Count);
        var featuredCount = await all.CountAsync(f => f.IsFeatured && f.Status == "Approved");
        var recommendYes = await all.CountAsync(f => f.Recommend != null && f.Recommend.StartsWith("Yes"));

        var q = all;
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase)) q = q.Where(f => f.Status == status);
        if (rating is >= 1 and <= 5) q = q.Where(f => f.Rating == rating);
        if (featured.HasValue) q = q.Where(f => f.IsFeatured == featured.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(f => f.Message.Contains(s) || (f.Name != null && f.Name.Contains(s)) || (f.Email != null && f.Email.Contains(s)) || (f.Product != null && f.Product.Contains(s)));
        }
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(f => f.CreatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items = items.Select(Map), total, page, limit, totalPages = (int)Math.Ceiling(total / (double)limit),
            stats = new
            {
                total = totalAll,
                avgRating = totalAll == 0 ? 0 : Math.Round(ratingSum / (double)totalAll, 2),
                pending = byStatus.FirstOrDefault(x => x.Key == "Pending")?.Count ?? 0,
                approved = byStatus.FirstOrDefault(x => x.Key == "Approved")?.Count ?? 0,
                rejected = byStatus.FirstOrDefault(x => x.Key == "Rejected")?.Count ?? 0,
                featured = featuredCount,
                recommendPct = totalAll == 0 ? 0 : Math.Round(recommendYes * 100.0 / totalAll, 1),
                ratings = Enumerable.Range(1, 5).ToDictionary(r => r.ToString(), r => byRating.FirstOrDefault(x => x.Key == r)?.Count ?? 0),
            },
            statuses = Statuses,
        }));
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusRequest req)
    {
        var status = Statuses.FirstOrDefault(s => s.Equals(req.Status?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (status == null) return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Status must be one of: {string.Join(", ", Statuses)}"));
        var f = await _db.Feedbacks.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (f == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Feedback not found"));
        f.Status = status;
        if (status != "Approved") f.IsFeatured = false;   // only approved feedback can be a testimonial
        f.UpdatedAt = DateTime.UtcNow; f.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(f), $"Feedback {status.ToLowerInvariant()}"));
    }

    [HttpPatch("{id:int}/feature")]
    public async Task<IActionResult> Feature(int id, [FromBody] FeatureRequest req)
    {
        var f = await _db.Feedbacks.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (f == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Feedback not found"));
        if (req.Featured && f.Status != "Approved")
        {
            // Featuring implies approval — the testimonial goes public.
            f.Status = "Approved";
        }
        f.IsFeatured = req.Featured;
        f.UpdatedAt = DateTime.UtcNow; f.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(f), req.Featured ? "Featured as testimonial on the website" : "Removed from testimonials"));
    }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyRequest req)
    {
        var text = req.Message?.Trim() ?? "";
        if (text.Length < 2 || text.Length > 5000) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Reply must be between 2 and 5000 characters.", "message"));
        var f = await _db.Feedbacks.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (f == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Feedback not found"));
        if (string.IsNullOrWhiteSpace(f.Email))
            return BadRequest(ApiResponse<object>.Fail("NO_EMAIL", "This feedback has no email address, so a reply can't be sent."));

        var name = string.IsNullOrWhiteSpace(f.Name) ? "there" : f.Name;
        try
        {
            await _email.SendNowAsync(f.Email, name, "feedback-reply",
                b => EmailTemplates.FeedbackReply(b, name, f.Rating, f.Product, f.Message, text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reply email failed for feedback {Id}", id);
            return StatusCode(502, ApiResponse<object>.Fail("EMAIL_FAILED", $"Could not send the email to {f.Email} — the mail server rejected it or could not be reached (details are in the server log)."));
        }
        f.AdminReply = text; f.RepliedAt = DateTime.UtcNow; f.RepliedBy = AdminName;
        f.UpdatedAt = DateTime.UtcNow; f.UpdatedBy = AdminName;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(f), $"Reply emailed to {f.Email}"));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var f = await _db.Feedbacks.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (f == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Feedback not found"));
        _db.Feedbacks.Remove(f);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { id }, "Feedback deleted"));
    }
}
