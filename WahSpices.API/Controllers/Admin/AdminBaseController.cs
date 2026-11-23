using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WahSpices.API.Controllers.Admin;

/// <summary>All admin controllers inherit from this. Requires AdminOnly policy.</summary>
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
public abstract class AdminBaseController : ControllerBase
{
    protected string AdminId => User.FindFirst("admin_id")?.Value ?? "unknown";
    protected string AdminName => User.FindFirst("admin_name")?.Value ?? "unknown";
}
