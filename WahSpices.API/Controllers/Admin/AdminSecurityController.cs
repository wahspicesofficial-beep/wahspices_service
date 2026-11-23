using Microsoft.AspNetCore.Mvc;
using WahSpices.API.Authorization;
using WahSpices.API.Middleware;
using WahSpices.Application.Common;

namespace WahSpices.API.Controllers.Admin;

/// <summary>IPs temporarily blocked by the abuse guard (many 401/403/404/429 in a short window).</summary>
[Route("api/admin/security")]
[RequireModule("AccessControl")]
public class AdminSecurityController : AdminBaseController
{
    private readonly AbuseGuardStore _store;
    private readonly ILogger<AdminSecurityController> _logger;

    public AdminSecurityController(AbuseGuardStore store, ILogger<AdminSecurityController> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>GET /api/admin/security/blocked-ips</summary>
    [HttpGet("blocked-ips")]
    public IActionResult BlockedIps() =>
        Ok(ApiResponse<object>.Ok(_store.ActiveBlocks().Select(b => new
        {
            ip = b.Ip, blockedAt = b.BlockedAt, until = b.Until, count = b.Count, lastRequest = b.LastRequest,
        })));

    /// <summary>DELETE /api/admin/security/blocked-ips/{ip} — lift a block early.</summary>
    [HttpDelete("blocked-ips/{ip}")]
    public IActionResult Unblock(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out _))
            return BadRequest(ApiResponse<object>.Fail("INVALID_IP", "Not a valid IP address."));
        var removed = _store.Unblock(ip);
        _logger.LogInformation("AbuseGuard: {Admin} unblocked {Ip}", AdminName, ip);
        return removed
            ? Ok(ApiResponse<object>.Ok(new { ip }, "IP unblocked."))
            : NotFound(ApiResponse<object>.Fail("NOT_FOUND", "That IP is not blocked."));
    }
}
