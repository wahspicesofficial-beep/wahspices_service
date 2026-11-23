using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Common;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WahSpices.API.Authorization;

namespace WahSpices.API.Controllers.Admin;

[Route("api/admin/settings")]
[RequireModule("Settings")]
public class AdminSettingsController : AdminBaseController
{
    private readonly AppDbContext _db;
    public AdminSettingsController(AppDbContext db) => _db = db;

    [RequireModule(AdminModules.AnyAdmin)]   // company details are shown on many pages
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var s = await _db.SiteSettings.FirstOrDefaultAsync()
                ?? new SiteSettings();
        return Ok(ApiResponse<object>.Ok(Map(s)));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SettingsRequest req)
    {
        // Udyam (MSME) registration format: UDYAM-<state>-<2 digits>-<7 digits>, e.g. UDYAM-GJ-01-0646120
        if (!string.IsNullOrWhiteSpace(req.UdyamNumber) &&
            !System.Text.RegularExpressions.Regex.IsMatch(req.UdyamNumber.Trim(), @"^UDYAM-[A-Za-z]{2}-\d{2}-\d{7}$"))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Udyam number should look like UDYAM-GJ-01-0646120.", "udyamNumber"));

        var s = await _db.SiteSettings.FirstOrDefaultAsync();
        if (s == null)
        {
            s = new SiteSettings { CreatedBy = AdminName };
            _db.SiteSettings.Add(s);
        }

        s.SiteName          = req.SiteName          ?? s.SiteName;
        s.Tagline           = req.Tagline            ?? s.Tagline;
        s.Email             = req.Email              ?? s.Email;
        s.Phone             = req.Phone              ?? s.Phone;
        s.Address           = req.Address            ?? s.Address;
        s.Gstin             = req.Gstin              ?? s.Gstin;
        s.Fssai             = req.Fssai              ?? s.Fssai;
        s.UdyamNumber       = req.UdyamNumber == null ? s.UdyamNumber : (string.IsNullOrWhiteSpace(req.UdyamNumber) ? null : req.UdyamNumber.Trim().ToUpperInvariant());
        s.FreeDeliveryAbove = req.FreeDeliveryAbove  ?? s.FreeDeliveryAbove;
        s.DeliveryCharge    = req.DeliveryCharge     ?? s.DeliveryCharge;
        s.AvgDeliveryDays   = req.AvgDeliveryDays    ?? s.AvgDeliveryDays;
        s.CodAvailable      = req.CodAvailable       ?? s.CodAvailable;
        s.OrderEmails       = req.OrderEmails        ?? s.OrderEmails;
        s.LowStockAlerts    = req.LowStockAlerts     ?? s.LowStockAlerts;
        s.ReviewAlerts      = req.ReviewAlerts       ?? s.ReviewAlerts;
        s.WeeklyReport      = req.WeeklyReport       ?? s.WeeklyReport;
        s.MaintenanceMode   = req.MaintenanceMode    ?? s.MaintenanceMode;
        s.UpdatedAt         = DateTime.UtcNow;
        s.UpdatedBy         = AdminName;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Map(s)));
    }

    private static object Map(SiteSettings s) => new
    {
        siteName          = s.SiteName,
        tagline           = s.Tagline,
        email             = s.Email,
        phone             = s.Phone,
        address           = s.Address,
        gstin             = s.Gstin,
        gst               = s.Gstin,   // legacy alias (older readers)
        fssai             = s.Fssai,
        udyamNumber       = s.UdyamNumber,
        freeDeliveryAbove = s.FreeDeliveryAbove,
        deliveryCharge    = s.DeliveryCharge,
        avgDeliveryDays   = s.AvgDeliveryDays,
        codAvailable      = s.CodAvailable,
        orderEmails       = s.OrderEmails,
        lowStockAlerts    = s.LowStockAlerts,
        reviewAlerts      = s.ReviewAlerts,
        weeklyReport      = s.WeeklyReport,
        maintenanceMode   = s.MaintenanceMode,
    };
}

public record SettingsRequest(
    string? SiteName, string? Tagline, string? Email, string? Phone, string? Address,
    string? Gstin, string? Fssai, string? UdyamNumber,
    decimal? FreeDeliveryAbove, decimal? DeliveryCharge, string? AvgDeliveryDays, bool? CodAvailable,
    bool? OrderEmails, bool? LowStockAlerts, bool? ReviewAlerts, bool? WeeklyReport,
    bool? MaintenanceMode
);
