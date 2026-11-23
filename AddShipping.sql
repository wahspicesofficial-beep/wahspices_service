BEGIN TRANSACTION;
CREATE TABLE [ShippingProviderAccounts] (
    [Id] int NOT NULL IDENTITY,
    [ProviderCode] nvarchar(40) NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [IsEnabled] bit NOT NULL,
    [IsDefault] bit NOT NULL,
    [ApiBaseUrl] nvarchar(300) NOT NULL,
    [ApiUsername] nvarchar(200) NULL,
    [ApiPassword] nvarchar(max) NULL,
    [AuthToken] nvarchar(max) NULL,
    [TokenExpiresAt] datetime2 NULL,
    [TokenLifetimeHours] int NOT NULL,
    [PickupLocation] nvarchar(100) NULL,
    [PickupPincode] nvarchar(10) NULL,
    [ChannelId] nvarchar(40) NULL,
    [TrackingUrlTemplate] nvarchar(300) NULL,
    [WebhookToken] nvarchar(max) NULL,
    [LastWebhookAt] datetime2 NULL,
    [DefaultLengthCm] decimal(10,3) NOT NULL,
    [DefaultBreadthCm] decimal(10,3) NOT NULL,
    [DefaultHeightCm] decimal(10,3) NOT NULL,
    [PackagingWeightKg] decimal(10,3) NOT NULL,
    [MinWeightKg] decimal(10,3) NOT NULL,
    [DefaultHsn] nvarchar(20) NULL,
    [DefaultTaxPercent] decimal(10,3) NOT NULL,
    [AutoSchedulePickup] bit NOT NULL,
    [AutoGenerateLabel] bit NOT NULL,
    [TrackingPollMinutes] int NOT NULL,
    [SyncOrderStatus] bit NOT NULL,
    [LastTestedAt] datetime2 NULL,
    [LastTestOk] bit NULL,
    [LastTestMessage] nvarchar(500) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [UpdatedBy] nvarchar(max) NULL,
    CONSTRAINT [PK_ShippingProviderAccounts] PRIMARY KEY ([Id])
);

CREATE TABLE [Shipments] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [ProviderAccountId] int NOT NULL,
    [ProviderCode] nvarchar(40) NOT NULL,
    [ProviderOrderId] nvarchar(60) NULL,
    [ProviderShipmentId] nvarchar(60) NULL,
    [AwbCode] nvarchar(60) NULL,
    [CourierCompanyId] nvarchar(20) NULL,
    [CourierName] nvarchar(120) NULL,
    [FreightCharge] decimal(10,2) NULL,
    [EstimatedDeliveryDays] nvarchar(40) NULL,
    [Etd] datetime2 NULL,
    [WeightKg] decimal(10,3) NOT NULL,
    [LengthCm] decimal(10,2) NOT NULL,
    [BreadthCm] decimal(10,2) NOT NULL,
    [HeightCm] decimal(10,2) NOT NULL,
    [IsCod] bit NOT NULL,
    [CodAmount] decimal(10,2) NOT NULL,
    [Status] nvarchar(80) NULL,
    [StatusCode] nvarchar(20) NULL,
    [IsCancelled] bit NOT NULL,
    [IsDelivered] bit NOT NULL,
    [IsRto] bit NOT NULL,
    [LabelUrl] nvarchar(600) NULL,
    [ManifestUrl] nvarchar(600) NULL,
    [InvoiceUrl] nvarchar(600) NULL,
    [PickupScheduledAt] datetime2 NULL,
    [PickupTokenNumber] nvarchar(60) NULL,
    [LastTrackedAt] datetime2 NULL,
    [LastError] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [UpdatedBy] nvarchar(max) NULL,
    CONSTRAINT [PK_Shipments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Shipments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Shipments_ShippingProviderAccounts_ProviderAccountId] FOREIGN KEY ([ProviderAccountId]) REFERENCES [ShippingProviderAccounts] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ShipmentEvents] (
    [Id] int NOT NULL IDENTITY,
    [ShipmentId] int NOT NULL,
    [OccurredAt] datetime2 NOT NULL,
    [Status] nvarchar(80) NOT NULL,
    [Activity] nvarchar(400) NULL,
    [Location] nvarchar(200) NULL,
    [Source] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [UpdatedBy] nvarchar(max) NULL,
    CONSTRAINT [PK_ShipmentEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ShipmentEvents_Shipments_ShipmentId] FOREIGN KEY ([ShipmentId]) REFERENCES [Shipments] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_ShipmentEvents_ShipmentId_OccurredAt] ON [ShipmentEvents] ([ShipmentId], [OccurredAt]);

CREATE INDEX [IX_Shipments_AwbCode] ON [Shipments] ([AwbCode]);

CREATE INDEX [IX_Shipments_OrderId] ON [Shipments] ([OrderId]);

CREATE INDEX [IX_Shipments_ProviderAccountId] ON [Shipments] ([ProviderAccountId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261121045350_AddShipping', N'9.0.6');

COMMIT;
GO

