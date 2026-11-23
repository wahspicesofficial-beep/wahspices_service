using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Data;

/// <summary>EF mapping for the "Regional Flavours" stories (Region.cs).</summary>
public static class RegionModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<Region>(e =>
        {
            e.ToTable("Regions");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Slug).HasMaxLength(80);
            e.Property(x => x.MapStateId).HasMaxLength(8);
            e.Property(x => x.Tagline).HasMaxLength(120);
            e.Property(x => x.CardText).HasMaxLength(400);
            e.Property(x => x.Landmark).HasMaxLength(80);
            e.Property(x => x.FeaturedMasala).HasMaxLength(60);
            e.Property(x => x.AccentColor).HasMaxLength(9);
            e.Property(x => x.HeroImageUrl).HasMaxLength(512);
            e.Property(x => x.HeroImageCredit).HasMaxLength(300);
            e.Property(x => x.CardImageUrl).HasMaxLength(512);
            e.Property(x => x.SignatureMasalasJson).HasMaxLength(8000);
            e.Property(x => x.ProducesJson).HasMaxLength(8000);
            e.Property(x => x.DishesJson).HasMaxLength(8000);
            e.Property(x => x.ProductSlugsJson).HasMaxLength(4000);
            e.Property(x => x.SeoTitle).HasMaxLength(70);
            e.Property(x => x.SeoDescription).HasMaxLength(160);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            // Unique among live rows: a deleted region keeps its row (so the seeder never re-creates it)
            // without blocking a new region with the same slug.
            e.HasIndex(x => x.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.IsPublished, x.DisplayOrder });
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<RegionStorySection>(e =>
        {
            e.ToTable("RegionStorySections");
            e.Property(x => x.Heading).HasMaxLength(150);
            e.Property(x => x.Body).HasMaxLength(8000);
            e.Property(x => x.ImageUrl).HasMaxLength(512);
            e.Property(x => x.ImageAlt).HasMaxLength(200);
            e.Property(x => x.ImageCaption).HasMaxLength(300);
            e.Property(x => x.ImageCredit).HasMaxLength(300);
            e.HasIndex(x => new { x.RegionId, x.SortOrder });
            e.HasOne(x => x.Region).WithMany(r => r.Sections).HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.Region.IsDeleted);   // matches Region's soft-delete filter
        });
    }
}
