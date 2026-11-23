USE db55965;
GO

-- Create Categories table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Categories')
BEGIN
    CREATE TABLE Categories (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        Name         NVARCHAR(128)  NOT NULL,
        Slug         NVARCHAR(128)  NOT NULL,
        Icon         NVARCHAR(10)   NULL,
        Description  NVARCHAR(500)  NULL,
        DisplayOrder INT            NOT NULL DEFAULT 0,
        IsActive     BIT            NOT NULL DEFAULT 1,
        IsDeleted    BIT            NOT NULL DEFAULT 0,
        CreatedAt    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt    DATETIME2      NULL,
        CreatedBy    NVARCHAR(256)  NULL,
        UpdatedBy    NVARCHAR(256)  NULL,
        CONSTRAINT UQ_Categories_Slug UNIQUE (Slug)
    );
    INSERT INTO Categories (Name,Slug,Icon,Description,DisplayOrder,IsActive,CreatedAt) VALUES
    ('Blend Masala','blend-masala',N'🌶️','Perfect blend of spices for everyday cooking',1,1,GETUTCDATE()),
    ('Powder',      'powder',      N'🫙', 'Fine ground spice powders',                   2,1,GETUTCDATE()),
    ('Specialty',   'specialty',   N'⭐', 'Special and unique spice combinations',        3,1,GETUTCDATE()),
    ('Whole Spice', 'whole-spice', N'🌿','Pure whole spices for authentic flavors',      4,1,GETUTCDATE()),
    ('Gift',        'gift',        N'🎁','Curated gift sets for spice lovers',           5,1,GETUTCDATE());
    PRINT 'Categories table created and seeded.';
END
ELSE
    PRINT 'Categories table already exists.';
GO

-- Add CategorySlug column (must be in separate batch after table is created)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Products') AND name='CategorySlug')
    ALTER TABLE Products ADD CategorySlug NVARCHAR(128) NULL;
GO

-- Migrate data from old Category TINYINT to slug string
UPDATE Products SET CategorySlug = CASE [Category]
    WHEN 0 THEN 'blend-masala'
    WHEN 1 THEN 'powder'
    WHEN 2 THEN 'specialty'
    WHEN 3 THEN 'whole-spice'
    WHEN 4 THEN 'gift'
    ELSE 'blend-masala' END
WHERE CategorySlug IS NULL;
GO

-- Make CategorySlug NOT NULL
UPDATE Products SET CategorySlug='blend-masala' WHERE CategorySlug IS NULL;
ALTER TABLE Products ALTER COLUMN CategorySlug NVARCHAR(128) NOT NULL;
PRINT 'CategorySlug ready on Products.';
GO
