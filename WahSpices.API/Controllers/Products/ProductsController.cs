using WahSpices.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WahSpices.Application.DTOs.Product;
using WahSpices.Application.Interfaces.Services;
using System.Security.Claims;

namespace WahSpices.API.Controllers.Products;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
        => _productService = productService;

    /// <summary>Get all products with optional filters and pagination</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ProductListRequest request)
    {
        var result = await _productService.GetAllAsync(request);
        return Ok(result);
    }

    /// <summary>Get featured products for homepage</summary>
    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured([FromQuery] int limit = 8)
    {
        var result = await _productService.GetFeaturedAsync(limit);
        return Ok(result);
    }

    /// <summary>Get all product categories</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var result = await _productService.GetCategoriesAsync();
        return Ok(result);
    }

    /// <summary>Search products by keyword</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var result = await _productService.SearchAsync(q, page, limit);
        return Ok(result);
    }

    /// <summary>Get products by Indian state</summary>
    [HttpGet("by-state/{state}")]
    public async Task<IActionResult> GetByState(string state)
    {
        var result = await _productService.GetByStateAsync(state);
        return Ok(result);
    }

    /// <summary>Get product detail by slug</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        var result = await _productService.GetBySlugAsync(slug);
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Get related products</summary>
    [HttpGet("{productId:int}/related")]
    public async Task<IActionResult> GetRelated(int productId, [FromQuery] string category, [FromQuery] int limit = 4)
    {
        var result = await _productService.GetRelatedAsync(productId, category, limit);
        return Ok(result);
    }

    /// <summary>Get reviews for a product</summary>
    [HttpGet("{productId:int}/reviews")]
    public async Task<IActionResult> GetReviews(
        int productId,
        [FromQuery] int? rating,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10)
    {
        var result = await _productService.GetReviewsAsync(productId, rating, sort, page, limit);
        return Ok(result);
    }

    /// <summary>The signed-in customer's own review of this product (any status) — used to show "awaiting approval"</summary>
    [Authorize]
    [HttpGet("{productId:int}/reviews/mine")]
    public async Task<IActionResult> GetMyReview(int productId)
    {
        if (User.FindFirst("admin_id") != null || !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Ok(Application.Common.ApiResponse<object?>.Ok(null));
        return Ok(await _productService.GetMyReviewAsync(productId, userId));
    }

    /// <summary>Submit a product review. Saved as Pending until an admin approves it
    /// (verified-purchase flag is computed from the customer's orders).</summary>
    [Authorize]
    [HttpPost("{productId:int}/reviews")]
    [RequireHumanProof("review")]
    public async Task<IActionResult> SubmitReview(int productId, [FromBody] SubmitReviewRequest request)
    {
        // Admin tokens also carry NameIdentifier (the admin id) — never attribute a review to a customer row by mistake
        if (User.FindFirst("admin_id") != null || !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            // 403 (not 401): the website's axios interceptor logs everyone out on 401
            return StatusCode(403, Application.Common.ApiResponse<object>.Fail("FORBIDDEN", "Please sign in with your customer account to post a review."));
        if (request == null)
            return BadRequest(Application.Common.ApiResponse<object>.Fail("VALIDATION_ERROR", "Review details are required."));
        var result = await _productService.SubmitReviewAsync(productId, userId, request);
        if (result.Success) return StatusCode(201, result);
        return result.Error?.Code switch
        {
            "NOT_FOUND" => NotFound(result),
            "ALREADY_REVIEWED" => Conflict(result),
            "UNAUTHORIZED" => Unauthorized(result),
            _ => BadRequest(result),
        };
    }
}
