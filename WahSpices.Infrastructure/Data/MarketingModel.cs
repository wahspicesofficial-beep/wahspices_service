using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Data;

/// <summary>EF mapping for campaigns, broadcasts and channel limits (kept out of AppDbContext to keep that file small).</summary>
public static class MarketingModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.MarketingOptOut).HasDefaultValue(false);
        });

        b.Entity<MarketingCampaign>(e =>
        {
            e.ToTable("MarketingCampaigns");
            e.Property(x => x.Type).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.ImageUrl).HasMaxLength(512);
            e.Property(x => x.ButtonLabel).HasMaxLength(40);
            e.Property(x => x.ButtonUrl).HasMaxLength(512);
            e.Property(x => x.ProductIds).HasMaxLength(200);
            e.Property(x => x.CouponCode).HasMaxLength(50);
            e.Property(x => x.AudienceType).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.Status, x.ScheduledAt });
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<CampaignRecipient>(e =>
        {
            e.ToTable("CampaignRecipients");
            e.Property(x => x.Channel).HasMaxLength(16);
            e.Property(x => x.Address).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => new { x.CampaignId, x.UserId, x.Channel }).IsUnique();
            e.HasIndex(x => new { x.CampaignId, x.Status });
            e.HasIndex(x => new { x.Channel, x.Status, x.NextAttemptAt });     // dispatcher: next pending row
            e.HasIndex(x => new { x.Channel, x.LastAttemptAt });                // throttle: attempts in the window
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MessagingChannelSetting>(e =>
        {
            e.ToTable("MessagingChannelSettings");
            e.Property(x => x.Channel).HasMaxLength(16);
            e.Property(x => x.PauseReason).HasMaxLength(300);
            e.Property(x => x.UpdatedBy).HasMaxLength(128);
            e.HasIndex(x => x.Channel).IsUnique();
        });

        b.Entity<Broadcast>(e =>
        {
            e.ToTable("Broadcasts");
            e.Property(x => x.Type).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.ImageUrl).HasMaxLength(512);
            e.Property(x => x.ButtonLabel).HasMaxLength(40);
            e.Property(x => x.ButtonUrl).HasMaxLength(512);
            e.Property(x => x.AudienceType).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.Status, x.ActiveFrom });
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<BroadcastRecipient>(e =>
        {
            e.ToTable("BroadcastRecipients");
            e.HasIndex(x => new { x.BroadcastId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ReadAt });
            e.HasOne(x => x.Broadcast).WithMany().HasForeignKey(x => x.BroadcastId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
