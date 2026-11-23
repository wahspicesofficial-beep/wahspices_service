using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Data;

/// <summary>EF mapping for the company finance & compliance module (kept out of AppDbContext to keep that file small).</summary>
public static class CompanyFinanceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        // New columns on the existing ExpenseEntries table — defaults keep existing rows valid (company-paid, not split)
        modelBuilder.Entity<ExpenseEntry>(e =>
        {
            e.Property(x => x.PayerType).HasMaxLength(16).HasDefaultValue(CompanyPayer.Company);
            e.Property(x => x.SplitMethod).HasMaxLength(16).HasDefaultValue(ExpenseSplitMethod.None);
            e.Property(x => x.SourceType).HasMaxLength(16);
            e.HasIndex(x => x.PaidByPartnerId);
            e.HasMany(x => x.Splits).WithOne(x => x.ExpenseEntry).HasForeignKey(x => x.ExpenseEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Partner>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.SharePercent).HasColumnType("decimal(5,2)");
            e.Property(x => x.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<ExpenseSplit>(e =>
        {
            e.Property(x => x.Percent).HasColumnType("decimal(9,4)");
            e.Property(x => x.Amount).HasColumnType("decimal(14,2)");
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ExpenseEntryId, x.PartnerId });
        });

        modelBuilder.Entity<PartnerTransaction>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(16);
            e.Property(x => x.Amount).HasColumnType("decimal(14,2)");
            e.Property(x => x.Mode).HasMaxLength(32);
            e.Property(x => x.Reference).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne(x => x.FromPartner).WithMany().HasForeignKey(x => x.FromPartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToPartner).WithMany().HasForeignKey(x => x.ToPartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
        });

        modelBuilder.Entity<CompanyAsset>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Cost).HasColumnType("decimal(14,2)");
            e.Property(x => x.DisposalValue).HasColumnType("decimal(14,2)");
            e.Property(x => x.Vendor).HasMaxLength(160);
            e.Property(x => x.InvoiceNumber).HasMaxLength(64);
            e.Property(x => x.DocumentUrl).HasMaxLength(256);
            e.Property(x => x.Location).HasMaxLength(128);
            e.Property(x => x.AssignedTo).HasMaxLength(128);
            e.Property(x => x.SerialNumber).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.ExpenseEntryId);
        });

        modelBuilder.Entity<CompanyAssetHistory>(e =>
        {
            e.Property(x => x.FromStatus).HasMaxLength(16);
            e.Property(x => x.ToStatus).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.CompanyAssetId);
        });

        modelBuilder.Entity<CompanyLicence>(e =>
        {
            e.Property(x => x.LicenceType).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Number).HasMaxLength(64);
            e.Property(x => x.IssuingAuthority).HasMaxLength(160);
            e.Property(x => x.Fee).HasColumnType("decimal(14,2)");
            e.Property(x => x.DocumentUrl).HasMaxLength(256);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.ExpiryDate);
            e.HasIndex(x => x.PreviousLicenceId);
        });

        modelBuilder.Entity<CompanyAlertLog>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Recipient).HasMaxLength(256);
            e.HasIndex(x => new { x.Kind, x.RefId, x.ExpiryDate, x.Threshold }).IsUnique();
        });
    }
}
