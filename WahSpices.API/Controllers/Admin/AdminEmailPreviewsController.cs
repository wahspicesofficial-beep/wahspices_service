using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Controllers.Admin;

/// <summary>
/// Development only: renders every transactional email with realistic sample data (real products from the catalogue,
/// real business details from Settings). Returns 404 in any other environment.
///   GET /api/admin/email-previews                       → list (key, group, description, subject, url)
///   GET /api/admin/email-previews/{key}                 → rendered HTML
///   GET /api/admin/email-previews/{key}?format=text     → plain-text alternative
/// </summary>
[Route("api/admin/email-previews")]
[RequireModule("Settings")]
public class AdminEmailPreviewsController : AdminBaseController
{
    private readonly IWebHostEnvironment _env;
    private readonly IEmailBrandProvider _brand;
    private readonly AppDbContext _db;

    public AdminEmailPreviewsController(IWebHostEnvironment env, IEmailBrandProvider brand, AppDbContext db)
    {
        _env = env;
        _brand = brand;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!_env.IsDevelopment()) return NotFound();
        var b = await _brand.GetAsync();
        var products = await SampleProductsAsync();
        var list = Catalog().Select(e => new
        {
            e.Key, e.Group, e.Description,
            Subject = e.Render(b, products).Subject,
            Url = $"/api/admin/email-previews/{e.Key}",
        });
        return Ok(ApiResponse<object>.Ok(list));
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Preview(string key, [FromQuery] string? format)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var entry = Catalog().FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", $"Unknown template '{key}'."));
        var r = entry.Render(await _brand.GetAsync(), await SampleProductsAsync());
        return string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
            ? Content($"Subject: {r.Subject}\n\n{r.Text}", "text/plain; charset=utf-8")
            : Content(r.Html, "text/html; charset=utf-8");
    }

    private IEnumerable<EmailPreviewCatalog.Entry> Catalog() =>
        EmailPreviewCatalog.All.Select(e => e.Key == "payroll-slip"
            ? e with { Render = (b, _) => EmailTemplates.SalarySlip(b, "Ramesh Patel", "October 2026", AdminPayrollController.BuildSlipEmailFragment(SamplePayroll())) }
            : e);

    private async Task<IReadOnlyList<EmailPreviewCatalog.SampleProduct>> SampleProductsAsync()
    {
        var rows = await _db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ImageUrl != null && p.Slug != null)
            .OrderBy(p => p.Id).Take(3)
            .Select(p => new { p.Name, p.Weight, p.Price, p.Mrp, p.ImageUrl, p.Slug })
            .ToListAsync();
        return rows.Count < 3
            ? EmailPreviewCatalog.FallbackProducts
            : rows.Select(p => new EmailPreviewCatalog.SampleProduct(p.Name, string.IsNullOrWhiteSpace(p.Weight) ? "100g" : p.Weight, p.Price, Math.Max(p.Mrp, p.Price), p.ImageUrl, p.Slug)).ToList();
    }

    private static PayrollEntry SamplePayroll() => new()
    {
        Month = 10, Year = 2026, DaysWorked = 26, LossOfPayDays = 0,
        BasicSalary = 15000, Hra = 6000, Ta = 1600, Da = 0, Bonus = 1200, OvertimePay = 800,
        GrossSalary = 24600, Epf = 1800, Esic = 0, ProfessionalTax = 200, Tds = 0, OtherDeductions = 0, NetSalary = 22600,
        IsApproved = true, IsDisbursed = true, DisbursedAt = DateTime.UtcNow.AddDays(-14),
        Employee = new Employee
        {
            Name = "Ramesh Patel", EmployeeId = "EMP-007", Department = "Production", Designation = "Senior Blender",
            JoinedAt = new DateTime(2023, 4, 1), PanNumber = "ABCPP1234K", PfNumber = "GJ/AHD/0012345/007", BankAccountNo = "123456784521",
        },
    };
}
