BEGIN TRANSACTION;
ALTER TABLE [Users] ADD [MarketingOptOut] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [Users] ADD [MarketingOptOutAt] datetime2 NULL;

CREATE TABLE [Broadcasts] (
    [Id] int NOT NULL IDENTITY,
    [Type] nvarchar(64) NOT NULL,
    [Title] nvarchar(150) NOT NULL,
    [Message] nvarchar(2000) NOT NULL,
    [ImageUrl] nvarchar(512) NULL,
    [ButtonLabel] nvarchar(40) NULL,
    [ButtonUrl] nvarchar(512) NULL,
    [AudienceType] nvarchar(16) NOT NULL,
    [SelectedUserIds] nvarchar(max) NULL,
    [ShowAsBanner] bit NOT NULL,
    [ActiveFrom] datetime2 NULL,
    [ActiveUntil] datetime2 NULL,
    [Status] nvarchar(16) NOT NULL,
    [PublishedAt] datetime2 NULL,
    [EndedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [UpdatedBy] nvarchar(max) NULL,
    CONSTRAINT [PK_Broadcasts] PRIMARY KEY ([Id])
);

CREATE TABLE [MarketingCampaigns] (
    [Id] int NOT NULL IDENTITY,
    [Type] nvarchar(64) NOT NULL,
    [Title] nvarchar(150) NOT NULL,
    [Body] nvarchar(4000) NOT NULL,
    [ImageUrl] nvarchar(512) NULL,
    [ButtonLabel] nvarchar(40) NULL,
    [ButtonUrl] nvarchar(512) NULL,
    [ProductIds] nvarchar(200) NULL,
    [CouponCode] nvarchar(50) NULL,
    [SendEmail] bit NOT NULL,
    [SendWhatsApp] bit NOT NULL,
    [AudienceType] nvarchar(16) NOT NULL,
    [SelectedUserIds] nvarchar(max) NULL,
    [Status] nvarchar(16) NOT NULL,
    [ScheduledAt] datetime2 NULL,
    [StartedAt] datetime2 NULL,
    [CompletedAt] datetime2 NULL,
    [CancelledAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [UpdatedBy] nvarchar(max) NULL,
    CONSTRAINT [PK_MarketingCampaigns] PRIMARY KEY ([Id])
);

CREATE TABLE [MessagingChannelSettings] (
    [Id] int NOT NULL IDENTITY,
    [Channel] nvarchar(16) NOT NULL,
    [PerMinute] int NOT NULL,
    [PerHour] int NOT NULL,
    [PerDay] int NOT NULL,
    [ProviderDailyLimit] int NOT NULL,
    [TransactionalReserve] int NOT NULL,
    [PausedUntil] datetime2 NULL,
    [PauseReason] nvarchar(300) NULL,
    [UpdatedAt] datetime2 NULL,
    [UpdatedBy] nvarchar(128) NULL,
    CONSTRAINT [PK_MessagingChannelSettings] PRIMARY KEY ([Id])
);

CREATE TABLE [BroadcastRecipients] (
    [Id] bigint NOT NULL IDENTITY,
    [BroadcastId] int NOT NULL,
    [UserId] int NOT NULL,
    [DeliveredAt] datetime2 NOT NULL,
    [BannerSeenAt] datetime2 NULL,
    [ReadAt] datetime2 NULL,
    CONSTRAINT [PK_BroadcastRecipients] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BroadcastRecipients_Broadcasts_BroadcastId] FOREIGN KEY ([BroadcastId]) REFERENCES [Broadcasts] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BroadcastRecipients_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CampaignRecipients] (
    [Id] bigint NOT NULL IDENTITY,
    [CampaignId] int NOT NULL,
    [UserId] int NOT NULL,
    [Channel] nvarchar(16) NOT NULL,
    [Address] nvarchar(256) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [Status] nvarchar(16) NOT NULL,
    [Attempts] int NOT NULL,
    [NextAttemptAt] datetime2 NULL,
    [LastAttemptAt] datetime2 NULL,
    [LastError] nvarchar(500) NULL,
    [SentAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_CampaignRecipients] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CampaignRecipients_MarketingCampaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [MarketingCampaigns] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CampaignRecipients_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_BroadcastRecipients_BroadcastId_UserId] ON [BroadcastRecipients] ([BroadcastId], [UserId]);

CREATE INDEX [IX_BroadcastRecipients_UserId_ReadAt] ON [BroadcastRecipients] ([UserId], [ReadAt]);

CREATE INDEX [IX_Broadcasts_Status_ActiveFrom] ON [Broadcasts] ([Status], [ActiveFrom]);

CREATE INDEX [IX_CampaignRecipients_CampaignId_Status] ON [CampaignRecipients] ([CampaignId], [Status]);

CREATE UNIQUE INDEX [IX_CampaignRecipients_CampaignId_UserId_Channel] ON [CampaignRecipients] ([CampaignId], [UserId], [Channel]);

CREATE INDEX [IX_CampaignRecipients_Channel_LastAttemptAt] ON [CampaignRecipients] ([Channel], [LastAttemptAt]);

CREATE INDEX [IX_CampaignRecipients_Channel_Status_NextAttemptAt] ON [CampaignRecipients] ([Channel], [Status], [NextAttemptAt]);

CREATE INDEX [IX_CampaignRecipients_UserId] ON [CampaignRecipients] ([UserId]);

CREATE INDEX [IX_MarketingCampaigns_Status_ScheduledAt] ON [MarketingCampaigns] ([Status], [ScheduledAt]);

CREATE UNIQUE INDEX [IX_MessagingChannelSettings_Channel] ON [MessagingChannelSettings] ([Channel]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261120153126_AddCustomerCommunications', N'9.0.6');

COMMIT;
GO

