using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

/// <summary>
/// A "Regional Flavours" story page (/regions/&lt;slug&gt;): one Indian state / UT, the spices it grows,
/// its signature masalas and dishes, a long-form story (<see cref="RegionStorySection"/>s) and the
/// products that come from it. Admin-editable (Admin → Website → Regions); mapping in RegionModel.cs.
/// List-like fields are stored as small JSON arrays (validated by the admin API).
/// </summary>
public class Region : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    /// <summary>Id of the state on the website's India map (lowercase ISO 3166-2:IN suffix, e.g. "gj", "kl", "la").</summary>
    public string MapStateId { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    /// <summary>Short text shown on the home-page card and the region list.</summary>
    public string? CardText { get; set; }
    /// <summary>Landmark / scene shown on the card photo badge (e.g. "Hawa Mahal").</summary>
    public string? Landmark { get; set; }
    /// <summary>Masala named on the card tag (e.g. "Chole Masala").</summary>
    public string? FeaturedMasala { get; set; }
    public string AccentColor { get; set; } = "#1a5c2e";

    public string? HeroImageUrl { get; set; }
    public string? HeroImageCredit { get; set; }
    /// <summary>Optional separate (smaller) card image; falls back to the hero image.</summary>
    public string? CardImageUrl { get; set; }

    /// <summary>JSON: [{ "name": "", "note": "" }]</summary>
    public string? SignatureMasalasJson { get; set; }
    /// <summary>JSON: [{ "name": "", "note": "", "icon": "🌶️" }] — crops / spices the region produces.</summary>
    public string? ProducesJson { get; set; }
    /// <summary>JSON: [{ "name": "", "note": "" }]</summary>
    public string? DishesJson { get; set; }
    /// <summary>JSON: ["product-slug", …] — explicit product picks, in display order.</summary>
    public string? ProductSlugsJson { get; set; }
    /// <summary>Also show products whose Product.State matches this region's name / slug / map id.</summary>
    public bool AutoLinkProducts { get; set; } = true;

    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public bool ShowOnHome { get; set; } = true;
    public DateTime? PublishedAt { get; set; }

    public ICollection<RegionStorySection> Sections { get; set; } = new List<RegionStorySection>();
}

/// <summary>One section of a region's story article: heading + plain-text paragraphs + optional photo.</summary>
public class RegionStorySection
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public int SortOrder { get; set; }
    public string Heading { get; set; } = string.Empty;
    /// <summary>Plain text; blank lines separate paragraphs. Never rendered as HTML.</summary>
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ImageAlt { get; set; }
    public string? ImageCaption { get; set; }
    public string? ImageCredit { get; set; }

    public Region Region { get; set; } = null!;
}
