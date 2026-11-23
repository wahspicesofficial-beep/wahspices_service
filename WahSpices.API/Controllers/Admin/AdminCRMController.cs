using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WahSpices.Application.Common;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.API.Authorization;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/crm")]
[RequireModule("Customers")]
public class AdminCRMController : AdminBaseController
{
    private readonly AppDbContext _db;
    private readonly WahSpices.Infrastructure.Email.IEmailComposer _email;
    private readonly ILogger<AdminCRMController> _logger;
    private new string AdminName => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirst("admin_name")?.Value ?? "Admin";

    public AdminCRMController(AppDbContext db, WahSpices.Infrastructure.Email.IEmailComposer email, ILogger<AdminCRMController> logger)
    {
        _db = db; _email = email; _logger = logger;
    }

    // Loyalty tiers (single source of truth — the UI reads these from /stats)
    private static readonly (string Tier, int MinPoints)[] Tiers =
        { ("Platinum", 5000), ("Gold", 2000), ("Silver", 500), ("Bronze", 0) };
    private static string TierFor(int points) => Tiers.First(t => points >= t.MinPoints).Tier;

    // Aging buckets (days since dues started)
    private static readonly (string Bucket, string Label, int Min, int? Max)[] Buckets =
        { ("0-30", "0–30 days", 0, 30), ("31-60", "31–60 days", 31, 60), ("61-90", "61–90 days", 61, 90), ("90+", "90+ days", 91, null) };
    private static string BucketFor(int days) => Buckets.First(b => days >= b.Min && (b.Max == null || days <= b.Max)).Bucket;

    private const string DefaultReminderMessage =
        "Dear {customer}, your outstanding amount of ₹{amount} with {company} is due ({days} days). Please make the payment at the earliest.";

    // ── List (server-side search / filter / sort / paging) ───────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? status,
        [FromQuery] bool? hasDues, [FromQuery] string? tier, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        try
        {
            page  = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 200);

            var q = _db.CrmCustomers.Where(c => !c.IsDeleted).AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(c => c.Name.Contains(s) || c.Phone.Contains(s)
                              || (c.Email != null && c.Email.Contains(s))
                              || (c.ContactName != null && c.ContactName.Contains(s))
                              || c.City.Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(category) && category != "All")
                q = q.Where(c => c.Category == category);
            if (status == "Blocked")  q = q.Where(c => c.IsBlocked);
            if (status == "Active")   q = q.Where(c => !c.IsBlocked && c.IsActive);
            if (status == "Inactive") q = q.Where(c => !c.IsActive);
            if (hasDues == true)  q = q.Where(c => c.OutstandingDues > 0);
            if (hasDues == false) q = q.Where(c => c.OutstandingDues <= 0);
            if (!string.IsNullOrWhiteSpace(tier) && tier != "All") q = q.Where(c => c.LoyaltyTier == tier);

            q = (sort ?? "newest").ToLowerInvariant() switch
            {
                "name"   => q.OrderBy(c => c.Name),
                "dues"   => q.OrderByDescending(c => c.OutstandingDues).ThenBy(c => c.Name),
                "points" => q.OrderByDescending(c => c.LoyaltyPoints).ThenBy(c => c.Name),
                "usage"  => q.OrderByDescending(c => c.CreditLimit > 0 ? c.OutstandingDues / c.CreditLimit : 0).ThenByDescending(c => c.OutstandingDues),
                _        => q.OrderByDescending(c => c.CreatedAt),
            };

            var total = await q.CountAsync();
            var list  = await q.Skip((page - 1) * limit).Take(limit).ToListAsync();

            return Ok(ApiResponse<object>.Ok(new {
                customers  = list.Select(MapSummary),
                pagination = new { total, page, limit, pages = Math.Max(1, (int)Math.Ceiling(total / (double)limit)) }
            }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Stats ─────────────────────────────────────────────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var all = await _db.CrmCustomers.Where(c => !c.IsDeleted)
                .Select(c => new { c.Category, c.IsActive, c.IsBlocked, c.OutstandingDues, c.CreditLimit, c.LoyaltyPoints, c.LoyaltyTier })
                .ToListAsync();
            var withLimit = all.Where(c => c.CreditLimit > 0).ToList();
            return Ok(ApiResponse<object>.Ok(new {
                total          = all.Count,
                active         = all.Count(c => c.IsActive && !c.IsBlocked),
                blocked        = all.Count(c => c.IsBlocked),
                inactive       = all.Count(c => !c.IsActive),
                withDues       = all.Count(c => c.OutstandingDues > 0),
                totalDues      = all.Sum(c => c.OutstandingDues),
                totalPoints    = all.Sum(c => c.LoyaltyPoints),
                avgCreditLimit = withLimit.Count > 0 ? Math.Round(withLimit.Average(c => c.CreditLimit), 0) : 0,
                overLimit      = all.Count(c => c.CreditLimit > 0 && c.OutstandingDues > c.CreditLimit),
                categories     = all.GroupBy(c => c.Category).Select(g => new { category = g.Key, count = g.Count() }).OrderByDescending(x => x.count),
                tiers          = Tiers.Select(t => new { tier = t.Tier, minPoints = t.MinPoints, count = all.Count(c => c.LoyaltyTier == t.Tier) }),
            }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Last order date backfill (idempotent; only moves dates forward) ──────
    /// Fills LastOrderDate from existing field-sales orders / bills and invoices (matched by GSTIN, phone, e-mail, name).
    /// New orders keep it current automatically (sales orders, sales bills, invoices).
    [HttpPost("backfill-last-order")]
    public async Task<IActionResult> BackfillLastOrder()
    {
        var updated = await WahSpices.Infrastructure.Services.CrmLastOrder.BackfillAsync(_db);
        return Ok(ApiResponse<object>.Ok(new { updated }, $"Last order date updated for {updated} customer(s)"));
    }

    // ── Aging ─────────────────────────────────────────────────────────────────
    // Contract: { buckets: [{bucket,label,minDays,maxDays,amount,customers}], rows: [{id,name,phone,email,city,category,
    //             outstandingDues,creditLimit,days,bucket,isBlocked}], totalOutstanding, totalCustomers }
    [HttpGet("aging")]
    public async Task<IActionResult> GetAging()
    {
        try
        {
            var now     = DateTime.UtcNow;
            var overdue = await _db.CrmCustomers.Where(c => !c.IsDeleted && c.OutstandingDues > 0).ToListAsync();

            var rows = overdue.Select(c => {
                var days = Math.Max(0, (int)(now - (c.DuesSince ?? c.CreatedAt)).TotalDays);
                return new AgingRow(c.Id, c.Name, c.Phone, c.Email, c.City, c.Category, c.OutstandingDues, c.CreditLimit, days, BucketFor(days), c.IsBlocked);
            }).OrderByDescending(x => x.Days).ThenByDescending(x => x.OutstandingDues).ToList();

            var buckets = Buckets.Select(b => {
                var inB = rows.Where(r => r.Bucket == b.Bucket).ToList();
                return new { bucket = b.Bucket, label = b.Label, minDays = b.Min, maxDays = b.Max, amount = inB.Sum(r => r.OutstandingDues), customers = inB.Count };
            });

            return Ok(ApiResponse<object>.Ok(new {
                buckets,
                rows,
                totalOutstanding = rows.Sum(r => r.OutstandingDues),
                totalCustomers   = rows.Count,
            }));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    public record AgingRow(int Id, string Name, string Phone, string? Email, string City, string Category,
        decimal OutstandingDues, decimal CreditLimit, int Days, string Bucket, bool IsBlocked);

    // ── Single ────────────────────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var c = await _db.CrmCustomers
                .Include(x => x.Payments.Where(p => !p.IsDeleted))
                .Include(x => x.PriceRules.Where(p => !p.IsDeleted))
                .Where(x => !x.IsDeleted && x.Id == id)
                .FirstOrDefaultAsync();
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            return Ok(ApiResponse<object>.Ok(MapFull(c)));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Create ────────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CrmCustomerRequest req)
    {
        try
        {
            var err = await Validate(req);
            if (err != null) return err;
            var c = new CrmCustomer {
                Name = req.Name!.Trim(), ContactName = req.ContactName, Email = Clean(req.Email),
                Phone = req.Phone!.Trim(), City = req.City?.Trim() ?? "", State = req.State,
                Category = string.IsNullOrWhiteSpace(req.Category) ? "Retail" : req.Category, Gstin = Clean(req.Gstin)?.ToUpperInvariant(), Pan = Clean(req.Pan)?.ToUpperInvariant(),
                BillingAddress = req.BillingAddress, ShippingAddress = req.ShippingAddress,
                CreditLimit = req.CreditLimit ?? 0, LoyaltyTier = TierFor(0),
                BirthDate = req.BirthDate, AnniversaryDate = req.AnniversaryDate,
                Notes = req.Notes, Tags = req.Tags,
                IsActive = true, CreatedBy = AdminName
            };
            _db.CrmCustomers.Add(c);
            await _db.SaveChangesAsync();
            return StatusCode(201, ApiResponse<object>.Ok(MapSummary(c), "Customer created"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Update ────────────────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CrmCustomerRequest req)
    {
        try
        {
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            var err = await Validate(req);
            if (err != null) return err;
            c.Name = req.Name!.Trim(); c.ContactName = req.ContactName; c.Email = Clean(req.Email);
            c.Phone = req.Phone!.Trim(); c.City = req.City?.Trim() ?? ""; c.State = req.State;
            c.Category = string.IsNullOrWhiteSpace(req.Category) ? c.Category : req.Category;
            c.Gstin = Clean(req.Gstin)?.ToUpperInvariant(); if (!SensitiveMask.IsEchoOfMask(Clean(req.Pan)?.ToUpperInvariant(), c.Pan, x => SensitiveMask.Tail(x))) c.Pan = Clean(req.Pan)?.ToUpperInvariant();
            c.BillingAddress = req.BillingAddress; c.ShippingAddress = req.ShippingAddress;
            c.CreditLimit = req.CreditLimit ?? c.CreditLimit;
            c.BirthDate = req.BirthDate; c.AnniversaryDate = req.AnniversaryDate;
            c.Notes = req.Notes; c.Tags = req.Tags;
            c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(MapSummary(c), "Customer updated"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var c = await _db.CrmCustomers.FindAsync(id);
            if (c == null || c.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            c.IsDeleted = true; c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true, "Customer deleted"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Block / Unblock ───────────────────────────────────────────────────────
    [HttpPut("{id:int}/block")]
    public async Task<IActionResult> Block(int id, [FromBody] BlockRequest req)
    {
        try
        {
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            c.IsBlocked = req.Block; c.BlockReason = req.Block ? req.Reason : null;
            c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(MapSummary(c), req.Block ? "Customer blocked" : "Customer unblocked"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Credit limit ──────────────────────────────────────────────────────────
    [HttpPut("{id:int}/credit-limit")]
    public async Task<IActionResult> UpdateCreditLimit(int id, [FromBody] CreditLimitRequest req)
    {
        try
        {
            if (req.CreditLimit < 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Credit limit cannot be negative.", "creditLimit"));
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            c.CreditLimit = req.CreditLimit;
            c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(MapSummary(c), "Credit limit updated"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Dues ──────────────────────────────────────────────────────────────────
    [HttpPut("{id:int}/dues")]
    public async Task<IActionResult> UpdateDues(int id, [FromBody] UpdateDuesRequest req)
    {
        try
        {
            if (req.Amount < 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Outstanding amount cannot be negative.", "amount"));
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            c.OutstandingDues = req.Amount;
            c.DuesSince       = req.Amount > 0 && !c.DuesSince.HasValue ? DateTime.UtcNow : (req.Amount == 0 ? null : c.DuesSince);
            c.UpdatedAt       = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(MapSummary(c), "Outstanding updated"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Loyalty ───────────────────────────────────────────────────────────────
    [HttpPut("{id:int}/loyalty")]
    public async Task<IActionResult> UpdateLoyalty(int id, [FromBody] LoyaltyRequest req)
    {
        try
        {
            if (req.Points == 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Enter the points to add (or a negative number to deduct).", "points"));
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            c.LoyaltyPoints = Math.Max(0, c.LoyaltyPoints + req.Points);
            c.LoyaltyTier   = TierFor(c.LoyaltyPoints);
            c.UpdatedAt     = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(MapSummary(c), "Loyalty points updated"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Payments ──────────────────────────────────────────────────────────────
    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> GetPayments(int id)
    {
        try
        {
            var payments = await _db.CrmPayments
                .Where(p => p.CrmCustomerId == id && !p.IsDeleted)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new { p.Id, p.Amount, p.PaymentDate, p.Mode, p.Reference, p.Notes, p.CreatedAt })
                .ToListAsync();
            return Ok(ApiResponse<object>.Ok(payments));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> AddPayment(int id, [FromBody] PaymentRequest req)
    {
        try
        {
            if (req.Amount <= 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Payment amount must be greater than zero.", "amount"));
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            var mode = string.IsNullOrWhiteSpace(req.Mode) ? "Cash" : req.Mode.Trim();
            if (!await IsListValue("PaymentMode", mode))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Unknown payment mode '{mode}'.", "mode"));

            var payment = new CrmPayment {
                CrmCustomerId = id, Amount = req.Amount,
                PaymentDate   = req.PaymentDate ?? DateTime.UtcNow,
                Mode          = mode,
                Reference     = req.Reference, Notes = req.Notes, CreatedBy = AdminName
            };
            _db.CrmPayments.Add(payment);

            c.OutstandingDues = Math.Max(0, c.OutstandingDues - req.Amount);
            if (c.OutstandingDues == 0) c.DuesSince = null;
            c.UpdatedAt = DateTime.UtcNow; c.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();

            return Ok(ApiResponse<object>.Ok(new {
                payment  = new { payment.Id, payment.Amount, payment.PaymentDate, payment.Mode, payment.Reference, payment.Notes },
                newDues  = c.OutstandingDues
            }, "Payment recorded"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Price rules ───────────────────────────────────────────────────────────
    [HttpGet("{id:int}/prices")]
    public async Task<IActionResult> GetPrices(int id)
    {
        try
        {
            var rules = await _db.CrmPriceRules
                .Where(p => p.CrmCustomerId == id && !p.IsDeleted)
                .OrderBy(p => p.ProductName)
                .Select(p => new { p.Id, p.ProductName, p.ProductId, p.CustomPrice, p.DiscountPercent })
                .ToListAsync();
            return Ok(ApiResponse<object>.Ok(rules));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    [HttpPost("{id:int}/prices")]
    public async Task<IActionResult> AddPrice(int id, [FromBody] PriceRuleRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.ProductName)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Product name is required.", "productName"));
            if (req.CustomPrice < 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Price cannot be negative.", "customPrice"));
            if (req.DiscountPercent is < 0 or > 100) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Discount must be between 0 and 100%.", "discountPercent"));
            if (!await _db.CrmCustomers.AnyAsync(x => x.Id == id && !x.IsDeleted))
                return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            var name = req.ProductName.Trim();
            int? productId = req.ProductId;
            if (productId == null)
                productId = await _db.Products.Where(p => !p.IsDeleted && p.Name == name).Select(p => (int?)p.Id).FirstOrDefaultAsync();

            var rule = new CrmPriceRule {
                CrmCustomerId  = id, ProductName   = name,
                ProductId      = productId,        CustomPrice    = req.CustomPrice,
                DiscountPercent = req.DiscountPercent, CreatedBy  = AdminName
            };
            _db.CrmPriceRules.Add(rule);
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { rule.Id, rule.ProductName, rule.ProductId, rule.CustomPrice, rule.DiscountPercent }, "Price rule added"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    [HttpDelete("prices/{priceId:int}")]
    public async Task<IActionResult> DeletePrice(int priceId)
    {
        try
        {
            var rule = await _db.CrmPriceRules.FindAsync(priceId);
            if (rule == null || rule.IsDeleted) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Price rule not found."));
            rule.IsDeleted = true; rule.UpdatedAt = DateTime.UtcNow; rule.UpdatedBy = AdminName;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<bool>.Ok(true, "Price rule removed"));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Payment reminders (email via IEmailService, logged to AuditLogs) ─────
    // Single: POST /crm/{id}/remind { message? }  → { sent, skipped, reason, email, message, whatsAppUrl }
    [HttpPost("{id:int}/remind")]
    public async Task<IActionResult> SendReminder(int id, [FromBody] ReminderRequest? req)
    {
        try
        {
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            if (c.OutstandingDues <= 0) return BadRequest(ApiResponse<object>.Fail("NO_DUES", $"{c.Name} has no outstanding dues."));

            var company = await CompanyName();
            var template = string.IsNullOrWhiteSpace(req?.Message) ? DefaultReminderMessage : req!.Message!;
            var msg = Render(template, c, company);
            var waUrl = WhatsAppUrl(c.Phone, msg);

            if (string.IsNullOrWhiteSpace(c.Email))
            {
                await LogReminder(c, "skipped (no email)");
                await _db.SaveChangesAsync();
                return Ok(ApiResponse<object>.Ok(new { sent = 0, skipped = 1, reason = "NO_EMAIL", email = (string?)null, message = msg, whatsAppUrl = waUrl },
                    $"{c.Name} has no email on file — reminder not emailed."));
            }

            await _email.SendNowAsync(c.Email!, c.ContactName ?? c.Name, "crm-reminder", b => ReminderEmail(b, msg, c, company));
            await LogReminder(c, $"emailed to {c.Email}");
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { sent = 1, skipped = 0, reason = (string?)null, email = c.Email, message = msg, whatsAppUrl = waUrl },
                $"Reminder emailed to {c.Email}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment reminder failed for CRM customer {Id}", id);
            return StatusCode(500, ApiResponse<object>.Fail("EMAIL_FAILED", "Could not send the reminder — the mail server rejected it or could not be reached (details are in the server log)."));
        }
    }

    // Bulk: POST /crm/reminders { minOutstanding, bucket?, category?, message? }
    //   → { matched, sent, skipped, failed, skippedCustomers:[{id,name}], failedCustomers:[{id,name,error}] }
    [HttpPost("reminders")]
    public async Task<IActionResult> SendBulkReminders([FromBody] BulkReminderRequest req)
    {
        try
        {
            if (req.MinOutstanding is < 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Minimum outstanding cannot be negative.", "minOutstanding"));
            if (!string.IsNullOrWhiteSpace(req.Bucket) && req.Bucket != "All" && Buckets.All(b => b.Bucket != req.Bucket))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Unknown aging bucket '{req.Bucket}'.", "bucket"));

            var min = req.MinOutstanding ?? 0;
            var q = _db.CrmCustomers.Where(c => !c.IsDeleted && !c.IsBlocked && c.OutstandingDues > 0 && c.OutstandingDues >= min);
            if (!string.IsNullOrWhiteSpace(req.Category) && req.Category != "All") q = q.Where(c => c.Category == req.Category);
            var candidates = await q.OrderByDescending(c => c.OutstandingDues).ToListAsync();

            var now = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(req.Bucket) && req.Bucket != "All")
                candidates = candidates.Where(c => BucketFor(Math.Max(0, (int)(now - (c.DuesSince ?? c.CreatedAt)).TotalDays)) == req.Bucket).ToList();

            var company  = await CompanyName();
            var template = string.IsNullOrWhiteSpace(req.Message) ? DefaultReminderMessage : req.Message!;
            var skipped = new List<object>();
            var failed  = new List<object>();
            var sent = 0;

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c.Email))
                {
                    skipped.Add(new { c.Id, c.Name });
                    await LogReminder(c, "bulk: skipped (no email)");
                    continue;
                }
                try
                {
                    var msg = Render(template, c, company);
                    await _email.SendNowAsync(c.Email!, c.ContactName ?? c.Name, "crm-reminder", b => ReminderEmail(b, msg, c, company));
                    await LogReminder(c, $"bulk: emailed to {c.Email}");
                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bulk reminder failed for CRM customer {Id}", c.Id);
                    failed.Add(new { c.Id, c.Name, error = "Email could not be sent (see server log)" });
                    await LogReminder(c, $"bulk: failed ({ex.GetType().Name})");
                }
            }
            await _db.SaveChangesAsync();

            var summary = $"{sent} reminder(s) emailed" + (skipped.Count > 0 ? $", {skipped.Count} skipped (no email)" : "") + (failed.Count > 0 ? $", {failed.Count} failed" : "");
            return Ok(ApiResponse<object>.Ok(new {
                matched = candidates.Count, sent, skipped = skipped.Count, failed = failed.Count,
                skippedCustomers = skipped, failedCustomers = failed,
            }, candidates.Count == 0 ? "No customers match these filters." : summary));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Promo (birthday / anniversary) ────────────────────────────────────────
    [HttpPost("{id:int}/promo")]
    public async Task<IActionResult> SendPromo(int id, [FromBody] PromoRequest req)
    {
        try
        {
            var c = await _db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (c == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Customer not found."));
            if (req.Type is not ("birthday" or "anniversary"))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Promo type must be 'birthday' or 'anniversary'.", "type"));

            var company  = await CompanyName();
            var occasion = req.Type == "anniversary" ? "Anniversary" : "Birthday";
            var code     = $"{occasion.ToUpper()}{c.Id:D3}";
            var msg      = string.IsNullOrWhiteSpace(req.CustomMessage)
                ? $"Dear {c.Name}, Happy {occasion}! Enjoy a special discount — use code {code} on your next order. — {company}"
                : req.CustomMessage!;
            var waUrl    = WhatsAppUrl(c.Phone, msg);

            var emailed = false;
            if (!string.IsNullOrWhiteSpace(c.Email))
            {
                await _email.SendNowAsync(c.Email!, c.ContactName ?? c.Name, "crm-promo",
                    b => WahSpices.Infrastructure.Email.EmailTemplates.CrmGreeting(b, company, occasion, msg, code));
                emailed = true;
            }
            _db.AuditLogs.Add(new AuditLog {
                UserId = AdminId, UserName = AdminName, Module = "crm", Action = "promo",
                Details = $"{occasion} promo for CRM customer #{c.Id} {c.Name}: " + (emailed ? $"emailed to {c.Email}" : "no email — WhatsApp link generated"),
            });
            await _db.SaveChangesAsync();

            return Ok(ApiResponse<object>.Ok(new { emailed, email = c.Email, occasion, promoCode = code, message = msg, whatsAppUrl = waUrl },
                emailed ? $"{occasion} promo emailed to {c.Email}" : "No email on file — opening WhatsApp."));
        }
        catch (Exception) { throw; /* ExceptionMiddleware logs it; client gets a generic message + correlation id */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<bool> IsListValue(string type, string value)
    {
        var values = await _db.MasterDataValues.Where(m => m.Type == type).Select(m => m.Value).ToListAsync();
        return values.Count == 0 || values.Contains(value); // no list configured → accept
    }

    private async Task<IActionResult?> Validate(CrmCustomerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Name is required.", "name"));
        if (string.IsNullOrWhiteSpace(req.Phone)) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Phone is required.", "phone"));
        if (req.CreditLimit is < 0) return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Credit limit cannot be negative.", "creditLimit"));
        if (!string.IsNullOrWhiteSpace(req.Email) && !req.Email.Contains('@'))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Enter a valid email address.", "email"));
        if (!string.IsNullOrWhiteSpace(req.Category) && !await IsListValue("CustomerCategory", req.Category))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", $"Unknown customer category '{req.Category}'.", "category"));
        return null;
    }

    private async Task<string> CompanyName()
    {
        var name = await _db.SiteSettings.Select(s => s.SiteName).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(name) ? "WAH! Spices" : name;
    }

    private static string Render(string template, CrmCustomer c, string company)
    {
        var days = Math.Max(0, (int)(DateTime.UtcNow - (c.DuesSince ?? c.CreatedAt)).TotalDays);
        return template
            .Replace("{customer}", c.Name)
            .Replace("{amount}", c.OutstandingDues.ToString("N0", new System.Globalization.CultureInfo("en-IN")))
            .Replace("{days}", days.ToString())
            .Replace("{company}", company);
    }

    private static WahSpices.Infrastructure.Email.RenderedEmail ReminderEmail(WahSpices.Infrastructure.Email.EmailBrand b, string msg, CrmCustomer c, string company) =>
        WahSpices.Infrastructure.Email.EmailTemplates.CrmPaymentReminder(b, company, msg, c.OutstandingDues, c.CreditLimit);

    private static string? WhatsAppUrl(string phone, string msg)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length > 10 && digits.StartsWith("91")) digits = digits[2..];
        return digits.Length == 10 ? $"https://wa.me/91{digits}?text={Uri.EscapeDataString(msg)}" : null;
    }

    private Task LogReminder(CrmCustomer c, string outcome)
    {
        _db.AuditLogs.Add(new AuditLog {
            UserId = AdminId, UserName = AdminName, Module = "crm", Action = "payment-reminder",
            Details = $"Payment reminder to CRM customer #{c.Id} {c.Name} (dues ₹{c.OutstandingDues:N0}): {outcome}",
            IpAddress = HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
        return Task.CompletedTask;
    }

    private static object MapSummary(CrmCustomer c) => new {
        c.Id, c.Name, c.ContactName, c.Email, c.Phone, c.City, c.State,
        c.Category, c.Gstin, Pan = SensitiveMask.Tail(c.Pan), c.BillingAddress, c.ShippingAddress,
        c.CreditLimit, c.OutstandingDues, c.DuesSince,
        c.IsBlocked, c.BlockReason, c.IsActive,
        c.LoyaltyPoints, c.LoyaltyTier, c.LastOrderDate,
        c.BirthDate, c.AnniversaryDate, c.Notes, c.Tags, c.CreatedAt
    };

    private static object MapFull(CrmCustomer c) => new {
        c.Id, c.Name, c.ContactName, c.Email, c.Phone, c.City, c.State,
        c.Category, c.Gstin, Pan = SensitiveMask.Tail(c.Pan), c.BillingAddress, c.ShippingAddress,
        c.CreditLimit, c.OutstandingDues, c.DuesSince,
        c.IsBlocked, c.BlockReason, c.IsActive,
        c.LoyaltyPoints, c.LoyaltyTier, c.LastOrderDate,
        c.BirthDate, c.AnniversaryDate, c.Notes, c.Tags, c.CreatedAt,
        payments   = c.Payments.OrderByDescending(p => p.PaymentDate)
                      .Select(p => new { p.Id, p.Amount, p.PaymentDate, p.Mode, p.Reference, p.Notes }),
        priceRules = c.PriceRules.OrderBy(p => p.ProductName)
                      .Select(p => new { p.Id, p.ProductName, p.ProductId, p.CustomPrice, p.DiscountPercent })
    };
}

// ── Request models (nullable so validation errors come back in the ApiResponse shape) ──

public record CrmCustomerRequest(
    string? Name, string? ContactName, string? Email, string? Phone,
    string? City, string? State, string? Category, string? Gstin, string? Pan,
    string? BillingAddress, string? ShippingAddress,
    decimal? CreditLimit = 0, DateTime? BirthDate = null, DateTime? AnniversaryDate = null,
    string? Notes = null, string? Tags = null);

public record BlockRequest(bool Block, string? Reason);
public record CreditLimitRequest(decimal CreditLimit);
public record UpdateDuesRequest(decimal Amount);
public record LoyaltyRequest(int Points);
public record PaymentRequest(decimal Amount, DateTime? PaymentDate, string? Mode, string? Reference, string? Notes);
public record PriceRuleRequest(string? ProductName, int? ProductId, decimal CustomPrice, decimal DiscountPercent = 0);
public record ReminderRequest(string? Via, string? Message);
public record BulkReminderRequest(decimal? MinOutstanding, string? Bucket, string? Category, string? Message);
public record PromoRequest(string? Type, string? CustomMessage);
