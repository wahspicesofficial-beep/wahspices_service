using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.Interfaces.Services;

namespace WahSpices.API.Controllers.Categories;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _svc;
    public CategoriesController(ICategoryService svc) => _svc = svc;

    /// <summary>Get all active categories ordered by display order</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _svc.GetAllAsync(includeInactive: false));
}
