-- ============================================================
--  WAH! SPICES — SQL SERVER DATABASE SCHEMA
--  Database: db55965
--  Compatible: SQL Server 2019+ / Azure SQL
-- ============================================================

USE db55965;
GO

-- ============================================================
--  SECTION 1 — CUSTOMER WEBSITE TABLES
-- ============================================================

-- ── Users ──────────────────────────────────────────────────
CREATE TABLE Users (
    Id                   INT IDENTITY(1,1) PRIMARY KEY,
    Name                 NVARCHAR(128)     NOT NULL,
    Email                NVARCHAR(256)     NOT NULL UNIQUE,
    Phone                NVARCHAR(15)      NOT NULL UNIQUE,
    PasswordHash         NVARCHAR(512)     NULL,
    AvatarUrl            NVARCHAR(500)     NULL,
    Role                 TINYINT           NOT NULL DEFAULT 0,  -- 0=Customer
    LoyaltyPoints        INT               NOT NULL DEFAULT 0,
    IsEmailVerified      BIT               NOT NULL DEFAULT 0,
    IsPhoneVerified      BIT               NOT NULL DEFAULT 0,
    IsActive             BIT               NOT NULL DEFAULT 1,
    IsLocked             BIT               NOT NULL DEFAULT 0,
    FailedLoginAttempts  INT               NOT NULL DEFAULT 0,
    LockedUntil          DATETIME2         NULL,
    RefreshToken         NVARCHAR(512)     NULL,
    RefreshTokenExpiry   DATETIME2         NULL,
    PasswordResetToken   NVARCHAR(256)     NULL,
    PasswordResetExpiry  DATETIME2         NULL,
    LastLoginAt          DATETIME2         NULL,
    GoogleId             NVARCHAR(128)     NULL,
    IsDeleted            BIT               NOT NULL DEFAULT 0,
    CreatedAt            DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt            DATETIME2         NULL,
    CreatedBy            NVARCHAR(64)      NULL,
    UpdatedBy            NVARCHAR(64)      NULL
);

CREATE INDEX IX_Users_Email  ON Users(Email);
CREATE INDEX IX_Users_Phone  ON Users(Phone);

-- ── Addresses ──────────────────────────────────────────────
CREATE TABLE Addresses (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT           NOT NULL REFERENCES Users(Id),
    Name        NVARCHAR(128) NOT NULL,
    Phone       NVARCHAR(15)  NOT NULL,
    AddressLine NVARCHAR(512) NOT NULL,
    City        NVARCHAR(100) NOT NULL,
    State       NVARCHAR(100) NOT NULL,
    Pincode     NVARCHAR(10)  NOT NULL,
    Landmark    NVARCHAR(256) NULL,
    Type        TINYINT       NOT NULL DEFAULT 0,  -- 0=Home,1=Office,2=Other
    IsDefault   BIT           NOT NULL DEFAULT 0,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   DATETIME2     NULL
);

-- ── Products ───────────────────────────────────────────────
CREATE TABLE Products (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(256)     NOT NULL,
    Slug            NVARCHAR(256)     NOT NULL UNIQUE,
    Description     NVARCHAR(MAX)     NULL,
    Ingredients     NVARCHAR(MAX)     NULL,
    Category        TINYINT           NOT NULL,   -- 0=BlendMasala,1=Powder,2=Specialty...
    State           NVARCHAR(100)     NULL,
    StateLabel      NVARCHAR(128)     NULL,
    StateColor      NVARCHAR(10)      NULL,
    Price           DECIMAL(10,2)     NOT NULL,
    Mrp             DECIMAL(10,2)     NOT NULL,
    Weight          NVARCHAR(20)      NOT NULL,
    PackType        TINYINT           NOT NULL DEFAULT 0,  -- 0=BoxPack,1=PouchPack
    Badge           NVARCHAR(64)      NULL,
    BgColor         NVARCHAR(10)      NULL,
    ImageUrl        NVARCHAR(500)     NULL,
    Rating          DECIMAL(3,2)      NOT NULL DEFAULT 0,
    ReviewCount     INT               NOT NULL DEFAULT 0,
    InStock         BIT               NOT NULL DEFAULT 1,
    Featured        BIT               NOT NULL DEFAULT 0,
    IsDraft         BIT               NOT NULL DEFAULT 0,
    HsnCode         NVARCHAR(10)      NULL,
    GstRate         DECIMAL(5,2)      NOT NULL DEFAULT 18,
    Usages          NVARCHAR(512)     NULL,        -- JSON array
    SeoTitle        NVARCHAR(256)     NULL,
    SeoDescription  NVARCHAR(512)     NULL,
    -- Nutrition per 100g
    CaloriesPer100g DECIMAL(8,2)      NULL,
    ProteinPer100g  NVARCHAR(20)      NULL,
    FatPer100g      NVARCHAR(20)      NULL,
    CarbsPer100g    NVARCHAR(20)      NULL,
    FiberPer100g    NVARCHAR(20)      NULL,
    IsDeleted       BIT               NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2         NULL
);

CREATE INDEX IX_Products_Slug     ON Products(Slug);
CREATE INDEX IX_Products_Category ON Products(Category);
CREATE INDEX IX_Products_InStock  ON Products(InStock);

-- ── Reviews ────────────────────────────────────────────────
CREATE TABLE Reviews (
    Id                 INT IDENTITY(1,1) PRIMARY KEY,
    UserId             INT       NOT NULL REFERENCES Users(Id),
    ProductId          INT       NOT NULL REFERENCES Products(Id),
    Rating             TINYINT   NOT NULL CHECK(Rating BETWEEN 1 AND 5),
    Title              NVARCHAR(256) NULL,
    Comment            NVARCHAR(MAX) NULL,
    IsVerifiedPurchase BIT       NOT NULL DEFAULT 0,
    HelpfulCount       INT       NOT NULL DEFAULT 0,
    IsApproved         BIT       NOT NULL DEFAULT 1,
    IsDeleted          BIT       NOT NULL DEFAULT 0,
    CreatedAt          DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_Review_UserProduct UNIQUE(UserId, ProductId)
);

-- ── Coupons ────────────────────────────────────────────────
CREATE TABLE Coupons (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Code           NVARCHAR(50)  NOT NULL UNIQUE,
    DiscountType   NVARCHAR(20)  NOT NULL,   -- percentage | flat
    DiscountValue  DECIMAL(10,2) NOT NULL,
    MinCartValue   DECIMAL(10,2) NULL,
    MaxDiscount    DECIMAL(10,2) NULL,
    ExpiresAt      DATETIME2     NULL,
    MaxUses        INT           NULL,
    UsedCount      INT           NOT NULL DEFAULT 0,
    IsActive       BIT           NOT NULL DEFAULT 1,
    IsDeleted      BIT           NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── CartItems ──────────────────────────────────────────────
CREATE TABLE CartItems (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT           NOT NULL REFERENCES Users(Id),
    ProductId   INT           NOT NULL REFERENCES Products(Id),
    Quantity    INT           NOT NULL DEFAULT 1,
    CouponCode  NVARCHAR(50)  NULL,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   DATETIME2     NULL,
    CONSTRAINT UQ_Cart_UserProduct UNIQUE(UserId, ProductId)
);

-- ── WishlistItems ──────────────────────────────────────────
CREATE TABLE WishlistItems (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT       NOT NULL REFERENCES Users(Id),
    ProductId   INT       NOT NULL REFERENCES Products(Id),
    IsDeleted   BIT       NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_Wishlist_UserProduct UNIQUE(UserId, ProductId)
);

-- ── Orders ─────────────────────────────────────────────────
CREATE TABLE Orders (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    OrderNumber         NVARCHAR(20)  NOT NULL UNIQUE,  -- WAH20260610001
    UserId              INT           NOT NULL REFERENCES Users(Id),
    AddressId           INT           NOT NULL REFERENCES Addresses(Id),
    Status              TINYINT       NOT NULL DEFAULT 0,
    PaymentStatus       TINYINT       NOT NULL DEFAULT 0,
    PaymentMethod       TINYINT       NOT NULL,
    CouponCode          NVARCHAR(50)  NULL,
    SubtotalMrp         DECIMAL(10,2) NOT NULL DEFAULT 0,
    Subtotal            DECIMAL(10,2) NOT NULL DEFAULT 0,
    Discount            DECIMAL(10,2) NOT NULL DEFAULT 0,
    CouponDiscount      DECIMAL(10,2) NOT NULL DEFAULT 0,
    DeliveryCharge      DECIMAL(10,2) NOT NULL DEFAULT 0,
    GrandTotal          DECIMAL(10,2) NOT NULL,
    Notes               NVARCHAR(512) NULL,
    Courier             NVARCHAR(100) NULL,
    AwbNumber           NVARCHAR(50)  NULL,
    CourierTrackingUrl  NVARCHAR(500) NULL,
    EstimatedDelivery   DATETIME2     NULL,
    DispatchedAt        DATETIME2     NULL,
    DeliveredAt         DATETIME2     NULL,
    CancellationReason  NVARCHAR(512) NULL,
    ReturnReason        NVARCHAR(512) NULL,
    RazorpayOrderId     NVARCHAR(100) NULL,
    RazorpayPaymentId   NVARCHAR(100) NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    CreatedBy           NVARCHAR(64)  NULL,
    UpdatedBy           NVARCHAR(64)  NULL
);

CREATE INDEX IX_Orders_Status       ON Orders(Status);
CREATE INDEX IX_Orders_UserId       ON Orders(UserId);
CREATE INDEX IX_Orders_CreatedAt    ON Orders(CreatedAt);
CREATE INDEX IX_Orders_OrderNumber  ON Orders(OrderNumber);

-- ── OrderItems ─────────────────────────────────────────────
CREATE TABLE OrderItems (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    OrderId     INT           NOT NULL REFERENCES Orders(Id),
    ProductId   INT           NOT NULL REFERENCES Products(Id),
    ProductName NVARCHAR(256) NOT NULL,
    Weight      NVARCHAR(20)  NOT NULL,
    Price       DECIMAL(10,2) NOT NULL,
    Mrp         DECIMAL(10,2) NOT NULL,
    Quantity    INT           NOT NULL,
    LineTotal   DECIMAL(10,2) NOT NULL,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── Order Status History ───────────────────────────────────
CREATE TABLE OrderStatusHistories (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    OrderId     INT           NOT NULL REFERENCES Orders(Id),
    Status      TINYINT       NOT NULL,
    Note        NVARCHAR(512) NULL,
    UpdatedBy   NVARCHAR(128) NULL,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── Return Orders ─────────────────────────────────────────
--
--  Auto Tax Calculation Logic (stored values, calculated by application):
--    Taxable Value   = SubtotalSelling × 100 / (100 + GstRate)   [GST-inclusive price]
--    CGST            = TaxableValue × CgstRate / 100             [Intra-state only]
--    SGST            = TaxableValue × SgstRate / 100             [Intra-state only]
--    IGST            = TaxableValue × IgstRate / 100             [Inter-state only]
--    TotalGst        = CGST + SGST  (or IGST)
--    TCS (Sec 52)    = TaxableValue × TcsRate / 100              [e-commerce operator]
--    CreditNote      = TaxableValue + TotalGst
--    GrandRefund     = SubtotalSelling + TcsAmount
--
--  GstType:       0=IntraState, 1=InterState, 2=Export
--  ReturnStatus:  0=Requested, 1=Approved, 2=PickupScheduled, 3=PickedUp,
--                 4=Received, 5=QualityCheck, 6=Processed, 7=Refunded,
--                 8=Rejected, 9=Cancelled
--  ReturnType:    0=Refund, 1=Replacement, 2=StoreCredit
--  PriceType:     0=Inclusive, 1=Exclusive
--  RefundMethod:  0=OriginalPaymentMethod, 1=BankTransfer, 2=WalletCredit,
--                 3=StoreCoupon, 4=UPI
CREATE TABLE ReturnOrders (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    ReturnNumber        NVARCHAR(20)  NOT NULL UNIQUE,   -- RET/2026/0001
    OrderId             INT           NOT NULL REFERENCES Orders(Id),
    UserId              INT           NOT NULL REFERENCES Users(Id),
    Status              TINYINT       NOT NULL DEFAULT 0,
    Type                TINYINT       NOT NULL DEFAULT 0,
    Reason              NVARCHAR(512) NOT NULL,
    AdminNotes          NVARCHAR(512) NULL,
    RejectionReason     NVARCHAR(512) NULL,

    -- Pickup / logistics
    PickupAddress       NVARCHAR(512) NULL,
    CourierName         NVARCHAR(100) NULL,
    AwbNumber           NVARCHAR(50)  NULL,
    PickupScheduledAt   DATETIME2     NULL,
    PickedUpAt          DATETIME2     NULL,
    ReceivedAt          DATETIME2     NULL,
    ProcessedAt         DATETIME2     NULL,

    -- GST configuration
    GstType             TINYINT       NOT NULL DEFAULT 0,  -- 0=IntraState,1=InterState,2=Export
    PriceType           TINYINT       NOT NULL DEFAULT 0,  -- 0=Inclusive,1=Exclusive

    -- Amounts
    SubtotalMrp         DECIMAL(12,2) NOT NULL DEFAULT 0,
    SubtotalSelling     DECIMAL(12,2) NOT NULL DEFAULT 0,  -- What customer paid (GST-inclusive)

    -- Auto-calculated tax reversal
    TaxableValue        DECIMAL(12,2) NOT NULL DEFAULT 0,  -- Ex-GST amount
    CgstRate            DECIMAL(5,2)  NOT NULL DEFAULT 0,  -- e.g. 2.50
    SgstRate            DECIMAL(5,2)  NOT NULL DEFAULT 0,  -- e.g. 2.50
    IgstRate            DECIMAL(5,2)  NOT NULL DEFAULT 0,  -- e.g. 5.00
    CgstAmount          DECIMAL(12,2) NOT NULL DEFAULT 0,
    SgstAmount          DECIMAL(12,2) NOT NULL DEFAULT 0,
    IgstAmount          DECIMAL(12,2) NOT NULL DEFAULT 0,
    TotalGstAmount      DECIMAL(12,2) NOT NULL DEFAULT 0,

    -- TCS (Section 52 — 1% collected by e-commerce operator)
    TcsRate             DECIMAL(5,2)  NOT NULL DEFAULT 0,
    TcsAmount           DECIMAL(12,2) NOT NULL DEFAULT 0,

    -- Final amounts
    CreditNoteAmount    DECIMAL(12,2) NOT NULL DEFAULT 0,  -- TaxableValue + TotalGst
    GrandRefundAmount   DECIMAL(12,2) NOT NULL DEFAULT 0,  -- Actual refund to customer

    -- Credit note
    CreditNoteNumber    NVARCHAR(20)  NULL UNIQUE,          -- CN/2026/0001
    CreditNoteDate      DATETIME2     NULL,

    -- Refund tracking
    RefundMethod        TINYINT       NOT NULL DEFAULT 0,
    RefundReferenceId   NVARCHAR(100) NULL,
    RefundInitiatedAt   DATETIME2     NULL,
    RefundCompletedAt   DATETIME2     NULL,

    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    CreatedBy           NVARCHAR(64)  NULL,
    UpdatedBy           NVARCHAR(64)  NULL
);

CREATE INDEX IX_ReturnOrders_OrderId    ON ReturnOrders(OrderId);
CREATE INDEX IX_ReturnOrders_UserId     ON ReturnOrders(UserId);
CREATE INDEX IX_ReturnOrders_Status     ON ReturnOrders(Status);
CREATE INDEX IX_ReturnOrders_CreatedAt  ON ReturnOrders(CreatedAt);

-- ── Return Items ───────────────────────────────────────────
--
--  Per-item tax breakdown stored for credit note / GSTR reporting.
--  ReturnItemCondition: 0=Unopened, 1=Opened, 2=Damaged,
--                       3=WrongProduct, 4=Expired, 5=DefectivePacking
CREATE TABLE ReturnItems (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    ReturnOrderId   INT           NOT NULL REFERENCES ReturnOrders(Id) ON DELETE CASCADE,
    OrderItemId     INT           NOT NULL REFERENCES OrderItems(Id),
    ProductId       INT           NOT NULL REFERENCES Products(Id),

    ProductName     NVARCHAR(256) NOT NULL,
    Weight          NVARCHAR(20)  NOT NULL,
    HsnCode         NVARCHAR(10)  NOT NULL DEFAULT '',

    -- Quantity & price
    Quantity        INT           NOT NULL,
    UnitPrice       DECIMAL(10,2) NOT NULL,   -- Selling price per unit
    UnitMrp         DECIMAL(10,2) NOT NULL,
    LineTotal       DECIMAL(10,2) NOT NULL,   -- UnitPrice × Quantity

    -- Per-item auto-calculated tax
    GstRate         DECIMAL(5,2)  NOT NULL DEFAULT 0,
    TaxableValue    DECIMAL(10,2) NOT NULL DEFAULT 0,  -- LineTotal ex-GST
    CgstRate        DECIMAL(5,2)  NOT NULL DEFAULT 0,
    SgstRate        DECIMAL(5,2)  NOT NULL DEFAULT 0,
    IgstRate        DECIMAL(5,2)  NOT NULL DEFAULT 0,
    CgstAmount      DECIMAL(10,2) NOT NULL DEFAULT 0,
    SgstAmount      DECIMAL(10,2) NOT NULL DEFAULT 0,
    IgstAmount      DECIMAL(10,2) NOT NULL DEFAULT 0,
    TotalTax        DECIMAL(10,2) NOT NULL DEFAULT 0,
    LineTotalWithTax DECIMAL(10,2) NOT NULL DEFAULT 0,

    ReturnReason    NVARCHAR(256) NOT NULL DEFAULT '',
    Condition       TINYINT       NOT NULL DEFAULT 0,
    IsApproved      BIT           NOT NULL DEFAULT 1,

    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_ReturnItems_ReturnOrderId ON ReturnItems(ReturnOrderId);
CREATE INDEX IX_ReturnItems_ProductId     ON ReturnItems(ProductId);

-- ── Blog Posts ─────────────────────────────────────────────
CREATE TABLE BlogPosts (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Title          NVARCHAR(512)  NOT NULL,
    Slug           NVARCHAR(512)  NOT NULL UNIQUE,
    Excerpt        NVARCHAR(1024) NOT NULL,
    Content        NVARCHAR(MAX)  NOT NULL,
    Category       NVARCHAR(50)   NOT NULL,
    Author         NVARCHAR(128)  NOT NULL,
    AuthorAvatar   NVARCHAR(500)  NULL,
    ImageUrl       NVARCHAR(500)  NULL,
    BgColor        NVARCHAR(10)   NULL,
    ReadTime       NVARCHAR(30)   NOT NULL DEFAULT '5 min read',
    Tags           NVARCHAR(512)  NULL,
    Masala         NVARCHAR(128)  NULL,
    Featured       BIT            NOT NULL DEFAULT 0,
    IsPublished    BIT            NOT NULL DEFAULT 1,
    PublishedAt    DATETIME2      NULL,
    SeoTitle       NVARCHAR(256)  NULL,
    SeoDescription NVARCHAR(512)  NULL,
    IsDeleted      BIT            NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt      DATETIME2      NULL
);

-- ── OTP Records ────────────────────────────────────────────
CREATE TABLE OtpRecords (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Phone     NVARCHAR(15) NOT NULL,
    Otp       NVARCHAR(6)  NOT NULL,
    Purpose   NVARCHAR(30) NOT NULL DEFAULT 'login',
    ExpiresAt DATETIME2    NOT NULL,
    IsUsed    BIT          NOT NULL DEFAULT 0,
    IsDeleted BIT          NOT NULL DEFAULT 0,
    CreatedAt DATETIME2    NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_OtpRecords_Phone ON OtpRecords(Phone);

-- ── Newsletter ─────────────────────────────────────────────
CREATE TABLE NewsletterSubscribers (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Email      NVARCHAR(256) NOT NULL UNIQUE,
    IsActive   BIT           NOT NULL DEFAULT 1,
    CouponCode NVARCHAR(50)  NULL,
    IsDeleted  BIT           NOT NULL DEFAULT 0,
    CreatedAt  DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── Contact Messages ───────────────────────────────────────
CREATE TABLE ContactMessages (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Name       NVARCHAR(128)  NOT NULL,
    Email      NVARCHAR(256)  NOT NULL,
    Phone      NVARCHAR(15)   NULL,
    Subject    NVARCHAR(256)  NOT NULL,
    Message    NVARCHAR(MAX)  NOT NULL,
    TicketId   NVARCHAR(30)   NULL,
    IsResolved BIT            NOT NULL DEFAULT 0,
    IsDeleted  BIT            NOT NULL DEFAULT 0,
    CreatedAt  DATETIME2      NOT NULL DEFAULT GETUTCDATE()
);

-- ── Feedback ───────────────────────────────────────────────
CREATE TABLE Feedbacks (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(128) NULL,
    Email     NVARCHAR(256) NULL,
    Product   NVARCHAR(128) NULL,
    Rating    TINYINT       NOT NULL,
    Message   NVARCHAR(MAX) NOT NULL,
    Recommend NVARCHAR(50)  NULL,
    IsDeleted BIT           NOT NULL DEFAULT 0,
    CreatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ============================================================
--  SECTION 2 — ADMIN / ERP TABLES
-- ============================================================

-- ── Admin Users ────────────────────────────────────────────
CREATE TABLE AdminUsers (
    Id                   INT IDENTITY(1,1) PRIMARY KEY,
    Name                 NVARCHAR(128) NOT NULL,
    Email                NVARCHAR(256) NOT NULL UNIQUE,
    PasswordHash         NVARCHAR(512) NOT NULL,
    Role                 NVARCHAR(50)  NOT NULL DEFAULT 'Manager',
    Permissions          NVARCHAR(MAX) NULL,   -- JSON permissions object
    IsActive             BIT           NOT NULL DEFAULT 1,
    IsTwoFactorEnabled   BIT           NOT NULL DEFAULT 0,
    RefreshToken         NVARCHAR(512) NULL,
    RefreshTokenExpiry   DATETIME2     NULL,
    LastLoginAt          DATETIME2     NULL,
    IsDeleted            BIT           NOT NULL DEFAULT 0,
    CreatedAt            DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt            DATETIME2     NULL
);

-- Default Super Admin (password: admin123)
INSERT INTO AdminUsers (Name, Email, PasswordHash, Role, IsActive)
VALUES ('Admin', 'admin@wahspices.com',
        '$2a$11$rj.oVWb7UOjP.5HRnG9TU.N7cQlTjRFjEkbvkczRmVYgd8bD0mApe',
        'Admin', 1);

-- ── Audit Logs ─────────────────────────────────────────────
CREATE TABLE AuditLogs (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      NVARCHAR(50)  NOT NULL,
    UserName    NVARCHAR(128) NOT NULL,
    Module      NVARCHAR(64)  NOT NULL,
    Action      NVARCHAR(64)  NOT NULL,
    Details     NVARCHAR(MAX) NULL,
    IpAddress   NVARCHAR(50)  NULL,
    UserAgent   NVARCHAR(256) NULL,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_AuditLogs_CreatedAt ON AuditLogs(CreatedAt);
CREATE INDEX IX_AuditLogs_Module    ON AuditLogs(Module);

-- ── Inventory Items ────────────────────────────────────────
CREATE TABLE InventoryItems (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    ProductId      INT           NOT NULL REFERENCES Products(Id),
    Sku            NVARCHAR(50)  NOT NULL UNIQUE,
    CurrentStock   INT           NOT NULL DEFAULT 0,
    ReorderPoint   INT           NOT NULL DEFAULT 50,
    BatchNumber    NVARCHAR(50)  NULL,
    ExpiryDate     DATETIME2     NULL,
    ManufactureDate DATETIME2    NULL,
    Location       NVARCHAR(50)  NOT NULL DEFAULT 'Warehouse A',
    CostPrice      DECIMAL(10,2) NOT NULL DEFAULT 0,
    Unit           NVARCHAR(10)  NOT NULL DEFAULT 'pcs',
    IsDeleted      BIT           NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt      DATETIME2     NULL,
    UpdatedBy      NVARCHAR(64)  NULL
);

CREATE INDEX IX_Inventory_ProductId ON InventoryItems(ProductId);

-- ── Inventory Ledger ───────────────────────────────────────
CREATE TABLE InventoryLedgers (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    InventoryItemId INT           NOT NULL REFERENCES InventoryItems(Id),
    Type            TINYINT       NOT NULL,  -- 0=Addition,1=Reduction,2=Damage,3=Return,4=Opening
    Quantity        INT           NOT NULL,
    BalanceAfter    INT           NOT NULL,
    Reason          NVARCHAR(256) NULL,
    BatchNumber     NVARCHAR(50)  NULL,
    ReferenceId     NVARCHAR(50)  NULL,
    PerformedBy     NVARCHAR(128) NOT NULL,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── Vendors ────────────────────────────────────────────────
CREATE TABLE Vendors (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(256) NOT NULL,
    Gstin           NVARCHAR(20)  NULL,
    City            NVARCHAR(100) NOT NULL,
    State           NVARCHAR(100) NULL,
    Address         NVARCHAR(512) NULL,
    Phone           NVARCHAR(15)  NOT NULL,
    Email           NVARCHAR(256) NULL,
    BankAccountNo   NVARCHAR(20)  NULL,
    BankIfsc        NVARCHAR(15)  NULL,
    BankName        NVARCHAR(100) NULL,
    Rating          DECIMAL(3,2)  NOT NULL DEFAULT 5,
    OutstandingDues DECIMAL(12,2) NOT NULL DEFAULT 0,
    IsActive        BIT           NOT NULL DEFAULT 1,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2     NULL
);

-- ── Purchase Orders ────────────────────────────────────────
CREATE TABLE PurchaseOrders (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    PoNumber      NVARCHAR(30)  NOT NULL UNIQUE,
    VendorId      INT           NOT NULL REFERENCES Vendors(Id),
    TotalAmount   DECIMAL(12,2) NOT NULL,
    Status        TINYINT       NOT NULL DEFAULT 0,   -- 0=Draft,1=Pending,2=Approved,3=Received
    Items         NVARCHAR(MAX) NULL,
    Notes         NVARCHAR(512) NULL,
    ExpectedDate  DATETIME2     NULL,
    ApprovedBy    NVARCHAR(128) NULL,
    ApprovedAt    DATETIME2     NULL,
    IsGrnDone     BIT           NOT NULL DEFAULT 0,
    IsDeleted     BIT           NOT NULL DEFAULT 0,
    CreatedAt     DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt     DATETIME2     NULL
);

-- ── Purchase Order Items ───────────────────────────────────
CREATE TABLE PurchaseOrderItems (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId     INT           NOT NULL REFERENCES PurchaseOrders(Id),
    ProductName         NVARCHAR(256) NOT NULL,
    HsnCode             NVARCHAR(20)  NULL,
    Quantity            INT           NOT NULL DEFAULT 0,
    UnitPrice           DECIMAL(18,2) NOT NULL DEFAULT 0,
    Unit                NVARCHAR(20)  NOT NULL DEFAULT 'kg',
    TotalAmount         DECIMAL(18,2) NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(128) NULL,
    UpdatedBy           NVARCHAR(128) NULL
);

CREATE TABLE GoodsReceiptNotes (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId     INT           NOT NULL REFERENCES PurchaseOrders(Id),
    GrnNumber           NVARCHAR(50)  NOT NULL,
    VendorInvoiceNo     NVARCHAR(100) NULL,
    InvoiceAmount       DECIMAL(18,2) NOT NULL DEFAULT 0,
    Notes               NVARCHAR(500) NULL,
    ReceivedAt          DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(128) NULL,
    UpdatedBy           NVARCHAR(128) NULL
);

CREATE TABLE GrnItems (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    GrnId               INT           NOT NULL REFERENCES GoodsReceiptNotes(Id),
    ProductName         NVARCHAR(256) NOT NULL,
    HsnCode             NVARCHAR(20)  NULL,
    OrderedQty          INT           NOT NULL DEFAULT 0,
    ReceivedQty         INT           NOT NULL DEFAULT 0,
    UnitPrice           DECIMAL(18,2) NOT NULL DEFAULT 0,
    Unit                NVARCHAR(20)  NOT NULL DEFAULT 'kg',
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(128) NULL,
    UpdatedBy           NVARCHAR(128) NULL
);

CREATE TABLE VendorPayments (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    VendorId            INT           NOT NULL REFERENCES Vendors(Id),
    PurchaseOrderId     INT           NULL REFERENCES PurchaseOrders(Id),
    PaymentRef          NVARCHAR(50)  NOT NULL,
    Amount              DECIMAL(18,2) NOT NULL,
    Mode                NVARCHAR(20)  NOT NULL DEFAULT 'NEFT',
    ReferenceNo         NVARCHAR(100) NULL,
    PaidAt              DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    Notes               NVARCHAR(500) NULL,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(128) NULL,
    UpdatedBy           NVARCHAR(128) NULL
);

CREATE TABLE VendorPriceHistories (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    VendorId            INT           NOT NULL REFERENCES Vendors(Id),
    ProductName         NVARCHAR(256) NOT NULL,
    HsnCode             NVARCHAR(20)  NULL,
    UnitPrice           DECIMAL(18,2) NOT NULL,
    Unit                NVARCHAR(20)  NOT NULL DEFAULT 'kg',
    EffectiveFrom       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(128) NULL,
    UpdatedBy           NVARCHAR(128) NULL
);

CREATE TABLE PurchaseReturns (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId     INT           NOT NULL REFERENCES PurchaseOrders(Id),
    ReturnNumber        NVARCHAR(50)  NOT NULL,
    Reason              NVARCHAR(500) NOT NULL,
    Amount              DECIMAL(18,2) NOT NULL,
    Status              NVARCHAR(20)  NOT NULL DEFAULT 'Pending',
    Notes               NVARCHAR(500) NULL,
    CreatedAt           DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2     NULL,
    IsDeleted           BIT           NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(128) NULL,
    UpdatedBy           NVARCHAR(128) NULL
);

-- ── Invoices ───────────────────────────────────────────────
CREATE TABLE Invoices (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    InvoiceNumber  NVARCHAR(30)  NOT NULL UNIQUE,   -- WAH/2026/049
    OrderId        INT           NULL REFERENCES Orders(Id),
    CustomerId     NVARCHAR(20)  NULL,
    CustomerName   NVARCHAR(256) NOT NULL,
    CustomerGstin  NVARCHAR(20)  NULL,
    Type           TINYINT       NOT NULL DEFAULT 0,  -- 0=B2C,1=B2B
    Status         TINYINT       NOT NULL DEFAULT 0,  -- 0=Unpaid,1=Paid...
    Subtotal       DECIMAL(12,2) NOT NULL,
    CgstAmount     DECIMAL(12,2) NOT NULL DEFAULT 0,
    SgstAmount     DECIMAL(12,2) NOT NULL DEFAULT 0,
    IgstAmount     DECIMAL(12,2) NOT NULL DEFAULT 0,
    TotalAmount    DECIMAL(12,2) NOT NULL,
    GstType        TINYINT       NOT NULL DEFAULT 0,  -- 0=IntraState,1=InterState
    Irn            NVARCHAR(128) NULL,
    IrnAckNo       NVARCHAR(30)  NULL,
    IrnAckDate     DATETIME2     NULL,
    QrCodeUrl      NVARCHAR(500) NULL,
    PdfUrl         NVARCHAR(500) NULL,
    Items          NVARCHAR(MAX) NULL,
    Notes          NVARCHAR(512) NULL,
    IsDeleted      BIT           NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt      DATETIME2     NULL
);

-- ── HSN Codes ──────────────────────────────────────────────
CREATE TABLE HsnCodes (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Code        NVARCHAR(10)  NOT NULL UNIQUE,
    Description NVARCHAR(512) NOT NULL,
    GstRate     DECIMAL(5,2)  NOT NULL,
    IsActive    BIT           NOT NULL DEFAULT 1,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

INSERT INTO HsnCodes (Code, Description, GstRate) VALUES
    ('0910',  'Ginger, Saffron, Turmeric (Curcuma), Thyme, Bay Leaves, Curry', 5.00),
    ('09109', 'Other Spices: Cumin, Coriander, Cardamom, Fenugreek',          5.00),
    ('21039', 'Mixed Condiments & Mixed Seasonings (Masala Blends)',           18.00),
    ('09101', 'Ginger — Dried',                                                5.00),
    ('09103', 'Turmeric (Curcuma)',                                            5.00),
    ('09105', 'Curry Powder and Paste',                                        18.00);

-- ── ITC Entries ────────────────────────────────────────────
CREATE TABLE ItcEntries (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Period          NVARCHAR(20)  NOT NULL,
    SupplierName    NVARCHAR(256) NOT NULL,
    SupplierGstin   NVARCHAR(20)  NULL,
    InvoiceNumber   NVARCHAR(50)  NOT NULL,
    TaxableAmount   DECIMAL(12,2) NOT NULL,
    IgstAmount      DECIMAL(12,2) NOT NULL DEFAULT 0,
    CgstAmount      DECIMAL(12,2) NOT NULL DEFAULT 0,
    SgstAmount      DECIMAL(12,2) NOT NULL DEFAULT 0,
    TotalItc        DECIMAL(12,2) NOT NULL,
    IsEligible      BIT           NOT NULL DEFAULT 1,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── CRM Customers ──────────────────────────────────────────
CREATE TABLE CrmCustomers (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(256) NOT NULL,
    Email           NVARCHAR(256) NULL,
    Phone           NVARCHAR(15)  NOT NULL,
    City            NVARCHAR(100) NOT NULL,
    Category        NVARCHAR(20)  NOT NULL DEFAULT 'Retail',
    Gstin           NVARCHAR(20)  NULL,
    CreditLimit     DECIMAL(12,2) NOT NULL DEFAULT 0,
    OutstandingDues DECIMAL(12,2) NOT NULL DEFAULT 0,
    LoyaltyPoints   INT           NOT NULL DEFAULT 0,
    IsActive        BIT           NOT NULL DEFAULT 1,
    LastOrderDate   DATETIME2     NULL,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2     NULL
);

-- ── Expense Entries ────────────────────────────────────────
CREATE TABLE ExpenseEntries (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Date        DATETIME2     NOT NULL,
    Category    TINYINT       NOT NULL,
    Description NVARCHAR(512) NOT NULL,
    Amount      DECIMAL(12,2) NOT NULL,
    PaidBy      NVARCHAR(50)  NOT NULL,
    IsApproved  BIT           NOT NULL DEFAULT 0,
    ApprovedBy  NVARCHAR(128) NULL,
    ReceiptUrl  NVARCHAR(500) NULL,
    Notes       NVARCHAR(512) NULL,
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   DATETIME2     NULL
);

-- ── Employees ──────────────────────────────────────────────
CREATE TABLE Employees (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId      NVARCHAR(15)  NOT NULL UNIQUE,  -- EMP-001
    Name            NVARCHAR(128) NOT NULL,
    Email           NVARCHAR(256) NULL,
    Phone           NVARCHAR(15)  NOT NULL,
    Department      NVARCHAR(64)  NOT NULL,
    Designation     NVARCHAR(128) NOT NULL,
    EmploymentType  TINYINT       NOT NULL DEFAULT 0,
    Salary          DECIMAL(12,2) NOT NULL,
    JoinedAt        DATETIME2     NOT NULL,
    ConfirmationDate DATETIME2    NULL,
    IsActive        BIT           NOT NULL DEFAULT 1,
    AadharNumber    NVARCHAR(12)  NULL,
    PanNumber       NVARCHAR(10)  NULL,
    BankAccountNo   NVARCHAR(20)  NULL,
    BankIfsc        NVARCHAR(15)  NULL,
    PfNumber        NVARCHAR(22)  NULL,
    EsicNumber      NVARCHAR(20)  NULL,
    ReportingManagerId NVARCHAR(15) NULL,
    PhotoUrl        NVARCHAR(500) NULL,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2     NULL
);

-- ── Attendance Records ─────────────────────────────────────
CREATE TABLE AttendanceRecords (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId     INT       NOT NULL REFERENCES Employees(Id),
    Date           DATE      NOT NULL,
    Status         TINYINT   NOT NULL DEFAULT 0,  -- 0=Present,1=Absent...
    InTime         TIME(0)   NULL,
    OutTime        TIME(0)   NULL,
    OvertimeHours  DECIMAL(4,2) NOT NULL DEFAULT 0,
    Remark         NVARCHAR(256) NULL,
    MarkedBy       NVARCHAR(128) NOT NULL,
    IsDeleted      BIT       NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt      DATETIME2 NULL,
    CONSTRAINT UQ_Attendance_EmpDate UNIQUE(EmployeeId, Date)
);

CREATE INDEX IX_Attendance_EmployeeId ON AttendanceRecords(EmployeeId);
CREATE INDEX IX_Attendance_Date       ON AttendanceRecords(Date);

-- ── Leave Applications ─────────────────────────────────────
CREATE TABLE LeaveApplications (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId      INT           NOT NULL REFERENCES Employees(Id),
    LeaveType       TINYINT       NOT NULL,
    FromDate        DATE          NOT NULL,
    ToDate          DATE          NOT NULL,
    TotalDays       INT           NOT NULL,
    Reason          NVARCHAR(512) NOT NULL,
    Status          TINYINT       NOT NULL DEFAULT 0,  -- 0=Pending,1=Approved,2=Rejected
    ApprovedBy      NVARCHAR(128) NULL,
    RejectionReason NVARCHAR(256) NULL,
    ActionAt        DATETIME2     NULL,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- ── Leave Balances ─────────────────────────────────────────
CREATE TABLE LeaveBalances (
    Id                     INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId             INT NOT NULL REFERENCES Employees(Id),
    Year                   INT NOT NULL,
    CasualLeaveBalance     INT NOT NULL DEFAULT 12,
    EarnedLeaveBalance     INT NOT NULL DEFAULT 15,
    SickLeaveBalance       INT NOT NULL DEFAULT 7,
    MaternityLeaveBalance  INT NOT NULL DEFAULT 0,
    PaternityLeaveBalance  INT NOT NULL DEFAULT 0,
    UsedCasual             INT NOT NULL DEFAULT 0,
    UsedEarned             INT NOT NULL DEFAULT 0,
    UsedSick               INT NOT NULL DEFAULT 0,
    IsDeleted              BIT NOT NULL DEFAULT 0,
    CreatedAt              DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_LeaveBalance_EmpYear UNIQUE(EmployeeId, Year)
);

-- ── Payroll Entries ────────────────────────────────────────
CREATE TABLE PayrollEntries (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId      INT           NOT NULL REFERENCES Employees(Id),
    Month           TINYINT       NOT NULL,
    Year            INT           NOT NULL,
    DaysWorked      INT           NOT NULL DEFAULT 0,
    LossOfPayDays   INT           NOT NULL DEFAULT 0,
    BasicSalary     DECIMAL(12,2) NOT NULL,
    Hra             DECIMAL(12,2) NOT NULL DEFAULT 0,
    Ta              DECIMAL(12,2) NOT NULL DEFAULT 0,
    Da              DECIMAL(12,2) NOT NULL DEFAULT 0,
    Bonus           DECIMAL(12,2) NOT NULL DEFAULT 0,
    OvertimePay     DECIMAL(12,2) NOT NULL DEFAULT 0,
    GrossSalary     DECIMAL(12,2) NOT NULL,
    Epf             DECIMAL(12,2) NOT NULL DEFAULT 0,
    Esic            DECIMAL(12,2) NOT NULL DEFAULT 0,
    ProfessionalTax DECIMAL(8,2)  NOT NULL DEFAULT 0,
    Tds             DECIMAL(12,2) NOT NULL DEFAULT 0,
    OtherDeductions DECIMAL(12,2) NOT NULL DEFAULT 0,
    NetSalary       DECIMAL(12,2) NOT NULL,
    IsApproved      BIT           NOT NULL DEFAULT 0,
    IsDisbursed     BIT           NOT NULL DEFAULT 0,
    DisbursedAt     DATETIME2     NULL,
    SlipUrl         NVARCHAR(500) NULL,
    IsDeleted       BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2     NULL,
    UpdatedBy       NVARCHAR(64)  NULL,
    CONSTRAINT UQ_Payroll_EmpMonthYear UNIQUE(EmployeeId, Month, Year)
);

-- ── Holidays ───────────────────────────────────────────────
CREATE TABLE Holidays (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Date      DATE          NOT NULL,
    Name      NVARCHAR(128) NOT NULL,
    Type      NVARCHAR(20)  NOT NULL DEFAULT 'National',
    Year      INT           NOT NULL,
    IsDeleted BIT           NOT NULL DEFAULT 0,
    CreatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);

-- Seed holidays for 2026
INSERT INTO Holidays (Date, Name, Type, Year) VALUES
    ('2026-01-26', 'Republic Day',         'National',  2026),
    ('2026-03-08', 'Holi',                 'Optional',  2026),
    ('2026-04-14', 'Ambedkar Jayanti',     'National',  2026),
    ('2026-05-01', 'Labour Day',           'Optional',  2026),
    ('2026-08-15', 'Independence Day',     'National',  2026),
    ('2026-10-02', 'Gandhi Jayanti',       'National',  2026),
    ('2026-10-20', 'Diwali',               'Festival',  2026),
    ('2026-12-25', 'Christmas',            'Optional',  2026);

-- ============================================================
--  SECTION 3 — USEFUL VIEWS
-- ============================================================
GO

-- Sales summary view
CREATE VIEW vw_DailySales AS
SELECT
    CAST(o.CreatedAt AS DATE) AS SaleDate,
    COUNT(*)                  AS OrderCount,
    SUM(o.GrandTotal)         AS Revenue,
    SUM(o.Discount + o.CouponDiscount) AS TotalDiscount
FROM Orders o
WHERE o.PaymentStatus = 2  -- Paid
  AND o.IsDeleted = 0
GROUP BY CAST(o.CreatedAt AS DATE);

GO

-- Low stock alert view
CREATE VIEW vw_LowStockAlert AS
SELECT
    i.Id,
    i.Sku,
    p.Name  AS ProductName,
    i.CurrentStock,
    i.ReorderPoint,
    i.Location,
    i.CostPrice,
    i.CurrentStock * i.CostPrice AS StockValue
FROM InventoryItems i
JOIN Products p ON p.Id = i.ProductId
WHERE i.CurrentStock <= i.ReorderPoint
  AND i.IsDeleted = 0
  AND p.IsDeleted = 0;

GO

-- Return GST credit note summary (monthly — for GSTR filing)
CREATE VIEW vw_ReturnGstSummary AS
SELECT
    YEAR(r.CreatedAt)               AS Year,
    MONTH(r.CreatedAt)              AS Month,
    COUNT(*)                        AS TotalReturns,
    SUM(r.SubtotalSelling)          AS TotalSellingAmount,
    SUM(r.TaxableValue)             AS TotalTaxableValue,
    SUM(r.CgstAmount)               AS TotalCgst,
    SUM(r.SgstAmount)               AS TotalSgst,
    SUM(r.IgstAmount)               AS TotalIgst,
    SUM(r.TotalGstAmount)           AS TotalGstReversed,
    SUM(r.TcsAmount)                AS TotalTcsReturned,
    SUM(r.CreditNoteAmount)         AS TotalCreditNoteValue,
    SUM(r.GrandRefundAmount)        AS TotalRefundPaid,
    COUNT(CASE WHEN r.GstType = 0 THEN 1 END) AS IntraStateCount,
    COUNT(CASE WHEN r.GstType = 1 THEN 1 END) AS InterStateCount
FROM ReturnOrders r
WHERE r.Status = 7          -- 7 = Refunded
  AND r.IsDeleted = 0
GROUP BY YEAR(r.CreatedAt), MONTH(r.CreatedAt);

GO

-- Pending refunds tracker
CREATE VIEW vw_PendingRefunds AS
SELECT
    r.Id,
    r.ReturnNumber,
    o.OrderNumber,
    u.Name          AS CustomerName,
    u.Phone         AS CustomerPhone,
    r.Status,
    r.Type,
    r.GrandRefundAmount,
    r.CreditNoteAmount,
    r.TotalGstAmount,
    r.TaxableValue,
    r.CgstAmount,
    r.SgstAmount,
    r.IgstAmount,
    r.TcsAmount,
    r.GstType,
    r.RefundMethod,
    r.CreatedAt     AS RequestedAt,
    r.ReceivedAt,
    DATEDIFF(DAY, r.CreatedAt, GETUTCDATE()) AS DaysOpen
FROM ReturnOrders r
JOIN Orders o ON o.Id = r.OrderId
JOIN Users  u ON u.Id = r.UserId
WHERE r.Status NOT IN (7, 8, 9)   -- Exclude Refunded, Rejected, Cancelled
  AND r.IsDeleted = 0;

GO

-- Monthly payroll summary view
CREATE VIEW vw_PayrollSummary AS
SELECT
    pe.Month,
    pe.Year,
    COUNT(pe.EmployeeId) AS EmployeeCount,
    SUM(pe.GrossSalary)  AS TotalGross,
    SUM(pe.Epf)          AS TotalEPF,
    SUM(pe.Esic)         AS TotalESIC,
    SUM(pe.ProfessionalTax) AS TotalPT,
    SUM(pe.Tds)          AS TotalTDS,
    SUM(pe.NetSalary)    AS TotalNet,
    MAX(CASE WHEN pe.IsApproved = 1 THEN 1 ELSE 0 END) AS IsApproved
FROM PayrollEntries pe
WHERE pe.IsDeleted = 0
GROUP BY pe.Month, pe.Year;

-- ============================================================
--  SECTION 4 — STORED PROCEDURES
-- ============================================================

GO

-- Generate next order number
CREATE PROCEDURE sp_GetNextOrderNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @prefix NVARCHAR(3) = 'WAH';
    DECLARE @date NVARCHAR(8) = CONVERT(NVARCHAR(8), GETDATE(), 112);
    DECLARE @seq INT;

    SELECT @seq = ISNULL(MAX(CAST(RIGHT(OrderNumber, 4) AS INT)), 0) + 1
    FROM Orders
    WHERE LEFT(OrderNumber, 11) = @prefix + @date;

    SELECT @prefix + @date + RIGHT('0000' + CAST(@seq AS NVARCHAR), 4) AS OrderNumber;
END;

GO

-- Generate invoice number
CREATE PROCEDURE sp_GetNextInvoiceNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @year NVARCHAR(4) = CAST(YEAR(GETDATE()) AS NVARCHAR(4));
    DECLARE @seq INT;

    SELECT @seq = ISNULL(MAX(CAST(RIGHT(InvoiceNumber, 4) AS INT)), 0) + 1
    FROM Invoices
    WHERE InvoiceNumber LIKE 'WAH/' + @year + '/%';

    SELECT 'WAH/' + @year + '/' + RIGHT('000' + CAST(@seq AS NVARCHAR), 3) AS InvoiceNumber;
END;

GO

-- Generate next return number  →  RET/2026/0001
CREATE PROCEDURE sp_GetNextReturnNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @year NVARCHAR(4) = CAST(YEAR(GETDATE()) AS NVARCHAR(4));
    DECLARE @seq INT;

    SELECT @seq = ISNULL(COUNT(*), 0) + 1
    FROM ReturnOrders
    WHERE ReturnNumber LIKE 'RET/' + @year + '/%';

    SELECT 'RET/' + @year + '/' + RIGHT('0000' + CAST(@seq AS NVARCHAR), 4) AS ReturnNumber;
END;

GO

-- Generate next credit note number  →  CN/2026/0001
CREATE PROCEDURE sp_GetNextCreditNoteNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @year NVARCHAR(4) = CAST(YEAR(GETDATE()) AS NVARCHAR(4));
    DECLARE @seq INT;

    SELECT @seq = ISNULL(COUNT(*), 0) + 1
    FROM ReturnOrders
    WHERE CreditNoteNumber LIKE 'CN/' + @year + '/%'
      AND CreditNoteNumber IS NOT NULL;

    SELECT 'CN/' + @year + '/' + RIGHT('0000' + CAST(@seq AS NVARCHAR), 4) AS CreditNoteNumber;
END;

GO

-- Get full tax breakdown for a return (used by credit note PDF generation)
CREATE PROCEDURE sp_GetReturnTaxBreakdown
    @ReturnId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Header
    SELECT
        r.ReturnNumber,
        r.CreditNoteNumber,
        r.CreditNoteDate,
        o.OrderNumber,
        u.Name              AS CustomerName,
        u.Phone             AS CustomerPhone,
        a.State             AS CustomerState,
        r.GstType,
        r.PriceType,
        r.SubtotalMrp,
        r.SubtotalSelling,
        r.TaxableValue,
        r.CgstRate,
        r.SgstRate,
        r.IgstRate,
        r.CgstAmount,
        r.SgstAmount,
        r.IgstAmount,
        r.TotalGstAmount,
        r.TcsRate,
        r.TcsAmount,
        r.CreditNoteAmount,
        r.GrandRefundAmount,
        r.RefundMethod,
        r.Status,
        r.CreatedAt         AS ReturnDate
    FROM ReturnOrders r
    JOIN Orders   o ON o.Id = r.OrderId
    JOIN Users    u ON u.Id = r.UserId
    JOIN Addresses a ON a.Id = o.AddressId
    WHERE r.Id = @ReturnId
      AND r.IsDeleted = 0;

    -- Line items
    SELECT
        ri.ProductName,
        ri.HsnCode,
        ri.Weight,
        ri.Quantity,
        ri.UnitPrice,
        ri.UnitMrp,
        ri.LineTotal,
        ri.GstRate,
        ri.TaxableValue,
        ri.CgstRate,
        ri.SgstRate,
        ri.IgstRate,
        ri.CgstAmount,
        ri.SgstAmount,
        ri.IgstAmount,
        ri.TotalTax,
        ri.LineTotalWithTax,
        ri.Condition,
        ri.ReturnReason,
        ri.IsApproved
    FROM ReturnItems ri
    WHERE ri.ReturnOrderId = @ReturnId
      AND ri.IsDeleted = 0
    ORDER BY ri.Id;
END;

-- ============================================================
--  SECTION 5 — INDEXES FOR PERFORMANCE
-- ============================================================

CREATE INDEX IX_OrderItems_OrderId    ON OrderItems(OrderId);
CREATE INDEX IX_OrderItems_ProductId  ON OrderItems(ProductId);
CREATE INDEX IX_CartItems_UserId      ON CartItems(UserId);
CREATE INDEX IX_WishlistItems_UserId  ON WishlistItems(UserId);
CREATE INDEX IX_Reviews_ProductId     ON Reviews(ProductId);
CREATE INDEX IX_Reviews_UserId        ON Reviews(UserId);
CREATE INDEX IX_BlogPosts_Slug        ON BlogPosts(Slug);
CREATE INDEX IX_BlogPosts_Category    ON BlogPosts(Category);
CREATE INDEX IX_Invoices_OrderId      ON Invoices(OrderId);
CREATE INDEX IX_Invoices_Status       ON Invoices(Status);
CREATE INDEX IX_PayrollEntries_EmpId  ON PayrollEntries(EmployeeId);
CREATE INDEX IX_LeaveApp_EmpId        ON LeaveApplications(EmployeeId);
CREATE INDEX IX_LeaveApp_Status       ON LeaveApplications(Status);

PRINT 'WahSpicesDB schema created successfully. Total tables: 32 | Views: 5 | Stored Procs: 5';
GO


-- ============================================================
--  SECTION 6 — PAYMENT & REFUND TABLES (Razorpay Integration)
-- ============================================================

-- Payments — one row per Razorpay order attempt
CREATE TABLE Payments (
    Id                  INT            IDENTITY(1,1) PRIMARY KEY,
    OrderId             INT            NOT NULL REFERENCES Orders(Id),

    -- Razorpay identifiers
    RazorpayOrderId     NVARCHAR(100)  NOT NULL,     -- order_xxxxx
    RazorpayPaymentId   NVARCHAR(100)  NULL,          -- pay_xxxxx (filled after success)
    RazorpaySignature   NVARCHAR(255)  NULL,          -- HMAC-SHA256 for verification

    Amount              DECIMAL(18,2)  NOT NULL,      -- in INR
    Currency            NVARCHAR(10)   NOT NULL DEFAULT 'INR',

    -- Status: 0=Pending 1=Initiated 2=Paid 3=Failed 4=Refunded 5=PartiallyRefunded
    Status              INT            NOT NULL DEFAULT 0,
    -- Method: 0=UPI 1=Card 2=NetBanking 3=COD 4=Wallet
    Method              INT            NOT NULL DEFAULT 0,

    -- Error details when payment fails
    ErrorCode           NVARCHAR(50)   NULL,
    ErrorDescription    NVARCHAR(500)  NULL,
    Notes               NVARCHAR(500)  NULL,

    -- Audit
    CreatedAt           DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2      NULL,
    IsDeleted           BIT            NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(100)  NULL,
    UpdatedBy           NVARCHAR(100)  NULL,

    CONSTRAINT UQ_Payments_RazorpayOrderId UNIQUE (RazorpayOrderId)
);

-- Refunds — one row per refund request (partial or full)
CREATE TABLE Refunds (
    Id                  INT            IDENTITY(1,1) PRIMARY KEY,
    PaymentId           INT            NOT NULL REFERENCES Payments(Id),
    OrderId             INT            NOT NULL REFERENCES Orders(Id),
    ReturnOrderId       INT            NULL REFERENCES ReturnOrders(Id),  -- linked return if any

    -- Razorpay refund identifier
    RazorpayRefundId    NVARCHAR(100)  NULL,          -- rfnd_xxxxx

    Amount              DECIMAL(18,2)  NOT NULL,      -- refund amount in INR

    -- Status: 0=Pending 1=Initiated 2=Processed 3=Failed 4=Cancelled
    Status              INT            NOT NULL DEFAULT 0,
    -- Method: 0=OriginalPaymentMethod 1=BankTransfer 2=WalletCredit 3=StoreCoupon 4=UPI
    Method              INT            NOT NULL DEFAULT 0,

    Reason              NVARCHAR(500)  NULL,
    Notes               NVARCHAR(500)  NULL,
    InitiatedBy         NVARCHAR(100)  NULL,           -- admin email who triggered
    ProcessedAt         DATETIME2      NULL,            -- when Razorpay confirmed

    -- Audit
    CreatedAt           DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2      NULL,
    IsDeleted           BIT            NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(100)  NULL,
    UpdatedBy           NVARCHAR(100)  NULL
);

-- Indexes
CREATE INDEX IX_Payments_OrderId          ON Payments(OrderId);
CREATE INDEX IX_Payments_Status           ON Payments(Status);
CREATE INDEX IX_Payments_RazorpayPaymentId ON Payments(RazorpayPaymentId) WHERE RazorpayPaymentId IS NOT NULL;
CREATE INDEX IX_Refunds_PaymentId         ON Refunds(PaymentId);
CREATE INDEX IX_Refunds_OrderId           ON Refunds(OrderId);
CREATE INDEX IX_Refunds_ReturnOrderId     ON Refunds(ReturnOrderId) WHERE ReturnOrderId IS NOT NULL;

-- Useful view: pending refunds for admin dashboard
CREATE OR ALTER VIEW vw_PendingRefunds AS
    SELECT r.Id, r.Amount, r.Reason, r.CreatedAt, r.InitiatedBy,
           o.OrderNumber, u.Name AS CustomerName, u.Email AS CustomerEmail,
           p.RazorpayPaymentId
    FROM   Refunds r
    JOIN   Payments p ON r.PaymentId = p.Id
    JOIN   Orders o   ON r.OrderId   = o.Id
    JOIN   Users u    ON o.UserId    = u.Id
    WHERE  r.Status IN (0, 1)   -- Pending or Initiated
    AND    r.IsDeleted = 0;
GO

PRINT 'Payment & Refund tables added. Total tables: 34';
GO


-- ─────────────────────────────────────────────────────────────
-- Site Settings (single row, Id = 1)
-- ─────────────────────────────────────────────────────────────
CREATE TABLE SiteSettings (
    Id                  INT            NOT NULL PRIMARY KEY DEFAULT 1,
    -- General
    SiteName            NVARCHAR(128)  NOT NULL DEFAULT 'Wah Spices',
    Tagline             NVARCHAR(256)  NOT NULL DEFAULT 'Pehle bite mein — WAH!',
    Email               NVARCHAR(256)  NOT NULL DEFAULT '',
    Phone               NVARCHAR(20)   NOT NULL DEFAULT '',
    [Address]           NVARCHAR(512)  NOT NULL DEFAULT '',
    Gstin               NVARCHAR(20)   NULL,
    Fssai               NVARCHAR(50)   NULL,
    -- Shipping
    FreeDeliveryAbove   DECIMAL(10,2)  NOT NULL DEFAULT 499,
    DeliveryCharge      DECIMAL(10,2)  NOT NULL DEFAULT 49,
    AvgDeliveryDays     NVARCHAR(20)   NOT NULL DEFAULT '3-5',
    CodAvailable        BIT            NOT NULL DEFAULT 1,
    -- Notifications
    OrderEmails         BIT            NOT NULL DEFAULT 1,
    LowStockAlerts      BIT            NOT NULL DEFAULT 1,
    ReviewAlerts        BIT            NOT NULL DEFAULT 0,
    WeeklyReport        BIT            NOT NULL DEFAULT 1,
    -- Other
    MaintenanceMode     BIT            NOT NULL DEFAULT 0,
    -- Audit
    CreatedAt           DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2      NULL,
    IsDeleted           BIT            NOT NULL DEFAULT 0,
    CreatedBy           NVARCHAR(100)  NULL,
    UpdatedBy           NVARCHAR(100)  NULL
);
GO

PRINT 'SiteSettings table added.';
GO

-- ─────────────────────────────────────────────────────────────
-- CrmCustomers — add ContactName, State, Notes, DuesSince, CreatedBy, UpdatedBy
-- ─────────────────────────────────────────────────────────────
ALTER TABLE CrmCustomers ADD
    ContactName  NVARCHAR(128) NULL,
    State        NVARCHAR(100) NULL,
    Notes        NVARCHAR(1000) NULL,
    DuesSince    DATETIME2     NULL,
    CreatedBy    NVARCHAR(100) NULL,
    UpdatedBy    NVARCHAR(100) NULL;
GO
PRINT 'CrmCustomers columns added.';
GO
