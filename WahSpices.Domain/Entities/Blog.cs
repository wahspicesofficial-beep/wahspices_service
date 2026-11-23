using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

public class BlogPost : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
    public string? ImageUrl { get; set; }
    public string? BgColor { get; set; }
    public string ReadTime { get; set; } = "5 min read";
    public string? Tags { get; set; }   // JSON array as string
    public string? Masala { get; set; } // Related product name
    public bool Featured { get; set; } = false;
    public bool IsPublished { get; set; } = true;
    public DateTime? PublishedAt { get; set; }
    public string? VideoUrl { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
}
