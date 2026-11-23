using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

public class Recipe : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;   // Breakfast, Lunch, Dinner, Snacks, etc.
    public string? CookTime { get; set; }                   // e.g. "30 mins"
    public string? PrepTime { get; set; }                   // e.g. "15 mins"
    public int Servings { get; set; } = 2;
    public string Difficulty { get; set; } = "Easy";        // Easy / Medium / Hard
    public string? Ingredients { get; set; }                // JSON: ["1 cup flour", ...]
    public string? Steps { get; set; }                      // JSON: [{"step":1,"instruction":"..."}]
    public string? Tips { get; set; }
    public string? ImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? RelatedMasala { get; set; }              // product slug
    public string? Tags { get; set; }                       // JSON: ["spicy","quick"]
    public bool Featured { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime? PublishedAt { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
}
