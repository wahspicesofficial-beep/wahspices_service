using Microsoft.EntityFrameworkCore;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Categories ───────────────────────────────────
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<PackTypeConfig> PackTypeConfigs => Set<PackTypeConfig>();

    // ── Customer Website ─────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<MasterDataValue> MasterDataValues => Set<MasterDataValue>();
    public DbSet<GstReturnFiling> GstReturnFilings => Set<GstReturnFiling>();
    public DbSet<SiteContentBlock> SiteContentBlocks => Set<SiteContentBlock>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<ReturnOrder> ReturnOrders => Set<ReturnOrder>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<OtpRecord> OtpRecords => Set<OtpRecord>();
    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<ContactMessageReply> ContactMessageReplies => Set<ContactMessageReply>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();

    // ── Payments & Refunds ───────────────────────────
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();

    // ── ERP / Admin ──────────────────────────────────
    public DbSet<AdminRole> AdminRoles => Set<AdminRole>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryLedger> InventoryLedgers => Set<InventoryLedger>();
    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();
    public DbSet<RawMaterialLedger> RawMaterialLedgers => Set<RawMaterialLedger>();
    public DbSet<PackingEntry> PackingEntries => Set<PackingEntry>();
    public DbSet<PackingEntryLine> PackingEntryLines => Set<PackingEntryLine>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceiptNote> GoodsReceiptNotes => Set<GoodsReceiptNote>();
    public DbSet<GrnItem> GrnItems => Set<GrnItem>();
    public DbSet<VendorPayment> VendorPayments => Set<VendorPayment>();
    public DbSet<VendorPriceHistory> VendorPriceHistories => Set<VendorPriceHistory>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<BomRecipe> BomRecipes => Set<BomRecipe>();
    public DbSet<BomIngredient> BomIngredients => Set<BomIngredient>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionBatch> ProductionBatches => Set<ProductionBatch>();
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();
    public DbSet<WastageRecord> WastageRecords => Set<WastageRecord>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<InvoiceSettings> InvoiceSettings => Set<InvoiceSettings>();
    public DbSet<HsnCode> HsnCodes => Set<HsnCode>();
    public DbSet<ItcEntry> ItcEntries => Set<ItcEntry>();
    public DbSet<CrmCustomer> CrmCustomers => Set<CrmCustomer>();
    public DbSet<CrmPayment> CrmPayments => Set<CrmPayment>();
    public DbSet<CrmPriceRule> CrmPriceRules => Set<CrmPriceRule>();
    public DbSet<ExpenseEntry> ExpenseEntries => Set<ExpenseEntry>();
    public DbSet<ExpenseBudget> ExpenseBudgets => Set<ExpenseBudget>();
    public DbSet<ExpenseCategoryConfig> ExpenseCategoryConfigs => Set<ExpenseCategoryConfig>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<ShiftMaster> Shifts => Set<ShiftMaster>();
    public DbSet<EmployeeShift> EmployeeShifts => Set<EmployeeShift>();
    public DbSet<AttendanceRegularization> AttendanceRegularizations => Set<AttendanceRegularization>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<PayrollEntry> PayrollEntries => Set<PayrollEntry>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<DepartmentMaster> Departments => Set<DepartmentMaster>();
    public DbSet<DesignationMaster> Designations => Set<DesignationMaster>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
    public DbSet<IncrementHistory> IncrementHistories => Set<IncrementHistory>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<EmployeeTransfer> EmployeeTransfers => Set<EmployeeTransfer>();
    public DbSet<RetailClient> RetailClients => Set<RetailClient>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<SalesOrderBill> SalesOrderBills => Set<SalesOrderBill>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<BarcodeLog> BarcodeLogs => Set<BarcodeLog>();
    // Company finance & compliance (CompanyFinance.cs; mapping in CompanyFinanceModel.cs)
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<ExpenseSplit> ExpenseSplits => Set<ExpenseSplit>();
    public DbSet<PartnerTransaction> PartnerTransactions => Set<PartnerTransaction>();
    public DbSet<CompanyAsset> CompanyAssets => Set<CompanyAsset>();
    public DbSet<CompanyAssetHistory> CompanyAssetHistories => Set<CompanyAssetHistory>();
    public DbSet<CompanyLicence> CompanyLicences => Set<CompanyLicence>();
    public DbSet<CompanyAlertLog> CompanyAlertLogs => Set<CompanyAlertLog>();

    // Customer communications — campaigns, broadcasts, channel limits (Marketing.cs; mapping in MarketingModel.cs)
    public DbSet<MarketingCampaign> MarketingCampaigns => Set<MarketingCampaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<MessagingChannelSetting> MessagingChannelSettings => Set<MessagingChannelSetting>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();
    public DbSet<BroadcastRecipient> BroadcastRecipients => Set<BroadcastRecipient>();

    // Regional Flavours — state story pages (Region.cs; mapping in RegionModel.cs)
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<ShippingProviderAccount> ShippingProviderAccounts => Set<ShippingProviderAccount>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<RegionStorySection> RegionStorySections => Set<RegionStorySection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Global query filter — soft delete ────────
        modelBuilder.Entity<User>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Product>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Order>().HasQueryFilter(e => !e.IsDeleted);

        // ── User ─────────────────────────────────────
        // ── Category ─────────────────────────────────
        modelBuilder.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Slug).HasMaxLength(128);
        });

        // ── User ─────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Phone).HasMaxLength(15);
        });

        // ── Product ──────────────────────────────────
        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Price).HasColumnType("decimal(10,2)");
            e.Property(x => x.Mrp).HasColumnType("decimal(10,2)");
            e.Property(x => x.Rating).HasColumnType("decimal(3,2)");
            e.Property(x => x.GstRate).HasColumnType("decimal(5,2)");
            e.Property(x => x.SubCategorySlug).HasMaxLength(128);
        });

        // ── Master data / GST filings ────────────────
        modelBuilder.Entity<MasterDataValue>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(64);
            e.Property(x => x.Value).HasMaxLength(128);
            e.Property(x => x.Label).HasMaxLength(128);
            e.HasIndex(x => new { x.Type, x.Value });
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        modelBuilder.Entity<SiteContentBlock>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.HasIndex(x => x.Key).IsUnique();
        });
        modelBuilder.Entity<GstReturnFiling>(e =>
        {
            e.Property(x => x.ReturnType).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Arn).HasMaxLength(64);
            foreach (var n in new[] { "TaxableValue", "TotalTax", "ItcClaimed", "NetPayable" })
                e.Property(n).HasColumnType("decimal(14,2)");
            e.HasIndex(x => new { x.ReturnType, x.Year, x.Month });
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        modelBuilder.Entity<ContactMessageReply>(e =>
        {
            e.Property(x => x.ToEmail).HasMaxLength(150);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.SentBy).HasMaxLength(100);
            e.HasIndex(x => x.ContactMessageId);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.ContactMessage).WithMany(x => x.Replies).HasForeignKey(x => x.ContactMessageId).OnDelete(DeleteBehavior.Cascade);
        });
        CompanyFinanceModel.Configure(modelBuilder);   // partners, expense splits, assets, licences
        MarketingModel.Configure(modelBuilder);        // campaigns, broadcasts, channel limits
        RegionModel.Configure(modelBuilder);           // regional flavours / state stories

        // ── ProductVariant ───────────────────────────
        modelBuilder.Entity<ProductVariant>(e =>
        {
            e.Property(x => x.Weight).HasMaxLength(32);
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.Price).HasColumnType("decimal(10,2)");
            e.Property(x => x.Mrp).HasColumnType("decimal(10,2)");
            e.HasIndex(x => new { x.ProductId, x.Weight });
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Order ────────────────────────────────────
        modelBuilder.Entity<Order>(e =>
        {
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.Property(x => x.GrandTotal).HasColumnType("decimal(10,2)");
            e.Property(x => x.Subtotal).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.User).WithMany(x => x.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Address).WithMany().HasForeignKey(x => x.AddressId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── OrderItem ────────────────────────────────
        modelBuilder.Entity<OrderItem>(e =>
        {
            e.Property(x => x.Price).HasColumnType("decimal(10,2)");
            e.Property(x => x.LineTotal).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.Product).WithMany(x => x.OrderItems).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── CartItem — unique per user+product ───────
        modelBuilder.Entity<CartItem>().HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        modelBuilder.Entity<WishlistItem>().HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();

        // ── Review — one per user+product ────────────
        modelBuilder.Entity<Review>().HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();

        // ── Purchase sub-entities ─────────────────────
        modelBuilder.Entity<PurchaseOrderItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasColumnType("decimal(10,2)");
            e.Property(x => x.TotalAmount).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.PurchaseOrder).WithMany(x => x.LineItems).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GoodsReceiptNote>(e =>
        {
            e.Property(x => x.InvoiceAmount).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne(x => x.Grn).HasForeignKey(x => x.GrnId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GrnItem>(e => e.Property(x => x.UnitPrice).HasColumnType("decimal(10,2)"));
        modelBuilder.Entity<VendorPayment>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<VendorPriceHistory>(e =>
        {
            e.Property(x => x.UnitPrice).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseReturn>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Inventory ────────────────────────────────
        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.CostPrice).HasColumnType("decimal(10,2)");
            // One product-level row (VariantId null) + one row per pack-size variant
            e.HasOne(x => x.Product).WithMany(x => x.InventoryItems).HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
        });

        // ── Raw material & packing ───────────────────
        modelBuilder.Entity<RawMaterial>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Code).HasMaxLength(32);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Category).HasMaxLength(80);
            e.Property(x => x.DisplayUnit).HasMaxLength(8);
            e.Property(x => x.HsnCode).HasMaxLength(16);
            e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CurrentStockKg).HasColumnType("decimal(14,3)");
            e.Property(x => x.ReorderLevelKg).HasColumnType("decimal(14,3)");
            e.Property(x => x.CostPerKg).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.DefaultProduct).WithMany().HasForeignKey(x => x.DefaultProductId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<RawMaterialLedger>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(32);
            e.Property(x => x.QuantityKg).HasColumnType("decimal(14,3)");
            e.Property(x => x.BalanceAfterKg).HasColumnType("decimal(14,3)");
            e.Property(x => x.UnitCostPerKg).HasColumnType("decimal(12,2)");
            e.Property(x => x.ReferenceType).HasMaxLength(16);
            e.Property(x => x.ReferenceId).HasMaxLength(40);
            e.Property(x => x.BatchNumber).HasMaxLength(60);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.PerformedBy).HasMaxLength(150);
            e.HasIndex(x => new { x.RawMaterialId, x.CreatedAt });
            e.HasIndex(x => x.ReferenceId);
            e.HasOne(x => x.RawMaterial).WithMany(x => x.Ledgers).HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PackingEntry>(e =>
        {
            e.Property(x => x.PackingNumber).HasMaxLength(32);
            e.HasIndex(x => x.PackingNumber).IsUnique();
            foreach (var n in new[] { "RawUsedKg", "PackedKg", "WastageKg", "VarianceKg" })
                e.Property(n).HasColumnType("decimal(14,3)");
            e.Property(x => x.CostPerKg).HasColumnType("decimal(12,2)");
            e.Property(x => x.BatchNumber).HasMaxLength(60);
            e.Property(x => x.PackedBy).HasMaxLength(150);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ReversedBy).HasMaxLength(150);
            e.Property(x => x.ReversalReason).HasMaxLength(500);
            e.HasIndex(x => new { x.RawMaterialId, x.PackedOn });
            e.HasOne(x => x.RawMaterial).WithMany().HasForeignKey(x => x.RawMaterialId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(x => x.PackingEntry).HasForeignKey(x => x.PackingEntryId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PackingEntryLine>(e =>
        {
            e.Property(x => x.PackLabel).HasMaxLength(32);
            e.Property(x => x.TotalKg).HasColumnType("decimal(14,3)");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.InventoryItem).WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Payroll ──────────────────────────────────
        modelBuilder.Entity<PayrollEntry>(e =>
        {
            e.HasIndex(x => new { x.EmployeeId, x.Month, x.Year }).IsUnique();
            e.Property(x => x.GrossSalary).HasColumnType("decimal(12,2)");
            e.Property(x => x.NetSalary).HasColumnType("decimal(12,2)");
        });

        // ── Attendance — unique per employee+date ────
        modelBuilder.Entity<AttendanceRecord>().HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        modelBuilder.Entity<LeaveBalance>().HasIndex(x => new { x.EmployeeId, x.Year }).IsUnique();

        // ── Invoice ──────────────────────────────────
        modelBuilder.Entity<Invoice>(e =>
        {
            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.Property(x => x.Subtotal).HasColumnType("decimal(12,2)");
            e.Property(x => x.DiscountAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.DeliveryCharge).HasColumnType("decimal(12,2)");
            e.Property(x => x.CgstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.SgstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.IgstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.TotalAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.CouponDiscount).HasColumnType("decimal(12,2)");
            e.Property(x => x.RoundOff).HasColumnType("decimal(6,2)");
            e.Property(x => x.PaymentMethod).HasMaxLength(40);
            e.Property(x => x.PlaceOfSupply).HasMaxLength(60);
            e.Property(x => x.Source).HasMaxLength(16).HasDefaultValue("Online");
            e.Property(x => x.LastSentTo).HasMaxLength(256);
            e.HasMany(x => x.LineItems).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── InvoiceLineItem ───────────────────────────
        modelBuilder.Entity<InvoiceLineItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasColumnType("decimal(10,2)");
            e.Property(x => x.MrpPrice).HasColumnType("decimal(10,2)");
            e.Property(x => x.DiscountAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.TaxableAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.GstRate).HasColumnType("decimal(5,2)");
            e.Property(x => x.CgstRate).HasColumnType("decimal(5,2)");
            e.Property(x => x.SgstRate).HasColumnType("decimal(5,2)");
            e.Property(x => x.IgstRate).HasColumnType("decimal(5,2)");
            e.Property(x => x.CgstAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.SgstAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.IgstAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.TaxAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.LineTotal).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Employee ─────────────────────────────────
        modelBuilder.Entity<Employee>().HasIndex(x => x.EmployeeId).IsUnique();

        // ── Audit log — no soft delete filter ────────
        modelBuilder.Entity<AuditLog>().HasQueryFilter(_ => true);

        // ── int properties stored as TINYINT in DB ───
        modelBuilder.Entity<Review>().Property(x => x.Rating).HasConversion<byte>();
        modelBuilder.Entity<Feedback>().Property(x => x.Rating).HasConversion<byte>();
        modelBuilder.Entity<PayrollEntry>().Property(x => x.Month).HasConversion<byte>();


        // ── ReturnOrder ───────────────────────────────
        modelBuilder.Entity<ReturnOrder>(e =>
        {
            e.HasIndex(x => x.ReturnNumber).IsUnique();
            e.HasIndex(x => x.CreditNoteNumber).IsUnique().HasFilter("[CreditNoteNumber] IS NOT NULL");
            e.Property(x => x.SubtotalSelling).HasColumnType("decimal(12,2)");
            e.Property(x => x.TaxableValue).HasColumnType("decimal(12,2)");
            e.Property(x => x.CgstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.SgstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.IgstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.TotalGstAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.TcsAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.CreditNoteAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.GrandRefundAmount).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Payment ──────────────────────────────────
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.RazorpayOrderId).IsUnique();
            e.HasIndex(x => x.RazorpayPaymentId).HasFilter("[RazorpayPaymentId] IS NOT NULL");
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Refund ───────────────────────────────────
        modelBuilder.Entity<Refund>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Payment).WithMany(x => x.Refunds).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReturnOrder).WithMany().HasForeignKey(x => x.ReturnOrderId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── ReturnItem ────────────────────────────────
        modelBuilder.Entity<ReturnItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasColumnType("decimal(10,2)");
            e.Property(x => x.LineTotal).HasColumnType("decimal(10,2)");
            e.Property(x => x.TaxableValue).HasColumnType("decimal(10,2)");
            e.Property(x => x.CgstAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.SgstAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.IgstAmount).HasColumnType("decimal(10,2)");
            e.Property(x => x.TotalTax).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.ReturnOrder).WithMany(x => x.Items).HasForeignKey(x => x.ReturnOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── AdminRole ─────────────────────────────────
        modelBuilder.Entity<AdminRole>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(256);
            e.Property(x => x.Color).HasMaxLength(128);
        });

        // ── AdminUser / AdminSession ──────────────────
        modelBuilder.Entity<AdminUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Role).HasMaxLength(64);
            e.Property(x => x.TotpSecret).HasMaxLength(512);   // stored encrypted
            e.HasOne(x => x.AdminRole).WithMany(x => x.Users)
             .HasForeignKey(x => x.AdminRoleId).OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });
        modelBuilder.Entity<AdminSession>(e =>
        {
            e.HasIndex(x => x.JwtJti).IsUnique();
            e.Property(x => x.JwtJti).HasMaxLength(128);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.Location).HasMaxLength(128);
            e.HasOne(x => x.Admin).WithMany(x => x.Sessions).HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Shipping aggregators ─
        modelBuilder.Entity<ShippingProviderAccount>(e =>
        {
            e.Property(x => x.ProviderCode).HasMaxLength(40);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.ApiBaseUrl).HasMaxLength(300);
            e.Property(x => x.ApiUsername).HasMaxLength(200);
            e.Property(x => x.PickupLocation).HasMaxLength(100);
            e.Property(x => x.PickupPincode).HasMaxLength(10);
            e.Property(x => x.ChannelId).HasMaxLength(40);
            e.Property(x => x.TrackingUrlTemplate).HasMaxLength(300);
            e.Property(x => x.DefaultHsn).HasMaxLength(20);
            e.Property(x => x.LastTestMessage).HasMaxLength(500);
            foreach (var d in new[] { nameof(ShippingProviderAccount.DefaultLengthCm), nameof(ShippingProviderAccount.DefaultBreadthCm),
                                      nameof(ShippingProviderAccount.DefaultHeightCm), nameof(ShippingProviderAccount.PackagingWeightKg),
                                      nameof(ShippingProviderAccount.MinWeightKg), nameof(ShippingProviderAccount.DefaultTaxPercent) })
                e.Property<decimal>(d).HasPrecision(10, 3);
        });
        modelBuilder.Entity<Shipment>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.AwbCode);
            e.Property(x => x.ProviderCode).HasMaxLength(40);
            e.Property(x => x.ProviderOrderId).HasMaxLength(60);
            e.Property(x => x.ProviderShipmentId).HasMaxLength(60);
            e.Property(x => x.AwbCode).HasMaxLength(60);
            e.Property(x => x.CourierCompanyId).HasMaxLength(20);
            e.Property(x => x.CourierName).HasMaxLength(120);
            e.Property(x => x.EstimatedDeliveryDays).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(80);
            e.Property(x => x.StatusCode).HasMaxLength(20);
            e.Property(x => x.LabelUrl).HasMaxLength(600);
            e.Property(x => x.ManifestUrl).HasMaxLength(600);
            e.Property(x => x.InvoiceUrl).HasMaxLength(600);
            e.Property(x => x.PickupTokenNumber).HasMaxLength(60);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.FreightCharge).HasPrecision(10, 2);
            e.Property(x => x.CodAmount).HasPrecision(10, 2);
            e.Property(x => x.WeightKg).HasPrecision(10, 3);
            e.Property(x => x.LengthCm).HasPrecision(10, 2);
            e.Property(x => x.BreadthCm).HasPrecision(10, 2);
            e.Property(x => x.HeightCm).HasPrecision(10, 2);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ProviderAccount).WithMany().HasForeignKey(x => x.ProviderAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ShipmentEvent>(e =>
        {
            e.HasIndex(x => new { x.ShipmentId, x.OccurredAt });
            e.Property(x => x.Status).HasMaxLength(80);
            e.Property(x => x.Activity).HasMaxLength(400);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.Source).HasMaxLength(20);
            e.HasOne(x => x.Shipment).WithMany(x => x.Events).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Field-level encryption at rest (AES-256-GCM) — see Security/FieldEncryption.cs ─
        // App code keeps reading/writing plaintext; only the DB holds ciphertext ("enc:v1:k1:…").
        // These columns can't be searched/joined in SQL — emails, phones and GSTINs are intentionally NOT listed.
        foreach (var (clr, props) in WahSpices.Infrastructure.Security.EncryptedFields.Map)
            foreach (var prop in props)
            {
                var purpose = WahSpices.Infrastructure.Security.EncryptedFields.Purpose(clr, prop);
                modelBuilder.Entity(clr).Property<string?>(prop).HasConversion(
                    new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string?, string?>(
                        v => v == null ? null : WahSpices.Infrastructure.Security.FieldEncryptor.Protect(v, purpose),
                        v => v == null ? null : WahSpices.Infrastructure.Security.FieldEncryptor.Unprotect(v, purpose)));
            }

        // ── Global: all C# enum properties map to TINYINT in this schema ─
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType.IsEnum)
                    property.SetProviderClrType(typeof(byte));
            }
        }
    }
}
