BEGIN TRANSACTION;
CREATE TABLE [Regions] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(80) NOT NULL,
    [Slug] nvarchar(80) NOT NULL,
    [MapStateId] nvarchar(8) NOT NULL,
    [Tagline] nvarchar(120) NULL,
    [CardText] nvarchar(400) NULL,
    [Landmark] nvarchar(80) NULL,
    [FeaturedMasala] nvarchar(60) NULL,
    [AccentColor] nvarchar(9) NOT NULL,
    [HeroImageUrl] nvarchar(512) NULL,
    [HeroImageCredit] nvarchar(300) NULL,
    [CardImageUrl] nvarchar(512) NULL,
    [SignatureMasalasJson] nvarchar(max) NULL,
    [ProducesJson] nvarchar(max) NULL,
    [DishesJson] nvarchar(max) NULL,
    [ProductSlugsJson] nvarchar(4000) NULL,
    [AutoLinkProducts] bit NOT NULL,
    [SeoTitle] nvarchar(70) NULL,
    [SeoDescription] nvarchar(160) NULL,
    [DisplayOrder] int NOT NULL,
    [IsPublished] bit NOT NULL,
    [ShowOnHome] bit NOT NULL,
    [PublishedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedBy] nvarchar(100) NULL,
    [UpdatedBy] nvarchar(100) NULL,
    CONSTRAINT [PK_Regions] PRIMARY KEY ([Id])
);

CREATE TABLE [RegionStorySections] (
    [Id] int NOT NULL IDENTITY,
    [RegionId] int NOT NULL,
    [SortOrder] int NOT NULL,
    [Heading] nvarchar(150) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [ImageUrl] nvarchar(512) NULL,
    [ImageAlt] nvarchar(200) NULL,
    [ImageCaption] nvarchar(300) NULL,
    [ImageCredit] nvarchar(300) NULL,
    CONSTRAINT [PK_RegionStorySections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RegionStorySections_Regions_RegionId] FOREIGN KEY ([RegionId]) REFERENCES [Regions] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Regions_IsPublished_DisplayOrder] ON [Regions] ([IsPublished], [DisplayOrder]);

CREATE UNIQUE INDEX [IX_Regions_Slug] ON [Regions] ([Slug]) WHERE [IsDeleted] = 0;

CREATE INDEX [IX_RegionStorySections_RegionId_SortOrder] ON [RegionStorySections] ([RegionId], [SortOrder]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261120162533_AddRegions', N'9.0.6');

COMMIT;
GO

