-- ============================================================
--  WAH! Spices — product catalog seed (88 products)
--  Generated file. Safe to re-run:
--   * categories are created if missing (names/icons refreshed)
--   * products are inserted with their pack sizes if missing
--   * existing products only get content + image refreshed;
--     prices / pack sizes edited in the admin panel are kept.
--  Requires migration AddProductVariantsAndSubCategories.
--  Images: copy WahSpices.API/wwwroot/uploads/products/catalog/
-- ============================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @pid INT, @parent INT;

-- ── Pack types ──
IF NOT EXISTS (SELECT 1 FROM PackTypeConfigs WHERE Name=N'BoxPack' AND IsDeleted=0)
    INSERT INTO PackTypeConfigs (Name, IsActive, CreatedAt, IsDeleted, CreatedBy) VALUES (N'BoxPack', 1, SYSUTCDATETIME(), 0, N'seed');
IF NOT EXISTS (SELECT 1 FROM PackTypeConfigs WHERE Name=N'PouchPack' AND IsDeleted=0)
    INSERT INTO PackTypeConfigs (Name, IsActive, CreatedAt, IsDeleted, CreatedBy) VALUES (N'PouchPack', 1, SYSUTCDATETIME(), 0, N'seed');
IF NOT EXISTS (SELECT 1 FROM PackTypeConfigs WHERE Name=N'GlassJar' AND IsDeleted=0)
    INSERT INTO PackTypeConfigs (Name, IsActive, CreatedAt, IsDeleted, CreatedBy) VALUES (N'GlassJar', 1, SYSUTCDATETIME(), 0, N'seed');

-- ── Main categories ──
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'blended-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Blended Masala', N'blended-masala', N'🍛', N'Ready-to-use masala blends for curries, dals, snacks and more.', 1, 1, NULL, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Blended Masala', Icon=N'🍛', Description=N'Ready-to-use masala blends for curries, dals, snacks and more.', ParentId=NULL, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'whole-spices')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Whole Spices', N'whole-spices', N'🌶️', N'Whole seeds, pods, barks and flowers — sourced fresh and cleaned.', 2, 1, NULL, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Whole Spices', Icon=N'🌶️', Description=N'Whole seeds, pods, barks and flowers — sourced fresh and cleaned.', ParentId=NULL, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'whole-spices';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'ground-spices')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Ground Spices', N'ground-spices', N'🥄', N'Pure single-spice powders and salts, freshly ground.', 3, 1, NULL, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Ground Spices', Icon=N'🥄', Description=N'Pure single-spice powders and salts, freshly ground.', ParentId=NULL, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'ground-spices';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'dry-dehydrated')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry & Dehydrated', N'dry-dehydrated', N'🍋', N'Sun-dried fruits, vegetables and leaves for tang and aroma.', 4, 1, NULL, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Dry & Dehydrated', Icon=N'🍋', Description=N'Sun-dried fruits, vegetables and leaves for tang and aroma.', ParentId=NULL, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-dehydrated';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'herbs-seasoning')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Herbs & Seasoning', N'herbs-seasoning', N'🌿', N'Herbs and seasonings for pizza, pasta and continental dishes.', 5, 1, NULL, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Herbs & Seasoning', Icon=N'🌿', Description=N'Herbs and seasonings for pizza, pasta and continental dishes.', ParentId=NULL, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'herbs-seasoning';

-- ── Sub-categories ──
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'curry-gravy-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Curry & Gravy Masala', N'curry-gravy-masala', N'🥘', NULL, 1, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Curry & Gravy Masala', Icon=N'🥘', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'curry-gravy-masala';
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'dal-chole-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dal & Chole Masala', N'dal-chole-masala', N'🫘', NULL, 2, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Dal & Chole Masala', Icon=N'🫘', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dal-chole-masala';
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'non-veg-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Non-Veg Masala', N'non-veg-masala', N'🍗', NULL, 3, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Non-Veg Masala', Icon=N'🍗', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'non-veg-masala';
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'rice-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Biryani & Rice Masala', N'rice-masala', N'🍚', NULL, 4, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Biryani & Rice Masala', Icon=N'🍚', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'rice-masala';
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'snacks-street-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Snacks & Street Food', N'snacks-street-masala', N'🥪', NULL, 5, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Snacks & Street Food', Icon=N'🥪', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'snacks-street-masala';
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'beverage-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Beverage Masala', N'beverage-masala', N'🥤', NULL, 6, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Beverage Masala', Icon=N'🥤', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'beverage-masala';
SELECT @parent = Id FROM Categories WHERE Slug=N'blended-masala';
IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug=N'regional-masala')
    INSERT INTO Categories (Name, Slug, Icon, Description, DisplayOrder, IsActive, ParentId, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Regional Specials', N'regional-masala', N'🏡', NULL, 7, 1, @parent, SYSUTCDATETIME(), 0, N'seed');
ELSE
    UPDATE Categories SET Name=N'Regional Specials', Icon=N'🏡', ParentId=@parent, IsDeleted=0, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'regional-masala';

-- ── Products ──

-- Kitchen King Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kitchen-king-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kitchen King Masala', N'kitchen-king-masala', N'The all-rounder of the Indian kitchen: a bold, aromatic blend that lifts everyday sabzis, dals and paneer gravies. Coriander, cumin and red chilli form the base, finished with warm notes of cardamom, clove and kasuri methi.', N'Coriander, Cumin, Red Chilli, Turmeric, Black Pepper, Dry Ginger, Fenugreek Leaves (Kasuri Methi), Cinnamon, Cloves, Green Cardamom, Bay Leaf, Mustard, Nutmeg, Asafoetida, Salt', N'["Mixed vegetable sabzi", "Paneer and veg gravies", "Aloo gobi and bhindi masala", "Dal fry and stuffed parathas"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        N'Bestseller', N'/uploads/products/catalog/kitchen-king-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Kitchen King Masala – All-in-One Masala | WAH! Spices', N'Buy Kitchen King Masala online from WAH! Spices – pure, aromatic and freshly packed. Pehle bite mein – WAH!',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-KITCHEN-KING-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-KITCHEN-KING-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-KITCHEN-KING-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-KITCHEN-KING-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'The all-rounder of the Indian kitchen: a bold, aromatic blend that lifts everyday sabzis, dals and paneer gravies. Coriander, cumin and red chilli form the base, finished with warm notes of cardamom, clove and kasuri methi.', Ingredients=N'Coriander, Cumin, Red Chilli, Turmeric, Black Pepper, Dry Ginger, Fenugreek Leaves (Kasuri Methi), Cinnamon, Cloves, Green Cardamom, Bay Leaf, Mustard, Nutmeg, Asafoetida, Salt', Usages=N'["Mixed vegetable sabzi", "Paneer and veg gravies", "Aloo gobi and bhindi masala", "Dal fry and stuffed parathas"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Kitchen King Masala – All-in-One Masala | WAH! Spices', SeoDescription=N'Buy Kitchen King Masala online from WAH! Spices – pure, aromatic and freshly packed. Pehle bite mein – WAH!', ImageUrl=N'/uploads/products/catalog/kitchen-king-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kitchen-king-masala';

-- Garam Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'garam-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Garam Masala', N'garam-masala', N'The heart of North Indian cooking: a warm, fragrant blend of slow-roasted whole spices. Cinnamon, cardamom, clove, black pepper and star anise come together for a rich aroma — add a pinch at the end of cooking to finish curries, dals and sabzis.', N'Coriander, Cumin, Black Pepper, Cinnamon, Green Cardamom, Black Cardamom, Cloves, Bay Leaf, Star Anise, Nutmeg, Mace, Dry Ginger, Fennel', N'["Finishing curries and gravies", "Dal tadka and dal makhani", "Paneer, chole and rajma", "Biryani, pulao and marinades"]', N'blended-masala', N'curry-gravy-masala',
        55, 60, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/garam-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Garam Masala – Aromatic Whole-Spice Blend | WAH! Spices', N'Buy Garam Masala online from WAH! Spices. A warm, fragrant blend of slow-roasted whole spices – 100% pure, no added preservatives.',
        380, N'13g', N'15g', N'50g', N'23g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 55, 60, N'WAH-GARAM-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 110, N'WAH-GARAM-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 185, 210, N'WAH-GARAM-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 425, 480, N'WAH-GARAM-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'The heart of North Indian cooking: a warm, fragrant blend of slow-roasted whole spices. Cinnamon, cardamom, clove, black pepper and star anise come together for a rich aroma — add a pinch at the end of cooking to finish curries, dals and sabzis.', Ingredients=N'Coriander, Cumin, Black Pepper, Cinnamon, Green Cardamom, Black Cardamom, Cloves, Bay Leaf, Star Anise, Nutmeg, Mace, Dry Ginger, Fennel', Usages=N'["Finishing curries and gravies", "Dal tadka and dal makhani", "Paneer, chole and rajma", "Biryani, pulao and marinades"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Garam Masala – Aromatic Whole-Spice Blend | WAH! Spices', SeoDescription=N'Buy Garam Masala online from WAH! Spices. A warm, fragrant blend of slow-roasted whole spices – 100% pure, no added preservatives.', ImageUrl=N'/uploads/products/catalog/garam-masala.png', CaloriesPer100g=380, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'50g', FiberPer100g=N'23g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'garam-masala';

-- Sabji Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sabji-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sabji Masala', N'sabji-masala', N'A balanced everyday blend made for dry and semi-dry vegetable dishes. Earthy coriander and cumin with a gentle chilli warmth bring out the natural sweetness of seasonal vegetables.', N'Coriander, Cumin, Red Chilli, Turmeric, Dry Mango Powder, Fennel, Black Pepper, Dry Ginger, Cloves, Cinnamon, Salt', N'["Aloo, gobi and bhindi stir-fries", "Mixed veg sukhi sabzi", "Lauki, tinda and turai curries", "Stuffed vegetables"]', N'blended-masala', N'curry-gravy-masala',
        39, 45, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/sabji-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Sabji Masala – Everyday Veg Blend | WAH! Spices', N'Buy Sabji Masala online from WAH! Spices. A balanced everyday blend made for dry and semi-dry vegetable dishes.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 39, 45, N'WAH-SABJI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 75, 85, N'WAH-SABJI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 139, 160, N'WAH-SABJI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 309, 350, N'WAH-SABJI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A balanced everyday blend made for dry and semi-dry vegetable dishes. Earthy coriander and cumin with a gentle chilli warmth bring out the natural sweetness of seasonal vegetables.', Ingredients=N'Coriander, Cumin, Red Chilli, Turmeric, Dry Mango Powder, Fennel, Black Pepper, Dry Ginger, Cloves, Cinnamon, Salt', Usages=N'["Aloo, gobi and bhindi stir-fries", "Mixed veg sukhi sabzi", "Lauki, tinda and turai curries", "Stuffed vegetables"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Sabji Masala – Everyday Veg Blend | WAH! Spices', SeoDescription=N'Buy Sabji Masala online from WAH! Spices. A balanced everyday blend made for dry and semi-dry vegetable dishes.', ImageUrl=N'/uploads/products/catalog/sabji-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sabji-masala';

-- Curry Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'curry-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Curry Masala', N'curry-masala', N'A versatile curry powder for rich, onion-tomato based gravies. Roasted coriander, turmeric and chilli give a deep golden colour and rounded, homestyle flavour.', N'Coriander, Turmeric, Red Chilli, Cumin, Fenugreek Seeds, Black Pepper, Mustard, Dry Ginger, Garlic, Cinnamon, Cloves, Bay Leaf, Salt', N'["Vegetable and egg curries", "Chicken or mutton gravy base", "Potato and pea curry", "Coconut-based curries"]', N'blended-masala', N'curry-gravy-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/curry-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Curry Masala – Rich Gravy Blend | WAH! Spices', N'Buy Curry Masala online from WAH! Spices. A versatile curry powder for rich, onion-tomato based gravies.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-CURRY-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-CURRY-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-CURRY-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-CURRY-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A versatile curry powder for rich, onion-tomato based gravies. Roasted coriander, turmeric and chilli give a deep golden colour and rounded, homestyle flavour.', Ingredients=N'Coriander, Turmeric, Red Chilli, Cumin, Fenugreek Seeds, Black Pepper, Mustard, Dry Ginger, Garlic, Cinnamon, Cloves, Bay Leaf, Salt', Usages=N'["Vegetable and egg curries", "Chicken or mutton gravy base", "Potato and pea curry", "Coconut-based curries"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Curry Masala – Rich Gravy Blend | WAH! Spices', SeoDescription=N'Buy Curry Masala online from WAH! Spices. A versatile curry powder for rich, onion-tomato based gravies.', ImageUrl=N'/uploads/products/catalog/curry-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'curry-masala';

-- Kadai Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kadai-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kadai Masala', N'kadai-masala', N'Coarsely ground, tossed-in-the-wok flavour: whole coriander and dried red chillies roasted and crushed for a smoky, rustic kadai taste. Perfect with capsicum, onions and tomatoes.', N'Coriander Seeds, Dried Red Chilli, Cumin, Black Pepper, Fennel, Fenugreek Leaves, Dry Ginger, Cloves, Green Cardamom, Salt', N'["Kadai paneer", "Kadai chicken", "Kadai mushroom and capsicum", "Dry veg stir-fries"]', N'blended-masala', N'curry-gravy-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/kadai-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Kadai Masala – Smoky Wok Blend | WAH! Spices', N'Buy Kadai Masala online from WAH! Spices – pure, aromatic and freshly packed. Pehle bite mein – WAH!',
        320, N'13g', N'12g', N'47g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-KADAI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 85, 95, N'WAH-KADAI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 155, 175, N'WAH-KADAI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 349, 395, N'WAH-KADAI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Coarsely ground, tossed-in-the-wok flavour: whole coriander and dried red chillies roasted and crushed for a smoky, rustic kadai taste. Perfect with capsicum, onions and tomatoes.', Ingredients=N'Coriander Seeds, Dried Red Chilli, Cumin, Black Pepper, Fennel, Fenugreek Leaves, Dry Ginger, Cloves, Green Cardamom, Salt', Usages=N'["Kadai paneer", "Kadai chicken", "Kadai mushroom and capsicum", "Dry veg stir-fries"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Kadai Masala – Smoky Wok Blend | WAH! Spices', SeoDescription=N'Buy Kadai Masala online from WAH! Spices – pure, aromatic and freshly packed. Pehle bite mein – WAH!', ImageUrl=N'/uploads/products/catalog/kadai-masala.png', CaloriesPer100g=320, ProteinPer100g=N'13g', FatPer100g=N'12g', CarbsPer100g=N'47g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kadai-masala';

-- Handi Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'handi-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Handi Masala', N'handi-masala', N'Inspired by slow-cooked handi dishes, this blend layers warm whole spices with a mellow creaminess. It gives gravies a rich, rounded depth without overpowering heat.', N'Coriander, Cumin, Red Chilli, Turmeric, Fennel, Black Pepper, Green Cardamom, Cinnamon, Cloves, Mace, Nutmeg, Kasuri Methi, Dry Ginger, Salt', N'["Handi paneer", "Chicken handi", "Veg handi with cream", "Slow-cooked mutton"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/handi-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Handi Masala – Slow-Cooked Flavour | WAH! Spices', N'Buy Handi Masala online from WAH! Spices. Inspired by slow-cooked handi dishes, this blend layers warm whole spices with a mellow creaminess.',
        360, N'13g', N'15g', N'45g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-HANDI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-HANDI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-HANDI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-HANDI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Inspired by slow-cooked handi dishes, this blend layers warm whole spices with a mellow creaminess. It gives gravies a rich, rounded depth without overpowering heat.', Ingredients=N'Coriander, Cumin, Red Chilli, Turmeric, Fennel, Black Pepper, Green Cardamom, Cinnamon, Cloves, Mace, Nutmeg, Kasuri Methi, Dry Ginger, Salt', Usages=N'["Handi paneer", "Chicken handi", "Veg handi with cream", "Slow-cooked mutton"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Handi Masala – Slow-Cooked Flavour | WAH! Spices', SeoDescription=N'Buy Handi Masala online from WAH! Spices. Inspired by slow-cooked handi dishes, this blend layers warm whole spices with a mellow creaminess.', ImageUrl=N'/uploads/products/catalog/handi-masala.png', CaloriesPer100g=360, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'45g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'handi-masala';

-- Korma Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'korma-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Korma Masala', N'korma-masala', N'A fragrant, mild Mughlai blend for creamy, nut-based kormas. Cardamom, mace and fennel lend a royal aroma while keeping the heat gentle.', N'Coriander, Fennel, Cumin, Green Cardamom, Cinnamon, Cloves, Mace, Nutmeg, Black Pepper, White Pepper, Dry Ginger, Kashmiri Red Chilli, Bay Leaf, Salt', N'["Navratan korma", "Chicken or mutton korma", "Veg korma with cashew paste", "Shahi egg korma"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/korma-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Korma Masala – Mughlai Blend | WAH! Spices', N'Buy Korma Masala online from WAH! Spices. A fragrant, mild Mughlai blend for creamy, nut-based kormas.',
        360, N'13g', N'15g', N'45g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-KORMA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-KORMA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-KORMA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-KORMA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A fragrant, mild Mughlai blend for creamy, nut-based kormas. Cardamom, mace and fennel lend a royal aroma while keeping the heat gentle.', Ingredients=N'Coriander, Fennel, Cumin, Green Cardamom, Cinnamon, Cloves, Mace, Nutmeg, Black Pepper, White Pepper, Dry Ginger, Kashmiri Red Chilli, Bay Leaf, Salt', Usages=N'["Navratan korma", "Chicken or mutton korma", "Veg korma with cashew paste", "Shahi egg korma"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Korma Masala – Mughlai Blend | WAH! Spices', SeoDescription=N'Buy Korma Masala online from WAH! Spices. A fragrant, mild Mughlai blend for creamy, nut-based kormas.', ImageUrl=N'/uploads/products/catalog/korma-masala.png', CaloriesPer100g=360, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'45g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'korma-masala';

-- Kofta Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kofta-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kofta Masala', N'kofta-masala', N'Made for kofta curries, this blend seasons both the dumplings and the gravy. Warm spices with a hint of fennel and kasuri methi give a restaurant-style finish.', N'Coriander, Cumin, Kashmiri Red Chilli, Turmeric, Fennel, Black Pepper, Green Cardamom, Cinnamon, Cloves, Kasuri Methi, Dry Ginger, Salt', N'["Lauki kofta curry", "Veg kofta gravy", "Seasoning the kofta mixture", "Mutton kofta (kheema balls)"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/kofta-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Kofta Masala – Curry Blend | WAH! Spices', N'Buy Kofta Masala online from WAH! Spices. Made for kofta curries, this blend seasons both the dumplings and the gravy.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-KOFTA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-KOFTA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-KOFTA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-KOFTA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Made for kofta curries, this blend seasons both the dumplings and the gravy. Warm spices with a hint of fennel and kasuri methi give a restaurant-style finish.', Ingredients=N'Coriander, Cumin, Kashmiri Red Chilli, Turmeric, Fennel, Black Pepper, Green Cardamom, Cinnamon, Cloves, Kasuri Methi, Dry Ginger, Salt', Usages=N'["Lauki kofta curry", "Veg kofta gravy", "Seasoning the kofta mixture", "Mutton kofta (kheema balls)"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Kofta Masala – Curry Blend | WAH! Spices', SeoDescription=N'Buy Kofta Masala online from WAH! Spices. Made for kofta curries, this blend seasons both the dumplings and the gravy.', ImageUrl=N'/uploads/products/catalog/kofta-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kofta-masala';

-- Malai Kofta Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'malai-kofta-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Malai Kofta Masala', N'malai-kofta-masala', N'A delicately spiced, slightly sweet blend for the creamy, cashew-rich gravy of malai kofta. Cardamom and mace give it a festive, indulgent aroma.', N'Coriander, Kashmiri Red Chilli, Cumin, Green Cardamom, Cinnamon, Mace, Nutmeg, White Pepper, Kasuri Methi, Fennel, Dry Ginger, Sugar, Salt', N'["Malai kofta", "Creamy paneer kofta", "White gravies with cashew", "Mild veg curries for kids"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/malai-kofta-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Malai Kofta Masala – Creamy Kofta Blend | WAH! Spices', N'Buy Malai Kofta Masala online from WAH! Spices. A delicately spiced, slightly sweet blend for the creamy, cashew-rich gravy of malai kofta.',
        360, N'13g', N'15g', N'45g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-MALAI-KOFTA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-MALAI-KOFTA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-MALAI-KOFTA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-MALAI-KOFTA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A delicately spiced, slightly sweet blend for the creamy, cashew-rich gravy of malai kofta. Cardamom and mace give it a festive, indulgent aroma.', Ingredients=N'Coriander, Kashmiri Red Chilli, Cumin, Green Cardamom, Cinnamon, Mace, Nutmeg, White Pepper, Kasuri Methi, Fennel, Dry Ginger, Sugar, Salt', Usages=N'["Malai kofta", "Creamy paneer kofta", "White gravies with cashew", "Mild veg curries for kids"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Malai Kofta Masala – Creamy Kofta Blend | WAH! Spices', SeoDescription=N'Buy Malai Kofta Masala online from WAH! Spices. A delicately spiced, slightly sweet blend for the creamy, cashew-rich gravy of malai kofta.', ImageUrl=N'/uploads/products/catalog/malai-kofta-masala.png', CaloriesPer100g=360, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'45g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'malai-kofta-masala';

-- Paneer Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'paneer-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Paneer Masala', N'paneer-masala', N'A vibrant, medium-spiced blend crafted for paneer. Kashmiri chilli adds rich colour while coriander, cumin and kasuri methi give that classic dhaba taste.', N'Coriander, Kashmiri Red Chilli, Cumin, Turmeric, Kasuri Methi, Black Pepper, Dry Ginger, Garlic, Green Cardamom, Cinnamon, Cloves, Dry Mango Powder, Salt', N'["Paneer masala gravy", "Matar paneer", "Paneer bhurji", "Paneer tikka marinade"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/paneer-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Paneer Masala – Dhaba-Style Blend | WAH! Spices', N'Buy Paneer Masala online from WAH! Spices. A vibrant, medium-spiced blend crafted for paneer.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-PANEER-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-PANEER-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-PANEER-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-PANEER-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A vibrant, medium-spiced blend crafted for paneer. Kashmiri chilli adds rich colour while coriander, cumin and kasuri methi give that classic dhaba taste.', Ingredients=N'Coriander, Kashmiri Red Chilli, Cumin, Turmeric, Kasuri Methi, Black Pepper, Dry Ginger, Garlic, Green Cardamom, Cinnamon, Cloves, Dry Mango Powder, Salt', Usages=N'["Paneer masala gravy", "Matar paneer", "Paneer bhurji", "Paneer tikka marinade"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Paneer Masala – Dhaba-Style Blend | WAH! Spices', SeoDescription=N'Buy Paneer Masala online from WAH! Spices. A vibrant, medium-spiced blend crafted for paneer.', ImageUrl=N'/uploads/products/catalog/paneer-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'paneer-masala';

-- Shahi Paneer Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'shahi-paneer-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Shahi Paneer Masala', N'shahi-paneer-masala', N'A royal, aromatic blend for rich, creamy shahi paneer. Cardamom, mace and the gentle glow of Kashmiri chilli create a mildly sweet, luxurious gravy.', N'Coriander, Kashmiri Red Chilli, Cumin, Green Cardamom, Cinnamon, Cloves, Mace, Nutmeg, White Pepper, Kasuri Methi, Fennel, Dry Ginger, Salt', N'["Shahi paneer", "Paneer pasanda", "Cashew-tomato gravies", "Festive veg curries"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/shahi-paneer-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Shahi Paneer Masala – Royal Gravy Blend | WAH! Spices', N'Buy Shahi Paneer Masala online from WAH! Spices. A royal, aromatic blend for rich, creamy shahi paneer.',
        360, N'13g', N'15g', N'45g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-SHAHI-PANEER-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-SHAHI-PANEER-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-SHAHI-PANEER-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-SHAHI-PANEER-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A royal, aromatic blend for rich, creamy shahi paneer. Cardamom, mace and the gentle glow of Kashmiri chilli create a mildly sweet, luxurious gravy.', Ingredients=N'Coriander, Kashmiri Red Chilli, Cumin, Green Cardamom, Cinnamon, Cloves, Mace, Nutmeg, White Pepper, Kasuri Methi, Fennel, Dry Ginger, Salt', Usages=N'["Shahi paneer", "Paneer pasanda", "Cashew-tomato gravies", "Festive veg curries"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Shahi Paneer Masala – Royal Gravy Blend | WAH! Spices', SeoDescription=N'Buy Shahi Paneer Masala online from WAH! Spices. A royal, aromatic blend for rich, creamy shahi paneer.', ImageUrl=N'/uploads/products/catalog/shahi-paneer-masala.png', CaloriesPer100g=360, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'45g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'shahi-paneer-masala';

-- Butter Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'butter-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Butter Masala', N'butter-masala', N'The secret behind silky, buttery tomato gravies. Kashmiri chilli for colour, kasuri methi for aroma and a gentle sweetness make paneer or chicken makhani taste restaurant-perfect.', N'Kashmiri Red Chilli, Coriander, Cumin, Kasuri Methi, Green Cardamom, Cinnamon, Cloves, Dry Ginger, Garlic, Black Pepper, Mace, Sugar, Salt', N'["Paneer butter masala", "Butter chicken", "Makhani dal", "Creamy tomato soups"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/butter-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Butter Masala – Makhani Gravy Blend | WAH! Spices', N'Buy Butter Masala online from WAH! Spices. The secret behind silky, buttery tomato gravies.',
        360, N'13g', N'15g', N'45g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-BUTTER-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-BUTTER-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-BUTTER-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-BUTTER-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'The secret behind silky, buttery tomato gravies. Kashmiri chilli for colour, kasuri methi for aroma and a gentle sweetness make paneer or chicken makhani taste restaurant-perfect.', Ingredients=N'Kashmiri Red Chilli, Coriander, Cumin, Kasuri Methi, Green Cardamom, Cinnamon, Cloves, Dry Ginger, Garlic, Black Pepper, Mace, Sugar, Salt', Usages=N'["Paneer butter masala", "Butter chicken", "Makhani dal", "Creamy tomato soups"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Butter Masala – Makhani Gravy Blend | WAH! Spices', SeoDescription=N'Buy Butter Masala online from WAH! Spices. The secret behind silky, buttery tomato gravies.', ImageUrl=N'/uploads/products/catalog/butter-masala.png', CaloriesPer100g=360, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'45g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'butter-masala';

-- Tikka Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'tikka-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Tikka Masala', N'tikka-masala', N'A smoky, tangy blend for tikkas straight off the grill. Use it as a marinade with curd and lemon, or stir it into a rich tomato gravy for tikka masala.', N'Kashmiri Red Chilli, Coriander, Cumin, Dry Mango Powder, Black Pepper, Dry Ginger, Garlic, Kasuri Methi, Green Cardamom, Cinnamon, Cloves, Black Salt, Salt', N'["Paneer tikka", "Chicken tikka", "Tikka masala gravy", "Grilled veggies and mushrooms"]', N'blended-masala', N'curry-gravy-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/tikka-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Tikka Masala – Smoky Grill Blend | WAH! Spices', N'Buy Tikka Masala online from WAH! Spices. A smoky, tangy blend for tikkas straight off the grill.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-TIKKA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-TIKKA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-TIKKA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-TIKKA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A smoky, tangy blend for tikkas straight off the grill. Use it as a marinade with curd and lemon, or stir it into a rich tomato gravy for tikka masala.', Ingredients=N'Kashmiri Red Chilli, Coriander, Cumin, Dry Mango Powder, Black Pepper, Dry Ginger, Garlic, Kasuri Methi, Green Cardamom, Cinnamon, Cloves, Black Salt, Salt', Usages=N'["Paneer tikka", "Chicken tikka", "Tikka masala gravy", "Grilled veggies and mushrooms"]', CategorySlug=N'blended-masala', SubCategorySlug=N'curry-gravy-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Tikka Masala – Smoky Grill Blend | WAH! Spices', SeoDescription=N'Buy Tikka Masala online from WAH! Spices. A smoky, tangy blend for tikkas straight off the grill.', ImageUrl=N'/uploads/products/catalog/tikka-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'tikka-masala';

-- Chana Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chana-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chana Masala', N'chana-masala', N'A tangy, robust blend for Punjabi-style chana. Dry mango and pomegranate seed add sourness, while roasted cumin and black pepper bring depth to every bowl.', N'Coriander, Cumin, Red Chilli, Dry Mango Powder, Pomegranate Seeds (Anardana), Black Pepper, Black Salt, Dry Ginger, Cinnamon, Cloves, Bay Leaf, Black Cardamom, Salt', N'["Kala chana curry", "Chana chaat", "Sprouts salad", "Chickpea sundal"]', N'blended-masala', N'dal-chole-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/chana-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Chana Masala – Tangy Punjabi Blend | WAH! Spices', N'Buy Chana Masala online from WAH! Spices. A tangy, robust blend for Punjabi-style chana.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-CHANA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-CHANA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-CHANA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-CHANA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A tangy, robust blend for Punjabi-style chana. Dry mango and pomegranate seed add sourness, while roasted cumin and black pepper bring depth to every bowl.', Ingredients=N'Coriander, Cumin, Red Chilli, Dry Mango Powder, Pomegranate Seeds (Anardana), Black Pepper, Black Salt, Dry Ginger, Cinnamon, Cloves, Bay Leaf, Black Cardamom, Salt', Usages=N'["Kala chana curry", "Chana chaat", "Sprouts salad", "Chickpea sundal"]', CategorySlug=N'blended-masala', SubCategorySlug=N'dal-chole-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Chana Masala – Tangy Punjabi Blend | WAH! Spices', SeoDescription=N'Buy Chana Masala online from WAH! Spices. A tangy, robust blend for Punjabi-style chana.', ImageUrl=N'/uploads/products/catalog/chana-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chana-masala';

-- Chole Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chole-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chole Masala', N'chole-masala', N'Deep, dark and tangy — the classic Amritsari chole flavour. Black cardamom, anardana and roasted spices give chole its signature colour and punch.', N'Coriander, Cumin, Red Chilli, Pomegranate Seeds (Anardana), Dry Mango Powder, Black Cardamom, Black Pepper, Cloves, Cinnamon, Bay Leaf, Dry Ginger, Black Salt, Salt', N'["Chole bhature", "Pindi chole", "Chole kulche", "Chickpea rice bowls"]', N'blended-masala', N'dal-chole-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/chole-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Chole Masala – Amritsari Chole Blend | WAH! Spices', N'Buy Chole Masala online from WAH! Spices. Deep, dark and tangy — the classic Amritsari chole flavour.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-CHOLE-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 85, 95, N'WAH-CHOLE-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 155, 175, N'WAH-CHOLE-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 349, 395, N'WAH-CHOLE-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Deep, dark and tangy — the classic Amritsari chole flavour. Black cardamom, anardana and roasted spices give chole its signature colour and punch.', Ingredients=N'Coriander, Cumin, Red Chilli, Pomegranate Seeds (Anardana), Dry Mango Powder, Black Cardamom, Black Pepper, Cloves, Cinnamon, Bay Leaf, Dry Ginger, Black Salt, Salt', Usages=N'["Chole bhature", "Pindi chole", "Chole kulche", "Chickpea rice bowls"]', CategorySlug=N'blended-masala', SubCategorySlug=N'dal-chole-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Chole Masala – Amritsari Chole Blend | WAH! Spices', SeoDescription=N'Buy Chole Masala online from WAH! Spices. Deep, dark and tangy — the classic Amritsari chole flavour.', ImageUrl=N'/uploads/products/catalog/chole-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chole-masala';

-- Rajma Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'rajma-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Rajma Masala', N'rajma-masala', N'A comforting blend for the Sunday favourite, rajma chawal. Kashmiri chilli, cumin and a touch of garam masala spices give kidney beans a rich, hearty gravy.', N'Coriander, Kashmiri Red Chilli, Cumin, Turmeric, Black Pepper, Dry Ginger, Black Cardamom, Cinnamon, Cloves, Bay Leaf, Kasuri Methi, Salt', N'["Rajma chawal", "Lobia (black-eyed pea) curry", "Bean soups and stews", "Rajma wraps"]', N'blended-masala', N'dal-chole-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/rajma-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Rajma Masala – Hearty Gravy Blend | WAH! Spices', N'Buy Rajma Masala online from WAH! Spices. A comforting blend for the Sunday favourite, rajma chawal.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-RAJMA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-RAJMA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-RAJMA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-RAJMA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A comforting blend for the Sunday favourite, rajma chawal. Kashmiri chilli, cumin and a touch of garam masala spices give kidney beans a rich, hearty gravy.', Ingredients=N'Coriander, Kashmiri Red Chilli, Cumin, Turmeric, Black Pepper, Dry Ginger, Black Cardamom, Cinnamon, Cloves, Bay Leaf, Kasuri Methi, Salt', Usages=N'["Rajma chawal", "Lobia (black-eyed pea) curry", "Bean soups and stews", "Rajma wraps"]', CategorySlug=N'blended-masala', SubCategorySlug=N'dal-chole-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Rajma Masala – Hearty Gravy Blend | WAH! Spices', SeoDescription=N'Buy Rajma Masala online from WAH! Spices. A comforting blend for the Sunday favourite, rajma chawal.', ImageUrl=N'/uploads/products/catalog/rajma-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'rajma-masala';

-- Dal Makhani Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dal-makhani-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dal Makhani Masala', N'dal-makhani-masala', N'Made for slow-simmered, buttery dal makhani. Smoky black cardamom, kasuri methi and Kashmiri chilli give whole urad and rajma a velvety, restaurant-style flavour.', N'Kashmiri Red Chilli, Coriander, Cumin, Kasuri Methi, Black Cardamom, Cinnamon, Cloves, Dry Ginger, Garlic, Black Pepper, Bay Leaf, Salt', N'["Dal makhani", "Maa ki dal", "Creamy mixed dal", "Makhani gravies"]', N'blended-masala', N'dal-chole-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/dal-makhani-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Dal Makhani Masala – Buttery Dal Blend | WAH! Spices', N'Buy Dal Makhani Masala online from WAH! Spices. Made for slow-simmered, buttery dal makhani.',
        360, N'13g', N'15g', N'45g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-DAL-MAKHANI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-DAL-MAKHANI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-DAL-MAKHANI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-DAL-MAKHANI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Made for slow-simmered, buttery dal makhani. Smoky black cardamom, kasuri methi and Kashmiri chilli give whole urad and rajma a velvety, restaurant-style flavour.', Ingredients=N'Kashmiri Red Chilli, Coriander, Cumin, Kasuri Methi, Black Cardamom, Cinnamon, Cloves, Dry Ginger, Garlic, Black Pepper, Bay Leaf, Salt', Usages=N'["Dal makhani", "Maa ki dal", "Creamy mixed dal", "Makhani gravies"]', CategorySlug=N'blended-masala', SubCategorySlug=N'dal-chole-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Dal Makhani Masala – Buttery Dal Blend | WAH! Spices', SeoDescription=N'Buy Dal Makhani Masala online from WAH! Spices. Made for slow-simmered, buttery dal makhani.', ImageUrl=N'/uploads/products/catalog/dal-makhani-masala.png', CaloriesPer100g=360, ProteinPer100g=N'13g', FatPer100g=N'15g', CarbsPer100g=N'45g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dal-makhani-masala';

-- Dal Tadka Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dal-tadka-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dal Tadka Masala', N'dal-tadka-masala', N'A ready tadka blend that turns plain yellow dal into a fragrant dhaba-style dal. Cumin, garlic, asafoetida and red chilli bloom beautifully in hot ghee.', N'Cumin, Coriander, Red Chilli, Turmeric, Garlic, Mustard, Asafoetida, Dry Ginger, Black Pepper, Kasuri Methi, Salt', N'["Dal tadka and dal fry", "Moong and masoor dal", "Khichdi tempering", "Sambhar-style lentil soups"]', N'blended-masala', N'dal-chole-masala',
        39, 45, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/dal-tadka-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Dal Tadka Masala – Dhaba Tadka Blend | WAH! Spices', N'Buy Dal Tadka Masala online from WAH! Spices. A ready tadka blend that turns plain yellow dal into a fragrant dhaba-style dal.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 39, 45, N'WAH-DAL-TADKA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 75, 85, N'WAH-DAL-TADKA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 139, 160, N'WAH-DAL-TADKA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 309, 350, N'WAH-DAL-TADKA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A ready tadka blend that turns plain yellow dal into a fragrant dhaba-style dal. Cumin, garlic, asafoetida and red chilli bloom beautifully in hot ghee.', Ingredients=N'Cumin, Coriander, Red Chilli, Turmeric, Garlic, Mustard, Asafoetida, Dry Ginger, Black Pepper, Kasuri Methi, Salt', Usages=N'["Dal tadka and dal fry", "Moong and masoor dal", "Khichdi tempering", "Sambhar-style lentil soups"]', CategorySlug=N'blended-masala', SubCategorySlug=N'dal-chole-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Dal Tadka Masala – Dhaba Tadka Blend | WAH! Spices', SeoDescription=N'Buy Dal Tadka Masala online from WAH! Spices. A ready tadka blend that turns plain yellow dal into a fragrant dhaba-style dal.', ImageUrl=N'/uploads/products/catalog/dal-tadka-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dal-tadka-masala';

-- Chicken Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chicken-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chicken Masala', N'chicken-masala', N'A robust, aromatic blend for homestyle chicken curry. Roasted coriander, chilli and whole garam spices give the gravy a deep colour and full-bodied taste.', N'Coriander, Red Chilli, Cumin, Turmeric, Black Pepper, Dry Ginger, Garlic, Fennel, Cinnamon, Cloves, Green Cardamom, Black Cardamom, Bay Leaf, Nutmeg, Mace, Salt', N'["Homestyle chicken curry", "Chicken fry and roast", "Chicken biryani gravy", "Egg or paneer curries"]', N'blended-masala', N'non-veg-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/chicken-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Chicken Masala – Homestyle Curry Blend | WAH! Spices', N'Buy Chicken Masala online from WAH! Spices. A robust, aromatic blend for homestyle chicken curry.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-CHICKEN-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-CHICKEN-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-CHICKEN-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-CHICKEN-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A robust, aromatic blend for homestyle chicken curry. Roasted coriander, chilli and whole garam spices give the gravy a deep colour and full-bodied taste.', Ingredients=N'Coriander, Red Chilli, Cumin, Turmeric, Black Pepper, Dry Ginger, Garlic, Fennel, Cinnamon, Cloves, Green Cardamom, Black Cardamom, Bay Leaf, Nutmeg, Mace, Salt', Usages=N'["Homestyle chicken curry", "Chicken fry and roast", "Chicken biryani gravy", "Egg or paneer curries"]', CategorySlug=N'blended-masala', SubCategorySlug=N'non-veg-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Chicken Masala – Homestyle Curry Blend | WAH! Spices', SeoDescription=N'Buy Chicken Masala online from WAH! Spices. A robust, aromatic blend for homestyle chicken curry.', ImageUrl=N'/uploads/products/catalog/chicken-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chicken-masala';

-- Mutton Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'mutton-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Mutton Masala', N'mutton-masala', N'A bold, warm blend built for slow-cooked mutton. Black cardamom, cloves and black pepper stand up to rich red meat and long simmering.', N'Coriander, Red Chilli, Cumin, Black Pepper, Black Cardamom, Cloves, Cinnamon, Turmeric, Dry Ginger, Garlic, Fennel, Bay Leaf, Mace, Nutmeg, Salt', N'["Mutton curry and rogan josh", "Keema", "Paya and nalli nihari", "Mutton sukka"]', N'blended-masala', N'non-veg-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/mutton-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Mutton Masala – Bold Meat Blend | WAH! Spices', N'Buy Mutton Masala online from WAH! Spices. A bold, warm blend built for slow-cooked mutton.',
        320, N'13g', N'12g', N'47g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-MUTTON-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-MUTTON-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-MUTTON-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-MUTTON-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A bold, warm blend built for slow-cooked mutton. Black cardamom, cloves and black pepper stand up to rich red meat and long simmering.', Ingredients=N'Coriander, Red Chilli, Cumin, Black Pepper, Black Cardamom, Cloves, Cinnamon, Turmeric, Dry Ginger, Garlic, Fennel, Bay Leaf, Mace, Nutmeg, Salt', Usages=N'["Mutton curry and rogan josh", "Keema", "Paya and nalli nihari", "Mutton sukka"]', CategorySlug=N'blended-masala', SubCategorySlug=N'non-veg-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Mutton Masala – Bold Meat Blend | WAH! Spices', SeoDescription=N'Buy Mutton Masala online from WAH! Spices. A bold, warm blend built for slow-cooked mutton.', ImageUrl=N'/uploads/products/catalog/mutton-masala.png', CaloriesPer100g=320, ProteinPer100g=N'13g', FatPer100g=N'12g', CarbsPer100g=N'47g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'mutton-masala';

-- Meat Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'meat-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Meat Masala', N'meat-masala', N'An all-purpose blend for meat dishes of every kind, from curries to dry roasts. Generous whole spices give a fiery, aromatic finish.', N'Coriander, Red Chilli, Cumin, Turmeric, Black Pepper, Cloves, Cinnamon, Black Cardamom, Green Cardamom, Dry Ginger, Garlic, Bay Leaf, Fennel, Nutmeg, Salt', N'["Meat curries", "Keema matar", "Kebab and cutlet mixtures", "Dry meat roasts"]', N'blended-masala', N'non-veg-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/meat-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Meat Masala – All-Purpose Meat Blend | WAH! Spices', N'Buy Meat Masala online from WAH! Spices. An all-purpose blend for meat dishes of every kind, from curries to dry roasts.',
        320, N'13g', N'12g', N'47g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-MEAT-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-MEAT-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-MEAT-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-MEAT-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'An all-purpose blend for meat dishes of every kind, from curries to dry roasts. Generous whole spices give a fiery, aromatic finish.', Ingredients=N'Coriander, Red Chilli, Cumin, Turmeric, Black Pepper, Cloves, Cinnamon, Black Cardamom, Green Cardamom, Dry Ginger, Garlic, Bay Leaf, Fennel, Nutmeg, Salt', Usages=N'["Meat curries", "Keema matar", "Kebab and cutlet mixtures", "Dry meat roasts"]', CategorySlug=N'blended-masala', SubCategorySlug=N'non-veg-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Meat Masala – All-Purpose Meat Blend | WAH! Spices', SeoDescription=N'Buy Meat Masala online from WAH! Spices. An all-purpose blend for meat dishes of every kind, from curries to dry roasts.', ImageUrl=N'/uploads/products/catalog/meat-masala.png', CaloriesPer100g=320, ProteinPer100g=N'13g', FatPer100g=N'12g', CarbsPer100g=N'47g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'meat-masala';

-- Fish Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'fish-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Fish Masala', N'fish-masala', N'A tangy, coastal-style blend for fish curries and fries. Tang from dry mango, along with chilli, turmeric and fenugreek, suits both freshwater and sea fish.', N'Red Chilli, Coriander, Turmeric, Cumin, Dry Mango Powder, Black Pepper, Fenugreek Seeds, Mustard, Garlic, Dry Ginger, Fennel, Salt', N'["Fish curry", "Tawa fish fry", "Prawn masala", "Rava-coated fish marinade"]', N'blended-masala', N'non-veg-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/fish-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Fish Masala – Coastal Blend | WAH! Spices', N'Buy Fish Masala online from WAH! Spices. A tangy, coastal-style blend for fish curries and fries.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-FISH-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-FISH-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-FISH-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-FISH-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A tangy, coastal-style blend for fish curries and fries. Tang from dry mango, along with chilli, turmeric and fenugreek, suits both freshwater and sea fish.', Ingredients=N'Red Chilli, Coriander, Turmeric, Cumin, Dry Mango Powder, Black Pepper, Fenugreek Seeds, Mustard, Garlic, Dry Ginger, Fennel, Salt', Usages=N'["Fish curry", "Tawa fish fry", "Prawn masala", "Rava-coated fish marinade"]', CategorySlug=N'blended-masala', SubCategorySlug=N'non-veg-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Fish Masala – Coastal Blend | WAH! Spices', SeoDescription=N'Buy Fish Masala online from WAH! Spices. A tangy, coastal-style blend for fish curries and fries.', ImageUrl=N'/uploads/products/catalog/fish-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'fish-masala';

-- Egg Curry Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'egg-curry-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Egg Curry Masala', N'egg-curry-masala', N'A punchy blend that makes egg curry a weeknight hero. Onion-tomato gravy plus this masala delivers a dhaba-style anda curry in minutes.', N'Coriander, Red Chilli, Cumin, Turmeric, Black Pepper, Kasuri Methi, Dry Ginger, Garlic, Fennel, Cinnamon, Cloves, Salt', N'["Anda curry", "Egg bhurji", "Masala omelette", "Egg roast"]', N'blended-masala', N'non-veg-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/egg-curry-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Egg Curry Masala – Anda Curry Blend | WAH! Spices', N'Buy Egg Curry Masala online from WAH! Spices. A punchy blend that makes egg curry a weeknight hero.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-EGG-CURRY-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-EGG-CURRY-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-EGG-CURRY-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-EGG-CURRY-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A punchy blend that makes egg curry a weeknight hero. Onion-tomato gravy plus this masala delivers a dhaba-style anda curry in minutes.', Ingredients=N'Coriander, Red Chilli, Cumin, Turmeric, Black Pepper, Kasuri Methi, Dry Ginger, Garlic, Fennel, Cinnamon, Cloves, Salt', Usages=N'["Anda curry", "Egg bhurji", "Masala omelette", "Egg roast"]', CategorySlug=N'blended-masala', SubCategorySlug=N'non-veg-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Egg Curry Masala – Anda Curry Blend | WAH! Spices', SeoDescription=N'Buy Egg Curry Masala online from WAH! Spices. A punchy blend that makes egg curry a weeknight hero.', ImageUrl=N'/uploads/products/catalog/egg-curry-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'egg-curry-masala';

-- Tandoori Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'tandoori-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Tandoori Masala', N'tandoori-masala', N'Smoky, tangy and vibrant red — everything you want from the tandoor at home. Mix with hung curd, lemon and oil for a classic marinade.', N'Kashmiri Red Chilli, Coriander, Cumin, Dry Ginger, Garlic, Dry Mango Powder, Black Pepper, Kasuri Methi, Cinnamon, Cloves, Green Cardamom, Mace, Black Salt, Salt', N'["Tandoori chicken", "Paneer and soya tikka", "Grilled fish", "Tandoori aloo and veggies"]', N'blended-masala', N'non-veg-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/tandoori-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Tandoori Masala – Smoky Tandoor Blend | WAH! Spices', N'Buy Tandoori Masala online from WAH! Spices. Smoky, tangy and vibrant red — everything you want from the tandoor at home.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-TANDOORI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-TANDOORI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-TANDOORI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-TANDOORI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Smoky, tangy and vibrant red — everything you want from the tandoor at home. Mix with hung curd, lemon and oil for a classic marinade.', Ingredients=N'Kashmiri Red Chilli, Coriander, Cumin, Dry Ginger, Garlic, Dry Mango Powder, Black Pepper, Kasuri Methi, Cinnamon, Cloves, Green Cardamom, Mace, Black Salt, Salt', Usages=N'["Tandoori chicken", "Paneer and soya tikka", "Grilled fish", "Tandoori aloo and veggies"]', CategorySlug=N'blended-masala', SubCategorySlug=N'non-veg-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Tandoori Masala – Smoky Tandoor Blend | WAH! Spices', SeoDescription=N'Buy Tandoori Masala online from WAH! Spices. Smoky, tangy and vibrant red — everything you want from the tandoor at home.', ImageUrl=N'/uploads/products/catalog/tandoori-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'tandoori-masala';

-- Biryani Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'biryani-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Biryani Masala', N'biryani-masala', N'A fragrant, layered blend for celebratory biryanis. Mace, star anise, green and black cardamom and shahi jeera bring the unmistakable dum biryani aroma.', N'Coriander, Red Chilli, Cumin, Shahi Jeera, Green Cardamom, Black Cardamom, Cinnamon, Cloves, Star Anise, Mace, Nutmeg, Bay Leaf, Black Pepper, Fennel, Dry Ginger, Salt', N'["Chicken and mutton dum biryani", "Veg and paneer biryani", "Egg biryani", "Biryani-style gravies"]', N'blended-masala', N'rice-masala',
        55, 60, N'50g', N'BoxPack',
        N'Bestseller', N'/uploads/products/catalog/biryani-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Biryani Masala – Authentic Blend | WAH! Spices', N'Buy Biryani Masala online from WAH! Spices. A fragrant, layered blend for celebratory biryanis.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 55, 60, N'WAH-BIRYANI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 115, N'WAH-BIRYANI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 185, 210, N'WAH-BIRYANI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 409, 465, N'WAH-BIRYANI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A fragrant, layered blend for celebratory biryanis. Mace, star anise, green and black cardamom and shahi jeera bring the unmistakable dum biryani aroma.', Ingredients=N'Coriander, Red Chilli, Cumin, Shahi Jeera, Green Cardamom, Black Cardamom, Cinnamon, Cloves, Star Anise, Mace, Nutmeg, Bay Leaf, Black Pepper, Fennel, Dry Ginger, Salt', Usages=N'["Chicken and mutton dum biryani", "Veg and paneer biryani", "Egg biryani", "Biryani-style gravies"]', CategorySlug=N'blended-masala', SubCategorySlug=N'rice-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Biryani Masala – Authentic Blend | WAH! Spices', SeoDescription=N'Buy Biryani Masala online from WAH! Spices. A fragrant, layered blend for celebratory biryanis.', ImageUrl=N'/uploads/products/catalog/biryani-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'biryani-masala';

-- Pulao Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'pulao-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Pulao Masala', N'pulao-masala', N'A light, aromatic blend for fragrant pulao. Whole-spice flavours of cinnamon, clove and cardamom perfume the rice without heavy heat.', N'Coriander, Cumin, Shahi Jeera, Cinnamon, Cloves, Green Cardamom, Black Pepper, Bay Leaf, Star Anise, Fennel, Mace, Dry Ginger, Salt', N'["Veg pulao", "Matar and jeera pulao", "Yakhni and chicken pulao", "Masala rice"]', N'blended-masala', N'rice-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/pulao-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Pulao Masala – Fragrant Rice Blend | WAH! Spices', N'Buy Pulao Masala online from WAH! Spices. A light, aromatic blend for fragrant pulao.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-PULAO-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-PULAO-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-PULAO-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-PULAO-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A light, aromatic blend for fragrant pulao. Whole-spice flavours of cinnamon, clove and cardamom perfume the rice without heavy heat.', Ingredients=N'Coriander, Cumin, Shahi Jeera, Cinnamon, Cloves, Green Cardamom, Black Pepper, Bay Leaf, Star Anise, Fennel, Mace, Dry Ginger, Salt', Usages=N'["Veg pulao", "Matar and jeera pulao", "Yakhni and chicken pulao", "Masala rice"]', CategorySlug=N'blended-masala', SubCategorySlug=N'rice-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Pulao Masala – Fragrant Rice Blend | WAH! Spices', SeoDescription=N'Buy Pulao Masala online from WAH! Spices. A light, aromatic blend for fragrant pulao.', ImageUrl=N'/uploads/products/catalog/pulao-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'pulao-masala';

-- Pav Bhaji Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'pav-bhaji-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Pav Bhaji Masala', N'pav-bhaji-masala', N'The taste of Mumbai street-side tawas in a box. A deep red, tangy blend with Kashmiri chilli, dry mango and black salt that makes bhaji buttery and irresistible.', N'Kashmiri Red Chilli, Coriander, Cumin, Fennel, Dry Mango Powder, Black Salt, Black Pepper, Black Cardamom, Cinnamon, Cloves, Star Anise, Dry Ginger, Bay Leaf, Salt', N'["Pav bhaji", "Masala pav", "Tawa pulao", "Tawa vegetables"]', N'blended-masala', N'snacks-street-masala',
        45, 50, N'50g', N'BoxPack',
        N'Bestseller', N'/uploads/products/catalog/pav-bhaji-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Pav Bhaji Masala – Mumbai Street Blend | WAH! Spices', N'Buy Pav Bhaji Masala online from WAH! Spices. The taste of Mumbai street-side tawas in a box.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-PAV-BHAJI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 85, 95, N'WAH-PAV-BHAJI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 155, 175, N'WAH-PAV-BHAJI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 349, 395, N'WAH-PAV-BHAJI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'The taste of Mumbai street-side tawas in a box. A deep red, tangy blend with Kashmiri chilli, dry mango and black salt that makes bhaji buttery and irresistible.', Ingredients=N'Kashmiri Red Chilli, Coriander, Cumin, Fennel, Dry Mango Powder, Black Salt, Black Pepper, Black Cardamom, Cinnamon, Cloves, Star Anise, Dry Ginger, Bay Leaf, Salt', Usages=N'["Pav bhaji", "Masala pav", "Tawa pulao", "Tawa vegetables"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Pav Bhaji Masala – Mumbai Street Blend | WAH! Spices', SeoDescription=N'Buy Pav Bhaji Masala online from WAH! Spices. The taste of Mumbai street-side tawas in a box.', ImageUrl=N'/uploads/products/catalog/pav-bhaji-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'pav-bhaji-masala';

-- Pani Puri Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'pani-puri-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Pani Puri Masala', N'pani-puri-masala', N'A zingy, cooling blend for the perfect pani. Mint, black salt, roasted cumin and dry mango create a tangy, spicy pani puri water in minutes.', N'Black Salt, Dry Mango Powder, Roasted Cumin, Mint Leaves, Black Pepper, Red Chilli, Dry Ginger, Coriander, Asafoetida, Citric Acid, Salt', N'["Pani puri / golgappa water", "Masala soda and shikanji", "Sprinkle on fruit chaat", "Tangy buttermilk"]', N'blended-masala', N'snacks-street-masala',
        39, 45, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/pani-puri-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Pani Puri Masala – Tangy Pani Blend | WAH! Spices', N'Buy Pani Puri Masala online from WAH! Spices. A zingy, cooling blend for the perfect pani.',
        210, N'7g', N'4g', N'38g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 39, 45, N'WAH-PANI-PURI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 75, 85, N'WAH-PANI-PURI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 139, 160, N'WAH-PANI-PURI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 309, 350, N'WAH-PANI-PURI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A zingy, cooling blend for the perfect pani. Mint, black salt, roasted cumin and dry mango create a tangy, spicy pani puri water in minutes.', Ingredients=N'Black Salt, Dry Mango Powder, Roasted Cumin, Mint Leaves, Black Pepper, Red Chilli, Dry Ginger, Coriander, Asafoetida, Citric Acid, Salt', Usages=N'["Pani puri / golgappa water", "Masala soda and shikanji", "Sprinkle on fruit chaat", "Tangy buttermilk"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Pani Puri Masala – Tangy Pani Blend | WAH! Spices', SeoDescription=N'Buy Pani Puri Masala online from WAH! Spices. A zingy, cooling blend for the perfect pani.', ImageUrl=N'/uploads/products/catalog/pani-puri-masala.png', CaloriesPer100g=210, ProteinPer100g=N'7g', FatPer100g=N'4g', CarbsPer100g=N'38g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'pani-puri-masala';

-- Chaat Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chaat-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chaat Masala', N'chaat-masala', N'The tangy, salty sprinkle that brightens almost anything. Black salt, amchur and roasted cumin give fruits, snacks and salads an instant chatpata kick.', N'Black Salt, Dry Mango Powder, Roasted Cumin, Coriander, Black Pepper, Red Chilli, Dry Ginger, Mint, Ajwain, Asafoetida, Salt', N'["Fruit and aloo chaat", "Salads and raita", "Sprinkle on fries and corn", "Dahi vada and pakoras"]', N'blended-masala', N'snacks-street-masala',
        45, 50, N'50g', N'BoxPack',
        N'Bestseller', N'/uploads/products/catalog/chaat-masala.png', 0, 0, 1, 1, 0, N'09109100', 5, N'Chaat Masala – Tangy Sprinkle | WAH! Spices', N'Buy Chaat Masala online from WAH! Spices. The tangy, salty sprinkle that brightens almost anything.',
        210, N'7g', N'4g', N'38g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-CHAAT-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-CHAAT-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-CHAAT-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-CHAAT-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'The tangy, salty sprinkle that brightens almost anything. Black salt, amchur and roasted cumin give fruits, snacks and salads an instant chatpata kick.', Ingredients=N'Black Salt, Dry Mango Powder, Roasted Cumin, Coriander, Black Pepper, Red Chilli, Dry Ginger, Mint, Ajwain, Asafoetida, Salt', Usages=N'["Fruit and aloo chaat", "Salads and raita", "Sprinkle on fries and corn", "Dahi vada and pakoras"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Chaat Masala – Tangy Sprinkle | WAH! Spices', SeoDescription=N'Buy Chaat Masala online from WAH! Spices. The tangy, salty sprinkle that brightens almost anything.', ImageUrl=N'/uploads/products/catalog/chaat-masala.png', CaloriesPer100g=210, ProteinPer100g=N'7g', FatPer100g=N'4g', CarbsPer100g=N'38g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chaat-masala';

-- Sandwich Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sandwich-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sandwich Masala', N'sandwich-masala', N'A light, tangy-spicy sprinkle designed for Bombay-style sandwiches. Dust it over buttered bread, cucumber, tomato and potato for street-cart flavour.', N'Black Salt, Roasted Cumin, Dry Mango Powder, Black Pepper, Coriander, Red Chilli, Dry Ginger, Mint, Salt', N'["Bombay veg sandwich", "Grilled cheese toasties", "Salads and sprouts", "Buttered corn"]', N'blended-masala', N'snacks-street-masala',
        45, 50, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/sandwich-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Sandwich Masala – Street Sandwich Sprinkle | WAH! Spices', N'Buy Sandwich Masala online from WAH! Spices. A light, tangy-spicy sprinkle designed for Bombay-style sandwiches.',
        210, N'7g', N'4g', N'38g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-SANDWICH-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-SANDWICH-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-SANDWICH-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-SANDWICH-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A light, tangy-spicy sprinkle designed for Bombay-style sandwiches. Dust it over buttered bread, cucumber, tomato and potato for street-cart flavour.', Ingredients=N'Black Salt, Roasted Cumin, Dry Mango Powder, Black Pepper, Coriander, Red Chilli, Dry Ginger, Mint, Salt', Usages=N'["Bombay veg sandwich", "Grilled cheese toasties", "Salads and sprouts", "Buttered corn"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Sandwich Masala – Street Sandwich Sprinkle | WAH! Spices', SeoDescription=N'Buy Sandwich Masala online from WAH! Spices. A light, tangy-spicy sprinkle designed for Bombay-style sandwiches.', ImageUrl=N'/uploads/products/catalog/sandwich-masala.png', CaloriesPer100g=210, ProteinPer100g=N'7g', FatPer100g=N'4g', CarbsPer100g=N'38g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sandwich-masala';

-- Fries Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'fries-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Fries Masala', N'fries-masala', N'A moreish seasoning for fries, wedges and chips. Paprika-style chilli, garlic, onion and a little tang give a crowd-pleasing snack shop flavour.', N'Salt, Kashmiri Red Chilli, Garlic Powder, Onion Powder, Dry Mango Powder, Black Pepper, Oregano, Sugar, Citric Acid', N'["French fries and wedges", "Popcorn", "Makhana and nachos", "Roasted potatoes"]', N'blended-masala', N'snacks-street-masala',
        55, 60, N'50g', N'BoxPack',
        N'New', N'/uploads/products/catalog/fries-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Fries Masala – Snack Seasoning | WAH! Spices', N'Buy Fries Masala online from WAH! Spices. A moreish seasoning for fries, wedges and chips.',
        210, N'7g', N'4g', N'38g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 55, 60, N'WAH-FRIES-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 115, N'WAH-FRIES-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 185, 210, N'WAH-FRIES-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 409, 465, N'WAH-FRIES-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A moreish seasoning for fries, wedges and chips. Paprika-style chilli, garlic, onion and a little tang give a crowd-pleasing snack shop flavour.', Ingredients=N'Salt, Kashmiri Red Chilli, Garlic Powder, Onion Powder, Dry Mango Powder, Black Pepper, Oregano, Sugar, Citric Acid', Usages=N'["French fries and wedges", "Popcorn", "Makhana and nachos", "Roasted potatoes"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Fries Masala – Snack Seasoning | WAH! Spices', SeoDescription=N'Buy Fries Masala online from WAH! Spices. A moreish seasoning for fries, wedges and chips.', ImageUrl=N'/uploads/products/catalog/fries-masala.png', CaloriesPer100g=210, ProteinPer100g=N'7g', FatPer100g=N'4g', CarbsPer100g=N'38g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'fries-masala';

-- Peri Peri Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'peri-peri-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Peri Peri Masala', N'peri-peri-masala', N'A fiery, garlicky African-style seasoning with an Indian twist. Bird''s eye-style chilli heat, garlic, oregano and a citrusy tang for fries, wings and wraps.', N'Red Chilli, Salt, Garlic Powder, Onion Powder, Paprika, Oregano, Black Pepper, Dry Ginger, Sugar, Citric Acid', N'["Peri peri fries", "Grilled chicken and paneer", "Wraps, pizzas and burgers", "Makhana and popcorn"]', N'blended-masala', N'snacks-street-masala',
        59, 69, N'50g', N'BoxPack',
        N'New', N'/uploads/products/catalog/peri-peri-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Peri Peri Masala – Fiery Seasoning | WAH! Spices', N'Buy Peri Peri Masala online from WAH! Spices. A fiery, garlicky African-style seasoning with an Indian twist.',
        320, N'13g', N'12g', N'47g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 59, 69, N'WAH-PERI-PERI-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 109, 125, N'WAH-PERI-PERI-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 199, 225, N'WAH-PERI-PERI-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 449, 510, N'WAH-PERI-PERI-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A fiery, garlicky African-style seasoning with an Indian twist. Bird''s eye-style chilli heat, garlic, oregano and a citrusy tang for fries, wings and wraps.', Ingredients=N'Red Chilli, Salt, Garlic Powder, Onion Powder, Paprika, Oregano, Black Pepper, Dry Ginger, Sugar, Citric Acid', Usages=N'["Peri peri fries", "Grilled chicken and paneer", "Wraps, pizzas and burgers", "Makhana and popcorn"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Peri Peri Masala – Fiery Seasoning | WAH! Spices', SeoDescription=N'Buy Peri Peri Masala online from WAH! Spices. A fiery, garlicky African-style seasoning with an Indian twist.', ImageUrl=N'/uploads/products/catalog/peri-peri-masala.png', CaloriesPer100g=320, ProteinPer100g=N'13g', FatPer100g=N'12g', CarbsPer100g=N'47g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'peri-peri-masala';

-- Chilli Garlic Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chilli-garlic-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chilli Garlic Masala', N'chilli-garlic-masala', N'A punchy blend of roasted garlic and red chilli for Indo-Chinese style heat. Brilliant on noodles, fried rice and snacks.', N'Red Chilli, Garlic, Salt, Onion, Black Pepper, Dry Ginger, Coriander, Sugar, Citric Acid', N'["Chilli garlic noodles", "Fried rice and manchurian", "Garlic bread and pizza", "Stir-fried veggies"]', N'blended-masala', N'snacks-street-masala',
        55, 60, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/chilli-garlic-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Chilli Garlic Masala – Indo-Chinese Blend | WAH! Spices', N'Buy Chilli Garlic Masala online from WAH! Spices. A punchy blend of roasted garlic and red chilli for Indo-Chinese style heat.',
        320, N'13g', N'12g', N'47g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 55, 60, N'WAH-CHILLI-GARLIC-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 115, N'WAH-CHILLI-GARLIC-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 185, 210, N'WAH-CHILLI-GARLIC-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 409, 465, N'WAH-CHILLI-GARLIC-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A punchy blend of roasted garlic and red chilli for Indo-Chinese style heat. Brilliant on noodles, fried rice and snacks.', Ingredients=N'Red Chilli, Garlic, Salt, Onion, Black Pepper, Dry Ginger, Coriander, Sugar, Citric Acid', Usages=N'["Chilli garlic noodles", "Fried rice and manchurian", "Garlic bread and pizza", "Stir-fried veggies"]', CategorySlug=N'blended-masala', SubCategorySlug=N'snacks-street-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Chilli Garlic Masala – Indo-Chinese Blend | WAH! Spices', SeoDescription=N'Buy Chilli Garlic Masala online from WAH! Spices. A punchy blend of roasted garlic and red chilli for Indo-Chinese style heat.', ImageUrl=N'/uploads/products/catalog/chilli-garlic-masala.png', CaloriesPer100g=320, ProteinPer100g=N'13g', FatPer100g=N'12g', CarbsPer100g=N'47g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chilli-garlic-masala';

-- Chaas Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chaas-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chaas Masala', N'chaas-masala', N'A cooling, digestive-style blend for buttermilk. Roasted cumin, black salt, dry ginger and mint turn plain chaas into a refreshing summer drink.', N'Black Salt, Roasted Cumin, Dry Ginger, Black Pepper, Mint Leaves, Coriander, Ajwain, Asafoetida, Salt', N'["Masala chaas", "Raita and dahi", "Lassi (salted)", "Sprinkle on salads"]', N'blended-masala', N'beverage-masala',
        39, 45, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/chaas-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Chaas Masala – Buttermilk Blend | WAH! Spices', N'Buy Chaas Masala online from WAH! Spices. A cooling, digestive-style blend for buttermilk.',
        210, N'7g', N'4g', N'38g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 39, 45, N'WAH-CHAAS-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 75, 85, N'WAH-CHAAS-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 139, 160, N'WAH-CHAAS-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 309, 350, N'WAH-CHAAS-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A cooling, digestive-style blend for buttermilk. Roasted cumin, black salt, dry ginger and mint turn plain chaas into a refreshing summer drink.', Ingredients=N'Black Salt, Roasted Cumin, Dry Ginger, Black Pepper, Mint Leaves, Coriander, Ajwain, Asafoetida, Salt', Usages=N'["Masala chaas", "Raita and dahi", "Lassi (salted)", "Sprinkle on salads"]', CategorySlug=N'blended-masala', SubCategorySlug=N'beverage-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Chaas Masala – Buttermilk Blend | WAH! Spices', SeoDescription=N'Buy Chaas Masala online from WAH! Spices. A cooling, digestive-style blend for buttermilk.', ImageUrl=N'/uploads/products/catalog/chaas-masala.png', CaloriesPer100g=210, ProteinPer100g=N'7g', FatPer100g=N'4g', CarbsPer100g=N'38g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chaas-masala';

-- Jaljeera Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'jaljeera-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Jaljeera Masala', N'jaljeera-masala', N'A tangy, cumin-forward blend for traditional jaljeera. Mix with chilled water, lemon and mint for a zesty summer cooler.', N'Black Salt, Roasted Cumin, Dry Mango Powder, Mint Leaves, Black Pepper, Dry Ginger, Tamarind, Citric Acid, Asafoetida, Salt', N'["Jaljeera drink", "Shikanji and nimbu pani", "Pani puri water", "Fruit chaat sprinkle"]', N'blended-masala', N'beverage-masala',
        39, 45, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/jaljeera-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Jaljeera Masala – Summer Cooler Blend | WAH! Spices', N'Buy Jaljeera Masala online from WAH! Spices. A tangy, cumin-forward blend for traditional jaljeera.',
        210, N'7g', N'4g', N'38g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 39, 45, N'WAH-JALJEERA-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 75, 85, N'WAH-JALJEERA-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 139, 160, N'WAH-JALJEERA-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 309, 350, N'WAH-JALJEERA-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A tangy, cumin-forward blend for traditional jaljeera. Mix with chilled water, lemon and mint for a zesty summer cooler.', Ingredients=N'Black Salt, Roasted Cumin, Dry Mango Powder, Mint Leaves, Black Pepper, Dry Ginger, Tamarind, Citric Acid, Asafoetida, Salt', Usages=N'["Jaljeera drink", "Shikanji and nimbu pani", "Pani puri water", "Fruit chaat sprinkle"]', CategorySlug=N'blended-masala', SubCategorySlug=N'beverage-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Jaljeera Masala – Summer Cooler Blend | WAH! Spices', SeoDescription=N'Buy Jaljeera Masala online from WAH! Spices. A tangy, cumin-forward blend for traditional jaljeera.', ImageUrl=N'/uploads/products/catalog/jaljeera-masala.png', CaloriesPer100g=210, ProteinPer100g=N'7g', FatPer100g=N'4g', CarbsPer100g=N'38g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'jaljeera-masala';

-- Undhyu Masala
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'undhyu-masala')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Undhyu Masala', N'undhyu-masala', N'A Gujarati specialty blend for the winter favourite undhiyu. Coriander, sesame, ajwain and a hint of sweetness season the mixed vegetables and methi muthia.', N'Coriander, Cumin, Red Chilli, Sesame Seeds, Ajwain, Turmeric, Fennel, Dry Ginger, Cinnamon, Cloves, Asafoetida, Sugar, Salt', N'["Surti undhiyu", "Methi muthia", "Stuffed baby brinjal and potato", "Oondhiyu-style mixed veg"]', N'blended-masala', N'regional-masala',
        49, 55, N'50g', N'BoxPack',
        NULL, N'/uploads/products/catalog/undhyu-masala.png', 0, 0, 1, 0, 0, N'09109100', 5, N'Undhyu Masala – Gujarati Specialty | WAH! Spices', N'Buy Undhyu Masala online from WAH! Spices. A Gujarati specialty blend for the winter favourite undhiyu.',
        330, N'12g', N'11g', N'48g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-UNDHYU-MASALA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 95, 109, N'WAH-UNDHYU-MASALA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 175, 200, N'WAH-UNDHYU-MASALA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 389, 440, N'WAH-UNDHYU-MASALA-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'A Gujarati specialty blend for the winter favourite undhiyu. Coriander, sesame, ajwain and a hint of sweetness season the mixed vegetables and methi muthia.', Ingredients=N'Coriander, Cumin, Red Chilli, Sesame Seeds, Ajwain, Turmeric, Fennel, Dry Ginger, Cinnamon, Cloves, Asafoetida, Sugar, Salt', Usages=N'["Surti undhiyu", "Methi muthia", "Stuffed baby brinjal and potato", "Oondhiyu-style mixed veg"]', CategorySlug=N'blended-masala', SubCategorySlug=N'regional-masala', PackType=N'BoxPack', HsnCode=N'09109100', GstRate=5, SeoTitle=N'Undhyu Masala – Gujarati Specialty | WAH! Spices', SeoDescription=N'Buy Undhyu Masala online from WAH! Spices. A Gujarati specialty blend for the winter favourite undhiyu.', ImageUrl=N'/uploads/products/catalog/undhyu-masala.png', CaloriesPer100g=330, ProteinPer100g=N'12g', FatPer100g=N'11g', CarbsPer100g=N'48g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'undhyu-masala';

-- Chilli Flakes
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chilli-flakes')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chilli Flakes', N'chilli-flakes', N'Crushed red chillies with their seeds, for a lively burst of heat and colour. Sprinkle straight from the jar over pizza, pasta and stir-fries.', N'100% Crushed Red Chilli', N'["Pizza and pasta", "Garlic bread", "Stir-fries and noodles", "Infused chilli oil"]', N'herbs-seasoning', NULL,
        59, 69, N'20g', N'GlassJar',
        N'New', N'/uploads/products/catalog/chilli-flakes.png', 0, 0, 1, 0, 0, N'09042212', 5, N'Chilli Flakes – Crushed Red Chilli | WAH! Spices', N'Buy Chilli Flakes online from WAH! Spices. Crushed red chillies with their seeds, for a lively burst of heat and colour.',
        318, N'12g', N'17g', N'57g', N'27g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 59, 69, N'WAH-CHILLI-FLAKES-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 135, 155, N'WAH-CHILLI-FLAKES-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 249, 285, N'WAH-CHILLI-FLAKES-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Crushed red chillies with their seeds, for a lively burst of heat and colour. Sprinkle straight from the jar over pizza, pasta and stir-fries.', Ingredients=N'100% Crushed Red Chilli', Usages=N'["Pizza and pasta", "Garlic bread", "Stir-fries and noodles", "Infused chilli oil"]', CategorySlug=N'herbs-seasoning', SubCategorySlug=NULL, PackType=N'GlassJar', HsnCode=N'09042212', GstRate=5, SeoTitle=N'Chilli Flakes – Crushed Red Chilli | WAH! Spices', SeoDescription=N'Buy Chilli Flakes online from WAH! Spices. Crushed red chillies with their seeds, for a lively burst of heat and colour.', ImageUrl=N'/uploads/products/catalog/chilli-flakes.png', CaloriesPer100g=318, ProteinPer100g=N'12g', FatPer100g=N'17g', CarbsPer100g=N'57g', FiberPer100g=N'27g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chilli-flakes';

-- Oregano
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'oregano')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Oregano', N'oregano', N'Dried oregano leaves with a warm, slightly peppery Mediterranean aroma. The essential finishing herb for pizzas, pastas and garlic breads.', N'100% Dried Oregano Leaves', N'["Pizza and pasta", "Garlic bread", "Salad dressings", "Herb butter and dips"]', N'herbs-seasoning', NULL,
        95, 109, N'20g', N'GlassJar',
        N'New', N'/uploads/products/catalog/oregano.png', 0, 0, 1, 0, 0, N'12119099', 5, N'Oregano – Dried Herb | WAH! Spices', N'Buy Oregano online from WAH! Spices. Dried oregano leaves with a warm, slightly peppery Mediterranean aroma.',
        265, N'9g', N'4g', N'69g', N'43g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 95, 109, N'WAH-OREGANO-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 215, 245, N'WAH-OREGANO-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Dried oregano leaves with a warm, slightly peppery Mediterranean aroma. The essential finishing herb for pizzas, pastas and garlic breads.', Ingredients=N'100% Dried Oregano Leaves', Usages=N'["Pizza and pasta", "Garlic bread", "Salad dressings", "Herb butter and dips"]', CategorySlug=N'herbs-seasoning', SubCategorySlug=NULL, PackType=N'GlassJar', HsnCode=N'12119099', GstRate=5, SeoTitle=N'Oregano – Dried Herb | WAH! Spices', SeoDescription=N'Buy Oregano online from WAH! Spices. Dried oregano leaves with a warm, slightly peppery Mediterranean aroma.', ImageUrl=N'/uploads/products/catalog/oregano.png', CaloriesPer100g=265, ProteinPer100g=N'9g', FatPer100g=N'4g', CarbsPer100g=N'69g', FiberPer100g=N'43g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'oregano';

-- Ajmod Beej
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'ajmod-beej')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Ajmod Beej', N'ajmod-beej', N'Ajmod Beej (celery seeds) are tiny, earthy seeds with a concentrated celery aroma. A little goes a long way in pickles, soups and spice blends.', N'100% Pure Ajmod Beej (Celery Seeds)', N'["Pickles and achaar masala", "Soups and broths", "Salad dressings", "Homemade spice blends"]', N'whole-spices', NULL,
        55, 60, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/ajmod-beej.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Ajmod Beej (Celery Seeds) | WAH! Spices', N'Buy Ajmod Beej online from WAH! Spices. Ajmod Beej (celery seeds) are tiny, earthy seeds with a concentrated celery aroma.',
        392, N'18g', N'25g', N'41g', N'12g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 55, 60, N'WAH-AJMOD-BEEJ-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 115, N'WAH-AJMOD-BEEJ-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 185, 210, N'WAH-AJMOD-BEEJ-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Ajmod Beej (celery seeds) are tiny, earthy seeds with a concentrated celery aroma. A little goes a long way in pickles, soups and spice blends.', Ingredients=N'100% Pure Ajmod Beej (Celery Seeds)', Usages=N'["Pickles and achaar masala", "Soups and broths", "Salad dressings", "Homemade spice blends"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Ajmod Beej (Celery Seeds) | WAH! Spices', SeoDescription=N'Buy Ajmod Beej online from WAH! Spices. Ajmod Beej (celery seeds) are tiny, earthy seeds with a concentrated celery aroma.', ImageUrl=N'/uploads/products/catalog/ajmod-beej.png', CaloriesPer100g=392, ProteinPer100g=N'18g', FatPer100g=N'25g', CarbsPer100g=N'41g', FiberPer100g=N'12g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'ajmod-beej';

-- Ajwain
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'ajwain')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Ajwain', N'ajwain', N'Ajwain (carom seeds) have a sharp, thyme-like flavour that blooms in hot oil. A staple in parathas, pakoras and tadkas, traditionally added to heavier dishes.', N'100% Pure Ajwain (Carom Seeds)', N'["Ajwain parathas and puris", "Pakora and bhajiya batter", "Dal and kadhi tadka", "Namak para and mathri"]', N'whole-spices', NULL,
        79, 90, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/ajwain.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Ajwain (Carom Seeds) – Whole | WAH! Spices', N'Buy Ajwain online from WAH! Spices. Ajwain (carom seeds) have a sharp, thyme-like flavour that blooms in hot oil.',
        305, N'16g', N'25g', N'43g', N'39g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 79, 90, N'WAH-AJWAIN-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-AJWAIN-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-AJWAIN-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 599, 680, N'WAH-AJWAIN-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Ajwain (carom seeds) have a sharp, thyme-like flavour that blooms in hot oil. A staple in parathas, pakoras and tadkas, traditionally added to heavier dishes.', Ingredients=N'100% Pure Ajwain (Carom Seeds)', Usages=N'["Ajwain parathas and puris", "Pakora and bhajiya batter", "Dal and kadhi tadka", "Namak para and mathri"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Ajwain (Carom Seeds) – Whole | WAH! Spices', SeoDescription=N'Buy Ajwain online from WAH! Spices. Ajwain (carom seeds) have a sharp, thyme-like flavour that blooms in hot oil.', ImageUrl=N'/uploads/products/catalog/ajwain.png', CaloriesPer100g=305, ProteinPer100g=N'16g', FatPer100g=N'25g', CarbsPer100g=N'43g', FiberPer100g=N'39g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'ajwain';

-- Badi Elaichi
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'badi-elaichi')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Badi Elaichi', N'badi-elaichi', N'Badi Elaichi (black cardamom) brings a deep, smoky, camphor-like aroma to rich gravies. Its large, wrinkled pods are essential in biryanis, chole and garam masala.', N'100% Pure Badi Elaichi (Black Cardamom)', N'["Biryani and pulao", "Chole and rajma", "Homemade garam masala", "Meat curries and yakhni"]', N'whole-spices', NULL,
        75, 85, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/badi-elaichi.png', 0, 0, 1, 0, 0, N'09083110', 5, N'Badi Elaichi (Black Cardamom) | WAH! Spices', N'Buy Badi Elaichi online from WAH! Spices. Badi Elaichi (black cardamom) brings a deep, smoky, camphor-like aroma to rich gravies.',
        311, N'11g', N'7g', N'68g', N'28g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 75, 85, N'WAH-BADI-ELAICHI-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 165, 190, N'WAH-BADI-ELAICHI-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 299, 340, N'WAH-BADI-ELAICHI-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 549, 625, N'WAH-BADI-ELAICHI-200G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Badi Elaichi (black cardamom) brings a deep, smoky, camphor-like aroma to rich gravies. Its large, wrinkled pods are essential in biryanis, chole and garam masala.', Ingredients=N'100% Pure Badi Elaichi (Black Cardamom)', Usages=N'["Biryani and pulao", "Chole and rajma", "Homemade garam masala", "Meat curries and yakhni"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09083110', GstRate=5, SeoTitle=N'Badi Elaichi (Black Cardamom) | WAH! Spices', SeoDescription=N'Buy Badi Elaichi online from WAH! Spices. Badi Elaichi (black cardamom) brings a deep, smoky, camphor-like aroma to rich gravies.', ImageUrl=N'/uploads/products/catalog/badi-elaichi.png', CaloriesPer100g=311, ProteinPer100g=N'11g', FatPer100g=N'7g', CarbsPer100g=N'68g', FiberPer100g=N'28g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'badi-elaichi';

-- Black Pepper
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'black-pepper')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Black Pepper', N'black-pepper', N'Bold, Malabar-style whole black pepper with a sharp, pungent bite. Freshly crack it for the best aroma in everything from curries to salads.', N'100% Pure Black Pepper (Kali Mirch)', N'["Freshly cracked over eggs and salads", "Rasam and pepper chicken", "Homemade garam masala", "Soups and kadha"]', N'whole-spices', NULL,
        45, 50, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/black-pepper.png', 0, 0, 1, 0, 0, N'09041130', 5, N'Black Pepper (Kali Mirch) – Whole | WAH! Spices', N'Buy Black Pepper online from WAH! Spices. Bold, Malabar-style whole black pepper with a sharp, pungent bite.',
        251, N'10g', N'3g', N'64g', N'25g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 45, 50, N'WAH-BLACK-PEPPER-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 99, 115, N'WAH-BLACK-PEPPER-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 179, 205, N'WAH-BLACK-PEPPER-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 329, 375, N'WAH-BLACK-PEPPER-200G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Bold, Malabar-style whole black pepper with a sharp, pungent bite. Freshly crack it for the best aroma in everything from curries to salads.', Ingredients=N'100% Pure Black Pepper (Kali Mirch)', Usages=N'["Freshly cracked over eggs and salads", "Rasam and pepper chicken", "Homemade garam masala", "Soups and kadha"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09041130', GstRate=5, SeoTitle=N'Black Pepper (Kali Mirch) – Whole | WAH! Spices', SeoDescription=N'Buy Black Pepper online from WAH! Spices. Bold, Malabar-style whole black pepper with a sharp, pungent bite.', ImageUrl=N'/uploads/products/catalog/black-pepper.png', CaloriesPer100g=251, ProteinPer100g=N'10g', FatPer100g=N'3g', CarbsPer100g=N'64g', FiberPer100g=N'25g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'black-pepper';

-- Chakri Phool
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'chakri-phool')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Chakri Phool', N'chakri-phool', N'Chakri Phool (star anise) is a striking star-shaped pod with a sweet, licorice-like aroma. A key note in biryani, pulao and garam masala.', N'100% Pure Chakri Phool (Star Anise)', N'["Biryani and pulao", "Garam masala blends", "Masala chai", "Slow-cooked meat gravies"]', N'whole-spices', NULL,
        49, 55, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/chakri-phool.png', 0, 0, 1, 0, 0, N'09096120', 5, N'Chakri Phool (Star Anise) | WAH! Spices', N'Buy Chakri Phool online from WAH! Spices. Chakri Phool (star anise) is a striking star-shaped pod with a sweet, licorice-like aroma.',
        337, N'18g', N'16g', N'50g', N'15g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 49, 55, N'WAH-CHAKRI-PHOOL-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 109, 125, N'WAH-CHAKRI-PHOOL-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 199, 225, N'WAH-CHAKRI-PHOOL-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 365, 415, N'WAH-CHAKRI-PHOOL-200G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Chakri Phool (star anise) is a striking star-shaped pod with a sweet, licorice-like aroma. A key note in biryani, pulao and garam masala.', Ingredients=N'100% Pure Chakri Phool (Star Anise)', Usages=N'["Biryani and pulao", "Garam masala blends", "Masala chai", "Slow-cooked meat gravies"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09096120', GstRate=5, SeoTitle=N'Chakri Phool (Star Anise) | WAH! Spices', SeoDescription=N'Buy Chakri Phool online from WAH! Spices. Chakri Phool (star anise) is a striking star-shaped pod with a sweet, licorice-like aroma.', ImageUrl=N'/uploads/products/catalog/chakri-phool.png', CaloriesPer100g=337, ProteinPer100g=N'18g', FatPer100g=N'16g', CarbsPer100g=N'50g', FiberPer100g=N'15g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'chakri-phool';

-- Dagad Phool
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dagad-phool')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dagad Phool', N'dagad-phool', N'Dagad Phool (black stone flower) is a dried lichen with an earthy, woody aroma that comes alive when fried in oil. It is the secret ingredient of Chettinad masala and Maharashtrian goda masala.', N'100% Pure Dagad Phool (Black Stone Flower)', N'["Chettinad chicken masala", "Goda and kala masala", "Biryani spice mix", "Meat curries"]', N'whole-spices', NULL,
        49, 55, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dagad-phool.png', 0, 0, 1, 0, 0, N'12119099', 5, N'Dagad Phool (Black Stone Flower) | WAH! Spices', N'Buy Dagad Phool online from WAH! Spices. Dagad Phool (black stone flower) is a dried lichen with an earthy, woody aroma that comes alive when fried in oil.',
        250, N'5g', N'2g', N'55g', N'30g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 49, 55, N'WAH-DAGAD-PHOOL-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 109, 125, N'WAH-DAGAD-PHOOL-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 199, 225, N'WAH-DAGAD-PHOOL-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Dagad Phool (black stone flower) is a dried lichen with an earthy, woody aroma that comes alive when fried in oil. It is the secret ingredient of Chettinad masala and Maharashtrian goda masala.', Ingredients=N'100% Pure Dagad Phool (Black Stone Flower)', Usages=N'["Chettinad chicken masala", "Goda and kala masala", "Biryani spice mix", "Meat curries"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12119099', GstRate=5, SeoTitle=N'Dagad Phool (Black Stone Flower) | WAH! Spices', SeoDescription=N'Buy Dagad Phool online from WAH! Spices. Dagad Phool (black stone flower) is a dried lichen with an earthy, woody aroma that comes alive when fried in oil.', ImageUrl=N'/uploads/products/catalog/dagad-phool.png', CaloriesPer100g=250, ProteinPer100g=N'5g', FatPer100g=N'2g', CarbsPer100g=N'55g', FiberPer100g=N'30g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dagad-phool';

-- Dalchini
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dalchini')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dalchini', N'dalchini', N'Dalchini (cinnamon/cassia bark) quills with a sweet, warm, woody aroma. Use whole in tadkas and rice, or grind fresh for masalas and desserts.', N'100% Pure Dalchini (Cinnamon Bark)', N'["Biryani and pulao", "Garam masala", "Masala chai and kadha", "Cakes, kheer and desserts"]', N'whole-spices', NULL,
        69, 79, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dalchini.png', 0, 0, 1, 0, 0, N'09061910', 5, N'Dalchini (Cinnamon) – Whole Bark | WAH! Spices', N'Buy Dalchini online from WAH! Spices. Dalchini (cinnamon/cassia bark) quills with a sweet, warm, woody aroma.',
        247, N'4g', N'1g', N'81g', N'53g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 69, 79, N'WAH-DALCHINI-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 129, 145, N'WAH-DALCHINI-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 239, 270, N'WAH-DALCHINI-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 529, 600, N'WAH-DALCHINI-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Dalchini (cinnamon/cassia bark) quills with a sweet, warm, woody aroma. Use whole in tadkas and rice, or grind fresh for masalas and desserts.', Ingredients=N'100% Pure Dalchini (Cinnamon Bark)', Usages=N'["Biryani and pulao", "Garam masala", "Masala chai and kadha", "Cakes, kheer and desserts"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09061910', GstRate=5, SeoTitle=N'Dalchini (Cinnamon) – Whole Bark | WAH! Spices', SeoDescription=N'Buy Dalchini online from WAH! Spices. Dalchini (cinnamon/cassia bark) quills with a sweet, warm, woody aroma.', ImageUrl=N'/uploads/products/catalog/dalchini.png', CaloriesPer100g=247, ProteinPer100g=N'4g', FatPer100g=N'1g', CarbsPer100g=N'81g', FiberPer100g=N'53g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dalchini';

-- Dhania
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dhania')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dhania', N'dhania', N'Whole Dhania (coriander seeds) with a citrusy, mildly sweet aroma. Dry-roast and grind for the freshest coriander powder, or crush coarsely for kadai dishes.', N'100% Pure Dhania (Coriander Seeds)', N'["Freshly ground coriander powder", "Kadai masala", "Pickles", "Sambhar and rasam powder"]', N'whole-spices', NULL,
        45, 50, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dhania.png', 0, 0, 1, 0, 0, N'09092100', 5, N'Dhania (Coriander Seeds) – Whole | WAH! Spices', N'Buy Dhania online from WAH! Spices. Whole Dhania (coriander seeds) with a citrusy, mildly sweet aroma.',
        298, N'12g', N'18g', N'55g', N'42g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 45, 50, N'WAH-DHANIA-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 85, 95, N'WAH-DHANIA-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 185, 210, N'WAH-DHANIA-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 339, 385, N'WAH-DHANIA-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Whole Dhania (coriander seeds) with a citrusy, mildly sweet aroma. Dry-roast and grind for the freshest coriander powder, or crush coarsely for kadai dishes.', Ingredients=N'100% Pure Dhania (Coriander Seeds)', Usages=N'["Freshly ground coriander powder", "Kadai masala", "Pickles", "Sambhar and rasam powder"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09092100', GstRate=5, SeoTitle=N'Dhania (Coriander Seeds) – Whole | WAH! Spices', SeoDescription=N'Buy Dhania online from WAH! Spices. Whole Dhania (coriander seeds) with a citrusy, mildly sweet aroma.', ImageUrl=N'/uploads/products/catalog/dhania.png', CaloriesPer100g=298, ProteinPer100g=N'12g', FatPer100g=N'18g', CarbsPer100g=N'55g', FiberPer100g=N'42g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dhania';

-- Dhaniya Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dhaniya-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dhaniya Powder', N'dhaniya-powder', N'Finely ground coriander from cleaned, sun-dried seeds, with a fresh citrusy aroma. The most-used base spice in Indian gravies, dals and sabzis.', N'100% Pure Dhaniya (Coriander) Powder', N'["Curries and gravies", "Dal and sabzi", "Marinades", "Chutneys and raita"]', N'ground-spices', NULL,
        55, 60, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dhaniya-powder.png', 0, 0, 1, 0, 0, N'09092200', 5, N'Dhaniya Powder (Coriander Powder) | WAH! Spices', N'Buy Dhaniya Powder online from WAH! Spices. Finely ground coriander from cleaned, sun-dried seeds, with a fresh citrusy aroma.',
        298, N'12g', N'18g', N'55g', N'42g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 55, 60, N'WAH-DHANIYA-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 99, 115, N'WAH-DHANIYA-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 225, 255, N'WAH-DHANIYA-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 419, 475, N'WAH-DHANIYA-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Finely ground coriander from cleaned, sun-dried seeds, with a fresh citrusy aroma. The most-used base spice in Indian gravies, dals and sabzis.', Ingredients=N'100% Pure Dhaniya (Coriander) Powder', Usages=N'["Curries and gravies", "Dal and sabzi", "Marinades", "Chutneys and raita"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09092200', GstRate=5, SeoTitle=N'Dhaniya Powder (Coriander Powder) | WAH! Spices', SeoDescription=N'Buy Dhaniya Powder online from WAH! Spices. Finely ground coriander from cleaned, sun-dried seeds, with a fresh citrusy aroma.', ImageUrl=N'/uploads/products/catalog/dhaniya-powder.png', CaloriesPer100g=298, ProteinPer100g=N'12g', FatPer100g=N'18g', CarbsPer100g=N'55g', FiberPer100g=N'42g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dhaniya-powder';

-- Dry Anardana
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-anardana')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Anardana', N'dry-anardana', N'Sun-dried wild pomegranate seeds (anardana) with a sweet-sour tang. Used whole or ground to add sourness to chole, chutneys and stuffed parathas.', N'100% Dried Pomegranate Seeds (Anardana)', N'["Pindi chole", "Anardana chutney", "Aloo paratha stuffing", "Chaat and digestive mixes"]', N'dry-dehydrated', NULL,
        79, 90, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-anardana.png', 0, 0, 1, 0, 0, N'08134090', 5, N'Dry Anardana (Pomegranate Seeds) | WAH! Spices', N'Buy Dry Anardana online from WAH! Spices. Sun-dried wild pomegranate seeds (anardana) with a sweet-sour tang.',
        320, N'5g', N'3g', N'72g', N'15g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 79, 90, N'WAH-DRY-ANARDANA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 149, 170, N'WAH-DRY-ANARDANA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 275, 315, N'WAH-DRY-ANARDANA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sun-dried wild pomegranate seeds (anardana) with a sweet-sour tang. Used whole or ground to add sourness to chole, chutneys and stuffed parathas.', Ingredients=N'100% Dried Pomegranate Seeds (Anardana)', Usages=N'["Pindi chole", "Anardana chutney", "Aloo paratha stuffing", "Chaat and digestive mixes"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'08134090', GstRate=5, SeoTitle=N'Dry Anardana (Pomegranate Seeds) | WAH! Spices', SeoDescription=N'Buy Dry Anardana online from WAH! Spices. Sun-dried wild pomegranate seeds (anardana) with a sweet-sour tang.', ImageUrl=N'/uploads/products/catalog/dry-anardana.png', CaloriesPer100g=320, ProteinPer100g=N'5g', FatPer100g=N'3g', CarbsPer100g=N'72g', FiberPer100g=N'15g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-anardana';

-- Dried Juniper Berries
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dried-juniper-berries')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dried Juniper Berries', N'dried-juniper-berries', N'Whole dried juniper berries (sukhe juniper berries) with a piney, resinous aroma and a gentle peppery-citrus bite. Lightly crush before use to release their oils in marinades, slow-cooked meats and pickles.', N'100% Pure Juniper Berries (Juniperus communis)', N'["Crushed into marinades for mutton and game", "Slow-cooked meat and vegetable stews", "Pickling and brining", "Homemade spice rubs", "Infused syrups and mocktails"]', N'whole-spices', NULL,
        99, 110, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dried-juniper-berries.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Dried Juniper Berries – Whole Spice | WAH! Spices', N'Buy whole dried juniper berries online from WAH! Spices. Piney, aromatic berries for marinades, meats and pickles.',
        300, N'4g', N'8g', N'60g', N'30g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 99, 110, N'WAH-DRIED-JUNIPER-BERRIES-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 219, 245, N'WAH-DRIED-JUNIPER-BERRIES-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 399, 445, N'WAH-DRIED-JUNIPER-BERRIES-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Whole dried juniper berries (sukhe juniper berries) with a piney, resinous aroma and a gentle peppery-citrus bite. Lightly crush before use to release their oils in marinades, slow-cooked meats and pickles.', Ingredients=N'100% Pure Juniper Berries (Juniperus communis)', Usages=N'["Crushed into marinades for mutton and game", "Slow-cooked meat and vegetable stews", "Pickling and brining", "Homemade spice rubs", "Infused syrups and mocktails"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Dried Juniper Berries – Whole Spice | WAH! Spices', SeoDescription=N'Buy whole dried juniper berries online from WAH! Spices. Piney, aromatic berries for marinades, meats and pickles.', ImageUrl=N'/uploads/products/catalog/dried-juniper-berries.png', CaloriesPer100g=300, ProteinPer100g=N'4g', FatPer100g=N'8g', CarbsPer100g=N'60g', FiberPer100g=N'30g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dried-juniper-berries';

-- Dry Curry Leaves
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-curry-leaves')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Curry Leaves', N'dry-curry-leaves', N'Carefully dried curry leaves that keep their distinctive aroma for months. Crumble into tadkas, chutney podis and South Indian dishes when fresh leaves are out of reach.', N'100% Dried Curry Leaves (Kadi Patta)', N'["Tadka for dal, sambhar and rasam", "Curry leaf podi / chutney powder", "Poha and upma", "Buttermilk tempering"]', N'dry-dehydrated', NULL,
        25, 29, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-curry-leaves.png', 0, 0, 1, 0, 0, N'12119099', 5, N'Dry Curry Leaves (Kadi Patta) | WAH! Spices', N'Buy Dry Curry Leaves online from WAH! Spices. Carefully dried curry leaves that keep their distinctive aroma for months.',
        300, N'12g', N'6g', N'55g', N'30g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 25, 29, N'WAH-DRY-CURRY-LEAVES-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 55, 60, N'WAH-DRY-CURRY-LEAVES-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 115, N'WAH-DRY-CURRY-LEAVES-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Carefully dried curry leaves that keep their distinctive aroma for months. Crumble into tadkas, chutney podis and South Indian dishes when fresh leaves are out of reach.', Ingredients=N'100% Dried Curry Leaves (Kadi Patta)', Usages=N'["Tadka for dal, sambhar and rasam", "Curry leaf podi / chutney powder", "Poha and upma", "Buttermilk tempering"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12119099', GstRate=5, SeoTitle=N'Dry Curry Leaves (Kadi Patta) | WAH! Spices', SeoDescription=N'Buy Dry Curry Leaves online from WAH! Spices. Carefully dried curry leaves that keep their distinctive aroma for months.', ImageUrl=N'/uploads/products/catalog/dry-curry-leaves.png', CaloriesPer100g=300, ProteinPer100g=N'12g', FatPer100g=N'6g', CarbsPer100g=N'55g', FiberPer100g=N'30g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-curry-leaves';

-- Dry Garlic
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-garlic')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Garlic', N'dry-garlic', N'Dehydrated garlic flakes with a mellow, savoury garlic flavour and no peeling required. Rehydrate in gravies or toast in oil for crunchy garlic.', N'100% Dehydrated Garlic Flakes', N'["Curries and dals", "Garlic rice and noodles", "Homemade garlic powder and seasonings", "Pickles and chutneys"]', N'dry-dehydrated', NULL,
        49, 55, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-garlic.png', 0, 0, 1, 0, 0, N'07129040', 5, N'Dry Garlic – Dehydrated Flakes | WAH! Spices', N'Buy Dry Garlic online from WAH! Spices. Dehydrated garlic flakes with a mellow, savoury garlic flavour and no peeling required.',
        331, N'17g', N'1g', N'73g', N'9g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-DRY-GARLIC-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-DRY-GARLIC-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-DRY-GARLIC-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-DRY-GARLIC-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Dehydrated garlic flakes with a mellow, savoury garlic flavour and no peeling required. Rehydrate in gravies or toast in oil for crunchy garlic.', Ingredients=N'100% Dehydrated Garlic Flakes', Usages=N'["Curries and dals", "Garlic rice and noodles", "Homemade garlic powder and seasonings", "Pickles and chutneys"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'07129040', GstRate=5, SeoTitle=N'Dry Garlic – Dehydrated Flakes | WAH! Spices', SeoDescription=N'Buy Dry Garlic online from WAH! Spices. Dehydrated garlic flakes with a mellow, savoury garlic flavour and no peeling required.', ImageUrl=N'/uploads/products/catalog/dry-garlic.png', CaloriesPer100g=331, ProteinPer100g=N'17g', FatPer100g=N'1g', CarbsPer100g=N'73g', FiberPer100g=N'9g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-garlic';

-- Dry Ginger
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-ginger')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Ginger', N'dry-ginger', N'Sonth (dried ginger root) with a warm, sharp heat that is gentler than fresh ginger. Grind for masalas, chai masala and traditional kadha.', N'100% Pure Dry Ginger (Sonth)', N'["Chai masala", "Kadha and herbal drinks", "Sonth chutney for chaat", "Laddoos and winter sweets"]', N'dry-dehydrated', NULL,
        55, 60, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-ginger.png', 0, 0, 1, 0, 0, N'09101110', 5, N'Dry Ginger (Sonth) – Whole | WAH! Spices', N'Buy Dry Ginger online from WAH! Spices. Sonth (dried ginger root) with a warm, sharp heat that is gentler than fresh ginger.',
        335, N'9g', N'4g', N'72g', N'14g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 55, 60, N'WAH-DRY-GINGER-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 99, 115, N'WAH-DRY-GINGER-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 185, 210, N'WAH-DRY-GINGER-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 409, 465, N'WAH-DRY-GINGER-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sonth (dried ginger root) with a warm, sharp heat that is gentler than fresh ginger. Grind for masalas, chai masala and traditional kadha.', Ingredients=N'100% Pure Dry Ginger (Sonth)', Usages=N'["Chai masala", "Kadha and herbal drinks", "Sonth chutney for chaat", "Laddoos and winter sweets"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09101110', GstRate=5, SeoTitle=N'Dry Ginger (Sonth) – Whole | WAH! Spices', SeoDescription=N'Buy Dry Ginger online from WAH! Spices. Sonth (dried ginger root) with a warm, sharp heat that is gentler than fresh ginger.', ImageUrl=N'/uploads/products/catalog/dry-ginger.png', CaloriesPer100g=335, ProteinPer100g=N'9g', FatPer100g=N'4g', CarbsPer100g=N'72g', FiberPer100g=N'14g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-ginger';

-- Dry Imli
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-imli')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Imli', N'dry-imli', N'Seedless dried tamarind pulp with a deep, fruity sourness. Soak and strain to make the tangy base for sambhar, rasam and sweet imli chutney.', N'100% Dried Tamarind (Imli)', N'["Sambhar and rasam", "Imli chutney for chaat", "Puliyogare / tamarind rice", "Gujarati dal and kadhi"]', N'dry-dehydrated', NULL,
        49, 55, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-imli.png', 0, 0, 1, 0, 0, N'08134010', 5, N'Dry Imli (Tamarind) | WAH! Spices', N'Buy Dry Imli online from WAH! Spices. Seedless dried tamarind pulp with a deep, fruity sourness.',
        239, N'3g', N'1g', N'63g', N'5g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 49, 55, N'WAH-DRY-IMLI-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 89, 100, N'WAH-DRY-IMLI-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 199, 225, N'WAH-DRY-IMLI-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 369, 420, N'WAH-DRY-IMLI-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Seedless dried tamarind pulp with a deep, fruity sourness. Soak and strain to make the tangy base for sambhar, rasam and sweet imli chutney.', Ingredients=N'100% Dried Tamarind (Imli)', Usages=N'["Sambhar and rasam", "Imli chutney for chaat", "Puliyogare / tamarind rice", "Gujarati dal and kadhi"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'08134010', GstRate=5, SeoTitle=N'Dry Imli (Tamarind) | WAH! Spices', SeoDescription=N'Buy Dry Imli online from WAH! Spices. Seedless dried tamarind pulp with a deep, fruity sourness.', ImageUrl=N'/uploads/products/catalog/dry-imli.png', CaloriesPer100g=239, ProteinPer100g=N'3g', FatPer100g=N'1g', CarbsPer100g=N'63g', FiberPer100g=N'5g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-imli';

-- Dry Kokum
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-kokum')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Kokum', N'dry-kokum', N'Sun-dried kokum rind (amsul) from the Konkan coast, with a gentle sourness and deep purple colour. A softer souring agent than tamarind for curries and coolers.', N'100% Sun-Dried Kokum Rind (Amsul)', N'["Konkani fish curry", "Sol kadhi", "Gujarati dal", "Kokum sherbet"]', N'dry-dehydrated', NULL,
        89, 100, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-kokum.png', 0, 0, 1, 0, 0, N'08134090', 5, N'Dry Kokum (Amsul) – Sun-Dried Rind | WAH! Spices', N'Buy Dry Kokum online from WAH! Spices. Sun-dried kokum rind (amsul) from the Konkan coast, with a gentle sourness and deep purple colour.',
        290, N'2g', N'2g', N'66g', N'15g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 89, 100, N'WAH-DRY-KOKUM-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-DRY-KOKUM-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-DRY-KOKUM-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sun-dried kokum rind (amsul) from the Konkan coast, with a gentle sourness and deep purple colour. A softer souring agent than tamarind for curries and coolers.', Ingredients=N'100% Sun-Dried Kokum Rind (Amsul)', Usages=N'["Konkani fish curry", "Sol kadhi", "Gujarati dal", "Kokum sherbet"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'08134090', GstRate=5, SeoTitle=N'Dry Kokum (Amsul) – Sun-Dried Rind | WAH! Spices', SeoDescription=N'Buy Dry Kokum online from WAH! Spices. Sun-dried kokum rind (amsul) from the Konkan coast, with a gentle sourness and deep purple colour.', ImageUrl=N'/uploads/products/catalog/dry-kokum.png', CaloriesPer100g=290, ProteinPer100g=N'2g', FatPer100g=N'2g', CarbsPer100g=N'66g', FiberPer100g=N'15g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-kokum';

-- Kokum
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kokum')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kokum', N'kokum', N'Kokum rind cured with salt in the traditional Konkan way, soft and intensely tangy. Add a couple of pieces to dals, curries and summer drinks.', N'Kokum Rind, Salt', N'["Gujarati and Maharashtrian dal", "Fish and prawn curries", "Kokum sharbat", "Sol kadhi"]', N'dry-dehydrated', NULL,
        79, 90, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/kokum.png', 0, 0, 1, 0, 0, N'08134090', 5, N'Kokum – Salted Kokum Rind | WAH! Spices', N'Buy Kokum online from WAH! Spices. Kokum rind cured with salt in the traditional Konkan way, soft and intensely tangy.',
        250, N'2g', N'2g', N'58g', N'13g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 79, 90, N'WAH-KOKUM-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-KOKUM-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-KOKUM-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Kokum rind cured with salt in the traditional Konkan way, soft and intensely tangy. Add a couple of pieces to dals, curries and summer drinks.', Ingredients=N'Kokum Rind, Salt', Usages=N'["Gujarati and Maharashtrian dal", "Fish and prawn curries", "Kokum sharbat", "Sol kadhi"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'08134090', GstRate=5, SeoTitle=N'Kokum – Salted Kokum Rind | WAH! Spices', SeoDescription=N'Buy Kokum online from WAH! Spices. Kokum rind cured with salt in the traditional Konkan way, soft and intensely tangy.', ImageUrl=N'/uploads/products/catalog/kokum.png', CaloriesPer100g=250, ProteinPer100g=N'2g', FatPer100g=N'2g', CarbsPer100g=N'58g', FiberPer100g=N'13g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kokum';

-- Dry Lemon
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-lemon')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Lemon', N'dry-lemon', N'Whole sun-dried lemons with a concentrated sour, slightly smoky citrus flavour. Pierce and simmer in stews and rice, or grind into a zesty seasoning.', N'100% Sun-Dried Lemon', N'["Middle-eastern style stews and soups", "Biryani and pulao", "Grind into lemon seasoning", "Infused teas"]', N'dry-dehydrated', NULL,
        69, 79, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-lemon.png', 0, 0, 1, 0, 0, N'08134090', 5, N'Dry Lemon – Sun-Dried Whole | WAH! Spices', N'Buy Dry Lemon online from WAH! Spices. Whole sun-dried lemons with a concentrated sour, slightly smoky citrus flavour.',
        290, N'7g', N'2g', N'60g', N'20g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 69, 79, N'WAH-DRY-LEMON-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 129, 145, N'WAH-DRY-LEMON-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 239, 270, N'WAH-DRY-LEMON-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Whole sun-dried lemons with a concentrated sour, slightly smoky citrus flavour. Pierce and simmer in stews and rice, or grind into a zesty seasoning.', Ingredients=N'100% Sun-Dried Lemon', Usages=N'["Middle-eastern style stews and soups", "Biryani and pulao", "Grind into lemon seasoning", "Infused teas"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'08134090', GstRate=5, SeoTitle=N'Dry Lemon – Sun-Dried Whole | WAH! Spices', SeoDescription=N'Buy Dry Lemon online from WAH! Spices. Whole sun-dried lemons with a concentrated sour, slightly smoky citrus flavour.', ImageUrl=N'/uploads/products/catalog/dry-lemon.png', CaloriesPer100g=290, ProteinPer100g=N'7g', FatPer100g=N'2g', CarbsPer100g=N'60g', FiberPer100g=N'20g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-lemon';

-- Dry Mango
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-mango')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Mango', N'dry-mango', N'Sun-dried raw mango slices (amchur pieces) with a bright, fruity sourness. Add whole to dals and pickles, or grind into fresh amchur powder.', N'100% Sun-Dried Raw Mango Slices', N'["Homemade amchur powder", "Dal and sambhar", "Pickles and chutneys", "Stuffed bhindi and karela"]', N'dry-dehydrated', NULL,
        79, 90, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-mango.png', 0, 0, 1, 0, 0, N'08134090', 5, N'Dry Mango (Amchur Slices) | WAH! Spices', N'Buy Dry Mango online from WAH! Spices. Sun-dried raw mango slices (amchur pieces) with a bright, fruity sourness.',
        330, N'3g', N'1g', N'80g', N'10g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 79, 90, N'WAH-DRY-MANGO-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-DRY-MANGO-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-DRY-MANGO-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sun-dried raw mango slices (amchur pieces) with a bright, fruity sourness. Add whole to dals and pickles, or grind into fresh amchur powder.', Ingredients=N'100% Sun-Dried Raw Mango Slices', Usages=N'["Homemade amchur powder", "Dal and sambhar", "Pickles and chutneys", "Stuffed bhindi and karela"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'08134090', GstRate=5, SeoTitle=N'Dry Mango (Amchur Slices) | WAH! Spices', SeoDescription=N'Buy Dry Mango online from WAH! Spices. Sun-dried raw mango slices (amchur pieces) with a bright, fruity sourness.', ImageUrl=N'/uploads/products/catalog/dry-mango.png', CaloriesPer100g=330, ProteinPer100g=N'3g', FatPer100g=N'1g', CarbsPer100g=N'80g', FiberPer100g=N'10g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-mango';

-- Dry Onion
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-onion')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Onion', N'dry-onion', N'Dehydrated onion flakes that deliver sweet, savoury onion flavour without chopping. Rehydrate in gravies or fry until golden for biryani birista.', N'100% Dehydrated Onion Flakes', N'["Quick curries and gravies", "Fried onion for biryani", "Soups and noodles", "Homemade seasonings"]', N'dry-dehydrated', NULL,
        39, 45, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-onion.png', 0, 0, 1, 0, 0, N'07122000', 5, N'Dry Onion – Dehydrated Flakes | WAH! Spices', N'Buy Dry Onion online from WAH! Spices. Dehydrated onion flakes that deliver sweet, savoury onion flavour without chopping.',
        341, N'10g', N'1g', N'79g', N'15g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 39, 45, N'WAH-DRY-ONION-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 69, 79, N'WAH-DRY-ONION-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 125, 140, N'WAH-DRY-ONION-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 285, 325, N'WAH-DRY-ONION-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Dehydrated onion flakes that deliver sweet, savoury onion flavour without chopping. Rehydrate in gravies or fry until golden for biryani birista.', Ingredients=N'100% Dehydrated Onion Flakes', Usages=N'["Quick curries and gravies", "Fried onion for biryani", "Soups and noodles", "Homemade seasonings"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'07122000', GstRate=5, SeoTitle=N'Dry Onion – Dehydrated Flakes | WAH! Spices', SeoDescription=N'Buy Dry Onion online from WAH! Spices. Dehydrated onion flakes that deliver sweet, savoury onion flavour without chopping.', ImageUrl=N'/uploads/products/catalog/dry-onion.png', CaloriesPer100g=341, ProteinPer100g=N'10g', FatPer100g=N'1g', CarbsPer100g=N'79g', FiberPer100g=N'15g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-onion';

-- Dry Rose
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'dry-rose')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Dry Rose', N'dry-rose', N'Fragrant dried rose petals with a delicate floral aroma and soft colour. A beautiful finish for biryanis, desserts, thandai and gulkand.', N'100% Dried Rose Petals', N'["Biryani and Mughlai dishes", "Kheer, phirni and desserts", "Thandai and rose drinks", "Gulkand and garnishing"]', N'dry-dehydrated', NULL,
        35, 40, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/dry-rose.png', 0, 0, 1, 0, 0, N'12119099', 5, N'Dry Rose Petals | WAH! Spices', N'Buy Dry Rose online from WAH! Spices. Fragrant dried rose petals with a delicate floral aroma and soft colour.',
        280, N'5g', N'2g', N'60g', N'25g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 35, 40, N'WAH-DRY-ROSE-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 79, 90, N'WAH-DRY-ROSE-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 149, 170, N'WAH-DRY-ROSE-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Fragrant dried rose petals with a delicate floral aroma and soft colour. A beautiful finish for biryanis, desserts, thandai and gulkand.', Ingredients=N'100% Dried Rose Petals', Usages=N'["Biryani and Mughlai dishes", "Kheer, phirni and desserts", "Thandai and rose drinks", "Gulkand and garnishing"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12119099', GstRate=5, SeoTitle=N'Dry Rose Petals | WAH! Spices', SeoDescription=N'Buy Dry Rose online from WAH! Spices. Fragrant dried rose petals with a delicate floral aroma and soft colour.', ImageUrl=N'/uploads/products/catalog/dry-rose.png', CaloriesPer100g=280, ProteinPer100g=N'5g', FatPer100g=N'2g', CarbsPer100g=N'60g', FiberPer100g=N'25g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'dry-rose';

-- Elaichi
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'elaichi')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Elaichi', N'elaichi', N'Plump, bold green cardamom pods with an intensely sweet, floral aroma. The queen of spices for chai, mithai, biryani and garam masala.', N'100% Pure Elaichi (Green Cardamom)', N'["Masala chai", "Kheer, halwa and mithai", "Biryani and pulao", "Garam masala"]', N'whole-spices', NULL,
        109, 125, N'20g', N'PouchPack',
        N'Premium', N'/uploads/products/catalog/elaichi.png', 0, 0, 1, 1, 0, N'09083120', 5, N'Elaichi (Green Cardamom) – Bold Pods | WAH! Spices', N'Buy Elaichi online from WAH! Spices. Plump, bold green cardamom pods with an intensely sweet, floral aroma.',
        311, N'11g', N'7g', N'68g', N'28g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 109, 125, N'WAH-ELAICHI-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 245, 280, N'WAH-ELAICHI-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 449, 510, N'WAH-ELAICHI-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Plump, bold green cardamom pods with an intensely sweet, floral aroma. The queen of spices for chai, mithai, biryani and garam masala.', Ingredients=N'100% Pure Elaichi (Green Cardamom)', Usages=N'["Masala chai", "Kheer, halwa and mithai", "Biryani and pulao", "Garam masala"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09083120', GstRate=5, SeoTitle=N'Elaichi (Green Cardamom) – Bold Pods | WAH! Spices', SeoDescription=N'Buy Elaichi online from WAH! Spices. Plump, bold green cardamom pods with an intensely sweet, floral aroma.', ImageUrl=N'/uploads/products/catalog/elaichi.png', CaloriesPer100g=311, ProteinPer100g=N'11g', FatPer100g=N'7g', CarbsPer100g=N'68g', FiberPer100g=N'28g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'elaichi';

-- Haldi Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'haldi-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Haldi Powder', N'haldi-powder', N'Bright golden turmeric powder ground from high-curcumin turmeric roots. Earthy, warm and vibrant — the everyday foundation of Indian cooking.', N'100% Pure Haldi (Turmeric) Powder', N'["Dal, sabzi and curries", "Marinades", "Haldi doodh (turmeric milk)", "Pickles and rice"]', N'ground-spices', NULL,
        59, 69, N'100g', N'PouchPack',
        N'Bestseller', N'/uploads/products/catalog/haldi-powder.png', 0, 0, 1, 1, 0, N'09103030', 5, N'Haldi Powder (Turmeric) – Pure | WAH! Spices', N'Buy Haldi Powder online from WAH! Spices. Bright golden turmeric powder ground from high-curcumin turmeric roots.',
        312, N'10g', N'3g', N'67g', N'23g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 59, 69, N'WAH-HALDI-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 109, 125, N'WAH-HALDI-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 245, 280, N'WAH-HALDI-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 449, 510, N'WAH-HALDI-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Bright golden turmeric powder ground from high-curcumin turmeric roots. Earthy, warm and vibrant — the everyday foundation of Indian cooking.', Ingredients=N'100% Pure Haldi (Turmeric) Powder', Usages=N'["Dal, sabzi and curries", "Marinades", "Haldi doodh (turmeric milk)", "Pickles and rice"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09103030', GstRate=5, SeoTitle=N'Haldi Powder (Turmeric) – Pure | WAH! Spices', SeoDescription=N'Buy Haldi Powder online from WAH! Spices. Bright golden turmeric powder ground from high-curcumin turmeric roots.', ImageUrl=N'/uploads/products/catalog/haldi-powder.png', CaloriesPer100g=312, ProteinPer100g=N'10g', FatPer100g=N'3g', CarbsPer100g=N'67g', FiberPer100g=N'23g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'haldi-powder';

-- Halim Beej
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'halim-beej')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Halim Beej', N'halim-beej', N'Halim Beej (garden cress seeds, also called aliv) are tiny reddish seeds with a peppery taste. Traditionally used in laddoos, kheer and drinks, especially during winter.', N'100% Pure Halim Beej (Garden Cress Seeds)', N'["Aliv laddoo", "Kheer and milk drinks", "Soaked in lemonade and sherbets", "Sprinkle on salads"]', N'whole-spices', NULL,
        45, 50, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/halim-beej.png', 0, 0, 1, 0, 0, N'12079990', 5, N'Halim Beej (Garden Cress Seeds) | WAH! Spices', N'Buy Halim Beej online from WAH! Spices. Halim Beej (garden cress seeds, also called aliv) are tiny reddish seeds with a peppery taste.',
        454, N'25g', N'24g', N'33g', N'8g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-HALIM-BEEJ-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-HALIM-BEEJ-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-HALIM-BEEJ-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-HALIM-BEEJ-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Halim Beej (garden cress seeds, also called aliv) are tiny reddish seeds with a peppery taste. Traditionally used in laddoos, kheer and drinks, especially during winter.', Ingredients=N'100% Pure Halim Beej (Garden Cress Seeds)', Usages=N'["Aliv laddoo", "Kheer and milk drinks", "Soaked in lemonade and sherbets", "Sprinkle on salads"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12079990', GstRate=5, SeoTitle=N'Halim Beej (Garden Cress Seeds) | WAH! Spices', SeoDescription=N'Buy Halim Beej online from WAH! Spices. Halim Beej (garden cress seeds, also called aliv) are tiny reddish seeds with a peppery taste.', ImageUrl=N'/uploads/products/catalog/halim-beej.png', CaloriesPer100g=454, ProteinPer100g=N'25g', FatPer100g=N'24g', CarbsPer100g=N'33g', FiberPer100g=N'8g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'halim-beej';

-- Jaiphal
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'jaiphal')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Jaiphal', N'jaiphal', N'Whole Jaiphal (nutmeg) with a warm, sweet and nutty aroma. Grate freshly into desserts, garam masala and creamy gravies — a pinch is all you need.', N'100% Pure Jaiphal (Whole Nutmeg)', N'["Freshly grated in kheer and sweets", "Garam masala and biryani", "Creamy white sauces", "Eggnog and spiced milk"]', N'whole-spices', NULL,
        59, 69, N'20g', N'PouchPack',
        N'Premium', N'/uploads/products/catalog/jaiphal.png', 0, 0, 1, 0, 0, N'09081100', 5, N'Jaiphal (Nutmeg) – Whole | WAH! Spices', N'Buy Jaiphal online from WAH! Spices. Whole Jaiphal (nutmeg) with a warm, sweet and nutty aroma.',
        525, N'6g', N'36g', N'49g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 59, 69, N'WAH-JAIPHAL-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 135, 155, N'WAH-JAIPHAL-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 249, 285, N'WAH-JAIPHAL-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 459, 520, N'WAH-JAIPHAL-200G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Whole Jaiphal (nutmeg) with a warm, sweet and nutty aroma. Grate freshly into desserts, garam masala and creamy gravies — a pinch is all you need.', Ingredients=N'100% Pure Jaiphal (Whole Nutmeg)', Usages=N'["Freshly grated in kheer and sweets", "Garam masala and biryani", "Creamy white sauces", "Eggnog and spiced milk"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09081100', GstRate=5, SeoTitle=N'Jaiphal (Nutmeg) – Whole | WAH! Spices', SeoDescription=N'Buy Jaiphal online from WAH! Spices. Whole Jaiphal (nutmeg) with a warm, sweet and nutty aroma.', ImageUrl=N'/uploads/products/catalog/jaiphal.png', CaloriesPer100g=525, ProteinPer100g=N'6g', FatPer100g=N'36g', CarbsPer100g=N'49g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'jaiphal';

-- Javitri
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'javitri')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Javitri', N'javitri', N'Javitri (mace) is the lacy, crimson-orange aril around nutmeg, with a refined, floral warmth. A luxurious note in biryanis, kormas and Mughlai sweets.', N'100% Pure Javitri (Mace)', N'["Biryani and korma", "Shahi gravies", "Garam masala", "Mithai and desserts"]', N'whole-spices', NULL,
        135, 155, N'20g', N'PouchPack',
        N'Premium', N'/uploads/products/catalog/javitri.png', 0, 0, 1, 0, 0, N'09082100', 5, N'Javitri (Mace) – Premium Whole | WAH! Spices', N'Buy Javitri online from WAH! Spices. Javitri (mace) is the lacy, crimson-orange aril around nutmeg, with a refined, floral warmth.',
        475, N'7g', N'32g', N'51g', N'20g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 135, 155, N'WAH-JAVITRI-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 299, 340, N'WAH-JAVITRI-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 549, 625, N'WAH-JAVITRI-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Javitri (mace) is the lacy, crimson-orange aril around nutmeg, with a refined, floral warmth. A luxurious note in biryanis, kormas and Mughlai sweets.', Ingredients=N'100% Pure Javitri (Mace)', Usages=N'["Biryani and korma", "Shahi gravies", "Garam masala", "Mithai and desserts"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09082100', GstRate=5, SeoTitle=N'Javitri (Mace) – Premium Whole | WAH! Spices', SeoDescription=N'Buy Javitri online from WAH! Spices. Javitri (mace) is the lacy, crimson-orange aril around nutmeg, with a refined, floral warmth.', ImageUrl=N'/uploads/products/catalog/javitri.png', CaloriesPer100g=475, ProteinPer100g=N'7g', FatPer100g=N'32g', CarbsPer100g=N'51g', FiberPer100g=N'20g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'javitri';

-- Jeera Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'jeera-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Jeera Powder', N'jeera-powder', N'Finely ground cumin with a warm, earthy, slightly nutty aroma. Stir into gravies or use roasted jeera powder for raita, chaas and chaat.', N'100% Pure Jeera (Cumin) Powder', N'["Curries and dals", "Raita and chaas", "Chaat and salads", "Marinades and kebabs"]', N'ground-spices', NULL,
        125, 140, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/jeera-powder.png', 0, 0, 1, 0, 0, N'09093200', 5, N'Jeera Powder (Cumin Powder) | WAH! Spices', N'Buy Jeera Powder online from WAH! Spices. Finely ground cumin with a warm, earthy, slightly nutty aroma.',
        375, N'18g', N'22g', N'44g', N'11g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 125, 140, N'WAH-JEERA-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 229, 260, N'WAH-JEERA-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 515, 585, N'WAH-JEERA-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 949, 1080, N'WAH-JEERA-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Finely ground cumin with a warm, earthy, slightly nutty aroma. Stir into gravies or use roasted jeera powder for raita, chaas and chaat.', Ingredients=N'100% Pure Jeera (Cumin) Powder', Usages=N'["Curries and dals", "Raita and chaas", "Chaat and salads", "Marinades and kebabs"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09093200', GstRate=5, SeoTitle=N'Jeera Powder (Cumin Powder) | WAH! Spices', SeoDescription=N'Buy Jeera Powder online from WAH! Spices. Finely ground cumin with a warm, earthy, slightly nutty aroma.', ImageUrl=N'/uploads/products/catalog/jeera-powder.png', CaloriesPer100g=375, ProteinPer100g=N'18g', FatPer100g=N'22g', CarbsPer100g=N'44g', FiberPer100g=N'11g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'jeera-powder';

-- Jeera
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'jeera')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Jeera', N'jeera', N'Bold, aromatic whole Jeera (cumin seeds), cleaned and sorted for even size. The sizzle of jeera in hot ghee is where countless Indian dishes begin.', N'100% Pure Jeera (Cumin Seeds)', N'["Jeera rice", "Dal and sabzi tadka", "Roasted jeera for raita and chaas", "Homemade masalas"]', N'whole-spices', NULL,
        109, 125, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/jeera.png', 0, 0, 1, 0, 0, N'09093129', 5, N'Jeera (Cumin Seeds) – Whole | WAH! Spices', N'Buy Jeera online from WAH! Spices. Bold, aromatic whole Jeera (cumin seeds), cleaned and sorted for even size.',
        375, N'18g', N'22g', N'44g', N'11g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 109, 125, N'WAH-JEERA-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 205, 235, N'WAH-JEERA-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 455, 515, N'WAH-JEERA-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 835, 950, N'WAH-JEERA-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Bold, aromatic whole Jeera (cumin seeds), cleaned and sorted for even size. The sizzle of jeera in hot ghee is where countless Indian dishes begin.', Ingredients=N'100% Pure Jeera (Cumin Seeds)', Usages=N'["Jeera rice", "Dal and sabzi tadka", "Roasted jeera for raita and chaas", "Homemade masalas"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09093129', GstRate=5, SeoTitle=N'Jeera (Cumin Seeds) – Whole | WAH! Spices', SeoDescription=N'Buy Jeera online from WAH! Spices. Bold, aromatic whole Jeera (cumin seeds), cleaned and sorted for even size.', ImageUrl=N'/uploads/products/catalog/jeera.png', CaloriesPer100g=375, ProteinPer100g=N'18g', FatPer100g=N'22g', CarbsPer100g=N'44g', FiberPer100g=N'11g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'jeera';

-- Kabab Chini
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kabab-chini')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kabab Chini', N'kabab-chini', N'Kabab Chini (cubeb pepper) are tailed peppercorns with a pungent, slightly bitter, allspice-like warmth. A traditional ingredient in Mughlai and Awadhi spice blends.', N'100% Pure Kabab Chini (Cubeb Pepper)', N'["Awadhi and Mughlai garam masala", "Kebab and galouti spice mix", "Biryani masala", "Meat curries"]', N'whole-spices', NULL,
        95, 109, N'20g', N'PouchPack',
        N'Premium', N'/uploads/products/catalog/kabab-chini.png', 0, 0, 1, 0, 0, N'09041190', 5, N'Kabab Chini (Cubeb Pepper) | WAH! Spices', N'Buy Kabab Chini online from WAH! Spices. Kabab Chini (cubeb pepper) are tailed peppercorns with a pungent, slightly bitter, allspice-like warmth.',
        300, N'11g', N'7g', N'60g', N'25g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 95, 109, N'WAH-KABAB-CHINI-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 215, 245, N'WAH-KABAB-CHINI-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 399, 455, N'WAH-KABAB-CHINI-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Kabab Chini (cubeb pepper) are tailed peppercorns with a pungent, slightly bitter, allspice-like warmth. A traditional ingredient in Mughlai and Awadhi spice blends.', Ingredients=N'100% Pure Kabab Chini (Cubeb Pepper)', Usages=N'["Awadhi and Mughlai garam masala", "Kebab and galouti spice mix", "Biryani masala", "Meat curries"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09041190', GstRate=5, SeoTitle=N'Kabab Chini (Cubeb Pepper) | WAH! Spices', SeoDescription=N'Buy Kabab Chini online from WAH! Spices. Kabab Chini (cubeb pepper) are tailed peppercorns with a pungent, slightly bitter, allspice-like warmth.', ImageUrl=N'/uploads/products/catalog/kabab-chini.png', CaloriesPer100g=300, ProteinPer100g=N'11g', FatPer100g=N'7g', CarbsPer100g=N'60g', FiberPer100g=N'25g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kabab-chini';

-- Kala Namak Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kala-namak-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kala Namak Powder', N'kala-namak-powder', N'Kala Namak (Indian black salt) with its distinctive tangy, sulphurous flavour. The essential ingredient behind chaat, raita, chaas and jaljeera.', N'100% Kala Namak (Black Salt) Powder', N'["Chaat and fruit salads", "Raita and chaas", "Jaljeera and shikanji", "Sprinkle on boiled eggs and corn"]', N'ground-spices', NULL,
        35, 40, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/kala-namak-powder.png', 0, 0, 1, 0, 0, N'25010090', 0, N'Kala Namak Powder (Black Salt) | WAH! Spices', N'Buy Kala Namak Powder online from WAH! Spices. Kala Namak (Indian black salt) with its distinctive tangy, sulphurous flavour.',
        0, N'0g', N'0g', N'0g', N'0g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 35, 40, N'WAH-KALA-NAMAK-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 65, 75, N'WAH-KALA-NAMAK-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 145, 165, N'WAH-KALA-NAMAK-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 265, 300, N'WAH-KALA-NAMAK-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Kala Namak (Indian black salt) with its distinctive tangy, sulphurous flavour. The essential ingredient behind chaat, raita, chaas and jaljeera.', Ingredients=N'100% Kala Namak (Black Salt) Powder', Usages=N'["Chaat and fruit salads", "Raita and chaas", "Jaljeera and shikanji", "Sprinkle on boiled eggs and corn"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'25010090', GstRate=0, SeoTitle=N'Kala Namak Powder (Black Salt) | WAH! Spices', SeoDescription=N'Buy Kala Namak Powder online from WAH! Spices. Kala Namak (Indian black salt) with its distinctive tangy, sulphurous flavour.', ImageUrl=N'/uploads/products/catalog/kala-namak-powder.png', CaloriesPer100g=0, ProteinPer100g=N'0g', FatPer100g=N'0g', CarbsPer100g=N'0g', FiberPer100g=N'0g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kala-namak-powder';

-- Kala Til
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kala-til')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kala Til', N'kala-til', N'Kala Til (black sesame seeds) are nutty and slightly more robust than white sesame. Traditionally used in winter sweets, laddoos and festive rituals.', N'100% Pure Kala Til (Black Sesame Seeds)', N'["Til laddoo and chikki", "Sprinkle on breads and salads", "Black sesame chutney", "Makar Sankranti sweets"]', N'whole-spices', NULL,
        69, 79, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/kala-til.png', 0, 0, 1, 0, 0, N'12074090', 5, N'Kala Til (Black Sesame Seeds) | WAH! Spices', N'Buy Kala Til online from WAH! Spices. Kala Til (black sesame seeds) are nutty and slightly more robust than white sesame.',
        573, N'18g', N'50g', N'24g', N'12g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 69, 79, N'WAH-KALA-TIL-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 125, 140, N'WAH-KALA-TIL-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 285, 325, N'WAH-KALA-TIL-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 525, 595, N'WAH-KALA-TIL-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Kala Til (black sesame seeds) are nutty and slightly more robust than white sesame. Traditionally used in winter sweets, laddoos and festive rituals.', Ingredients=N'100% Pure Kala Til (Black Sesame Seeds)', Usages=N'["Til laddoo and chikki", "Sprinkle on breads and salads", "Black sesame chutney", "Makar Sankranti sweets"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12074090', GstRate=5, SeoTitle=N'Kala Til (Black Sesame Seeds) | WAH! Spices', SeoDescription=N'Buy Kala Til online from WAH! Spices. Kala Til (black sesame seeds) are nutty and slightly more robust than white sesame.', ImageUrl=N'/uploads/products/catalog/kala-til.png', CaloriesPer100g=573, ProteinPer100g=N'18g', FatPer100g=N'50g', CarbsPer100g=N'24g', FiberPer100g=N'12g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kala-til';

-- Kalonji
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kalonji')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kalonji', N'kalonji', N'Kalonji (nigella seeds) are small black seeds with an oniony, slightly peppery flavour. Popular on naan, in pickles and in Bengali panch phoron.', N'100% Pure Kalonji (Nigella Seeds)', N'["Naan and kulcha topping", "Achaar and pickles", "Panch phoron tadka", "Aloo and pumpkin sabzi"]', N'whole-spices', NULL,
        49, 55, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/kalonji.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Kalonji (Nigella Seeds) | WAH! Spices', N'Buy Kalonji online from WAH! Spices. Kalonji (nigella seeds) are small black seeds with an oniony, slightly peppery flavour.',
        345, N'16g', N'22g', N'44g', N'11g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 49, 55, N'WAH-KALONJI-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 89, 100, N'WAH-KALONJI-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 165, 190, N'WAH-KALONJI-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 365, 415, N'WAH-KALONJI-500G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Kalonji (nigella seeds) are small black seeds with an oniony, slightly peppery flavour. Popular on naan, in pickles and in Bengali panch phoron.', Ingredients=N'100% Pure Kalonji (Nigella Seeds)', Usages=N'["Naan and kulcha topping", "Achaar and pickles", "Panch phoron tadka", "Aloo and pumpkin sabzi"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Kalonji (Nigella Seeds) | WAH! Spices', SeoDescription=N'Buy Kalonji online from WAH! Spices. Kalonji (nigella seeds) are small black seeds with an oniony, slightly peppery flavour.', ImageUrl=N'/uploads/products/catalog/kalonji.png', CaloriesPer100g=345, ProteinPer100g=N'16g', FatPer100g=N'22g', CarbsPer100g=N'44g', FiberPer100g=N'11g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kalonji';

-- Kashmiri Lal Mirch Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'kashmiri-lal-mirch-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Kashmiri Lal Mirch Powder', N'kashmiri-lal-mirch-powder', N'Kashmiri red chilli powder known for its brilliant red colour and mild heat. Gives curries and tandoori dishes a rich, appetising hue without excess spice.', N'100% Pure Kashmiri Red Chilli Powder', N'["Tandoori and tikka marinades", "Butter masala and makhani gravies", "Rogan josh", "Tadka for dal"]', N'ground-spices', NULL,
        109, 125, N'100g', N'PouchPack',
        N'Bestseller', N'/uploads/products/catalog/kashmiri-lal-mirch-powder.png', 0, 0, 1, 1, 0, N'09042211', 5, N'Kashmiri Lal Mirch Powder – Rich Colour | WAH! Spices', N'Buy Kashmiri Lal Mirch Powder online from WAH! Spices. Kashmiri red chilli powder known for its brilliant red colour and mild heat.',
        318, N'12g', N'17g', N'57g', N'27g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 109, 125, N'WAH-KASHMIRI-LAL-MIRCH-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 199, 225, N'WAH-KASHMIRI-LAL-MIRCH-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 449, 510, N'WAH-KASHMIRI-LAL-MIRCH-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 825, 940, N'WAH-KASHMIRI-LAL-MIRCH-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Kashmiri red chilli powder known for its brilliant red colour and mild heat. Gives curries and tandoori dishes a rich, appetising hue without excess spice.', Ingredients=N'100% Pure Kashmiri Red Chilli Powder', Usages=N'["Tandoori and tikka marinades", "Butter masala and makhani gravies", "Rogan josh", "Tadka for dal"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09042211', GstRate=5, SeoTitle=N'Kashmiri Lal Mirch Powder – Rich Colour | WAH! Spices', SeoDescription=N'Buy Kashmiri Lal Mirch Powder online from WAH! Spices. Kashmiri red chilli powder known for its brilliant red colour and mild heat.', ImageUrl=N'/uploads/products/catalog/kashmiri-lal-mirch-powder.png', CaloriesPer100g=318, ProteinPer100g=N'12g', FatPer100g=N'17g', CarbsPer100g=N'57g', FiberPer100g=N'27g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'kashmiri-lal-mirch-powder';

-- Khus Khus
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'khus-khus')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Khus Khus', N'khus-khus', N'Khus Khus (white poppy seeds) are tiny, creamy seeds with a mild, nutty taste. Ground into a paste, they thicken and enrich kormas and Bengali posto dishes.', N'100% Pure Khus Khus (White Poppy Seeds)', N'["Korma and shahi gravy paste", "Aloo posto", "Khus khus halwa and kheer", "Sprinkle on breads"]', N'whole-spices', NULL,
        85, 95, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/khus-khus.png', 0, 0, 1, 0, 0, N'12079100', 5, N'Khus Khus (Poppy Seeds) | WAH! Spices', N'Buy Khus Khus online from WAH! Spices. Khus Khus (white poppy seeds) are tiny, creamy seeds with a mild, nutty taste.',
        525, N'18g', N'42g', N'28g', N'20g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 85, 95, N'WAH-KHUS-KHUS-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 189, 215, N'WAH-KHUS-KHUS-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 349, 395, N'WAH-KHUS-KHUS-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Khus Khus (white poppy seeds) are tiny, creamy seeds with a mild, nutty taste. Ground into a paste, they thicken and enrich kormas and Bengali posto dishes.', Ingredients=N'100% Pure Khus Khus (White Poppy Seeds)', Usages=N'["Korma and shahi gravy paste", "Aloo posto", "Khus khus halwa and kheer", "Sprinkle on breads"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12079100', GstRate=5, SeoTitle=N'Khus Khus (Poppy Seeds) | WAH! Spices', SeoDescription=N'Buy Khus Khus online from WAH! Spices. Khus Khus (white poppy seeds) are tiny, creamy seeds with a mild, nutty taste.', ImageUrl=N'/uploads/products/catalog/khus-khus.png', CaloriesPer100g=525, ProteinPer100g=N'18g', FatPer100g=N'42g', CarbsPer100g=N'28g', FiberPer100g=N'20g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'khus-khus';

-- Lal Mirch Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'lal-mirch-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Lal Mirch Powder', N'lal-mirch-powder', N'Fiery red chilli powder ground from sun-dried red chillies for a clean, punchy heat. The everyday spice for curries, sabzis and chutneys.', N'100% Pure Lal Mirch (Red Chilli) Powder', N'["Curries and sabzis", "Pickles and chutneys", "Marinades", "Snacks and namkeen"]', N'ground-spices', NULL,
        69, 79, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/lal-mirch-powder.png', 0, 0, 1, 0, 0, N'09042211', 5, N'Lal Mirch Powder (Red Chilli) | WAH! Spices', N'Buy Lal Mirch Powder online from WAH! Spices. Fiery red chilli powder ground from sun-dried red chillies for a clean, punchy heat.',
        318, N'12g', N'17g', N'57g', N'27g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 69, 79, N'WAH-LAL-MIRCH-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 125, 140, N'WAH-LAL-MIRCH-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 285, 325, N'WAH-LAL-MIRCH-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 525, 595, N'WAH-LAL-MIRCH-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Fiery red chilli powder ground from sun-dried red chillies for a clean, punchy heat. The everyday spice for curries, sabzis and chutneys.', Ingredients=N'100% Pure Lal Mirch (Red Chilli) Powder', Usages=N'["Curries and sabzis", "Pickles and chutneys", "Marinades", "Snacks and namkeen"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09042211', GstRate=5, SeoTitle=N'Lal Mirch Powder (Red Chilli) | WAH! Spices', SeoDescription=N'Buy Lal Mirch Powder online from WAH! Spices. Fiery red chilli powder ground from sun-dried red chillies for a clean, punchy heat.', ImageUrl=N'/uploads/products/catalog/lal-mirch-powder.png', CaloriesPer100g=318, ProteinPer100g=N'12g', FatPer100g=N'17g', CarbsPer100g=N'57g', FiberPer100g=N'27g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'lal-mirch-powder';

-- Laung
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'laung')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Laung', N'laung', N'Hand-picked whole Laung (cloves) with intense, warm, slightly sweet pungency. Essential in garam masala, biryani and masala chai.', N'100% Pure Laung (Cloves)', N'["Biryani and pulao", "Garam masala", "Masala chai", "Pickles and meat curries"]', N'whole-spices', NULL,
        59, 69, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/laung.png', 0, 0, 1, 0, 0, N'09071000', 5, N'Laung (Cloves) – Whole | WAH! Spices', N'Buy Laung online from WAH! Spices. Hand-picked whole Laung (cloves) with intense, warm, slightly sweet pungency.',
        274, N'6g', N'13g', N'66g', N'34g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 59, 69, N'WAH-LAUNG-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 135, 155, N'WAH-LAUNG-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 249, 285, N'WAH-LAUNG-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 459, 520, N'WAH-LAUNG-200G', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Hand-picked whole Laung (cloves) with intense, warm, slightly sweet pungency. Essential in garam masala, biryani and masala chai.', Ingredients=N'100% Pure Laung (Cloves)', Usages=N'["Biryani and pulao", "Garam masala", "Masala chai", "Pickles and meat curries"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09071000', GstRate=5, SeoTitle=N'Laung (Cloves) – Whole | WAH! Spices', SeoDescription=N'Buy Laung online from WAH! Spices. Hand-picked whole Laung (cloves) with intense, warm, slightly sweet pungency.', ImageUrl=N'/uploads/products/catalog/laung.png', CaloriesPer100g=274, ProteinPer100g=N'6g', FatPer100g=N'13g', CarbsPer100g=N'66g', FiberPer100g=N'34g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'laung';

-- Methi
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'methi')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Methi', N'methi', N'Methi (fenugreek seeds) are golden, slightly bitter seeds that turn nutty and aromatic when tempered. Key to pickles, sambhar powder and Gujarati kadhi.', N'100% Pure Methi (Fenugreek Seeds)', N'["Achaar and pickles", "Sambhar and rasam powder", "Kadhi and dal tadka", "Sprouted methi sabzi"]', N'whole-spices', NULL,
        39, 45, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/methi.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Methi (Fenugreek Seeds) | WAH! Spices', N'Buy Methi online from WAH! Spices. Methi (fenugreek seeds) are golden, slightly bitter seeds that turn nutty and aromatic when tempered.',
        323, N'23g', N'6g', N'58g', N'25g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 39, 45, N'WAH-METHI-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 69, 79, N'WAH-METHI-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 159, 180, N'WAH-METHI-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 295, 335, N'WAH-METHI-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Methi (fenugreek seeds) are golden, slightly bitter seeds that turn nutty and aromatic when tempered. Key to pickles, sambhar powder and Gujarati kadhi.', Ingredients=N'100% Pure Methi (Fenugreek Seeds)', Usages=N'["Achaar and pickles", "Sambhar and rasam powder", "Kadhi and dal tadka", "Sprouted methi sabzi"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Methi (Fenugreek Seeds) | WAH! Spices', SeoDescription=N'Buy Methi online from WAH! Spices. Methi (fenugreek seeds) are golden, slightly bitter seeds that turn nutty and aromatic when tempered.', ImageUrl=N'/uploads/products/catalog/methi.png', CaloriesPer100g=323, ProteinPer100g=N'23g', FatPer100g=N'6g', CarbsPer100g=N'58g', FiberPer100g=N'25g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'methi';

-- Nagkesar
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'nagkesar')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Nagkesar', N'nagkesar', N'Nagkesar is the dried stamen of the Mesua ferrea flower, with a floral, slightly pepper-like aroma. Used in select regional spice blends and traditional preparations.', N'100% Pure Nagkesar (Mesua ferrea Stamens)', N'["Kashmiri and regional garam masala", "Traditional spice blends", "Biryani masala", "Ayurvedic-style preparations"]', N'whole-spices', NULL,
        85, 95, N'20g', N'PouchPack',
        N'Premium', N'/uploads/products/catalog/nagkesar.png', 0, 0, 1, 0, 0, N'12119099', 5, N'Nagkesar – Premium Whole Spice | WAH! Spices', N'Buy Nagkesar online from WAH! Spices. Nagkesar is the dried stamen of the Mesua ferrea flower, with a floral, slightly pepper-like aroma.',
        290, N'7g', N'6g', N'60g', N'25g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 85, 95, N'WAH-NAGKESAR-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 189, 215, N'WAH-NAGKESAR-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 349, 395, N'WAH-NAGKESAR-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Nagkesar is the dried stamen of the Mesua ferrea flower, with a floral, slightly pepper-like aroma. Used in select regional spice blends and traditional preparations.', Ingredients=N'100% Pure Nagkesar (Mesua ferrea Stamens)', Usages=N'["Kashmiri and regional garam masala", "Traditional spice blends", "Biryani masala", "Ayurvedic-style preparations"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12119099', GstRate=5, SeoTitle=N'Nagkesar – Premium Whole Spice | WAH! Spices', SeoDescription=N'Buy Nagkesar online from WAH! Spices. Nagkesar is the dried stamen of the Mesua ferrea flower, with a floral, slightly pepper-like aroma.', ImageUrl=N'/uploads/products/catalog/nagkesar.png', CaloriesPer100g=290, ProteinPer100g=N'7g', FatPer100g=N'6g', CarbsPer100g=N'60g', FiberPer100g=N'25g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'nagkesar';

-- Pimenta
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'pimenta')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Pimenta', N'pimenta', N'Pimenta (allspice berries) taste like a blend of clove, cinnamon and nutmeg in one dried berry. A versatile warm spice for rice, gravies, baking and pickling.', N'100% Pure Pimenta (Allspice Berries)', N'["Biryani and pulao", "Pickling spice", "Cakes and spiced bakes", "Meat marinades"]', N'whole-spices', NULL,
        75, 85, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/pimenta.png', 0, 0, 1, 0, 0, N'09042190', 5, N'Pimenta (Allspice Berries) | WAH! Spices', N'Buy Pimenta online from WAH! Spices. Pimenta (allspice berries) taste like a blend of clove, cinnamon and nutmeg in one dried berry.',
        263, N'6g', N'9g', N'72g', N'22g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 75, 85, N'WAH-PIMENTA-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 165, 190, N'WAH-PIMENTA-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 299, 340, N'WAH-PIMENTA-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Pimenta (allspice berries) taste like a blend of clove, cinnamon and nutmeg in one dried berry. A versatile warm spice for rice, gravies, baking and pickling.', Ingredients=N'100% Pure Pimenta (Allspice Berries)', Usages=N'["Biryani and pulao", "Pickling spice", "Cakes and spiced bakes", "Meat marinades"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09042190', GstRate=5, SeoTitle=N'Pimenta (Allspice Berries) | WAH! Spices', SeoDescription=N'Buy Pimenta online from WAH! Spices. Pimenta (allspice berries) taste like a blend of clove, cinnamon and nutmeg in one dried berry.', ImageUrl=N'/uploads/products/catalog/pimenta.png', CaloriesPer100g=263, ProteinPer100g=N'6g', FatPer100g=N'9g', CarbsPer100g=N'72g', FiberPer100g=N'22g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'pimenta';

-- Rai
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'rai')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Rai', N'rai', N'Rai (brown mustard seeds) that crackle and pop in hot oil, releasing a nutty, pungent flavour. The starting point of South Indian and Gujarati tadkas.', N'100% Pure Rai (Mustard Seeds)', N'["Tadka for dal, sambhar and poha", "Achaar and pickles", "Dhokla and khaman tempering", "Kasundi and mustard pastes"]', N'whole-spices', NULL,
        35, 40, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/rai.png', 0, 0, 1, 0, 0, N'12075090', 5, N'Rai (Mustard Seeds) | WAH! Spices', N'Buy Rai online from WAH! Spices. Rai (brown mustard seeds) that crackle and pop in hot oil, releasing a nutty, pungent flavour.',
        508, N'26g', N'36g', N'28g', N'12g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 35, 40, N'WAH-RAI-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 65, 75, N'WAH-RAI-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 145, 165, N'WAH-RAI-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 265, 300, N'WAH-RAI-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Rai (brown mustard seeds) that crackle and pop in hot oil, releasing a nutty, pungent flavour. The starting point of South Indian and Gujarati tadkas.', Ingredients=N'100% Pure Rai (Mustard Seeds)', Usages=N'["Tadka for dal, sambhar and poha", "Achaar and pickles", "Dhokla and khaman tempering", "Kasundi and mustard pastes"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12075090', GstRate=5, SeoTitle=N'Rai (Mustard Seeds) | WAH! Spices', SeoDescription=N'Buy Rai online from WAH! Spices. Rai (brown mustard seeds) that crackle and pop in hot oil, releasing a nutty, pungent flavour.', ImageUrl=N'/uploads/products/catalog/rai.png', CaloriesPer100g=508, ProteinPer100g=N'26g', FatPer100g=N'36g', CarbsPer100g=N'28g', FiberPer100g=N'12g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'rai';

-- Sabut Lal Mirch
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sabut-lal-mirch')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sabut Lal Mirch', N'sabut-lal-mirch', N'Sabut Lal Mirch are long, whole dried red chillies with a deep red colour and moderate heat. Grind at home for fresh chilli powder or use whole in masalas.', N'100% Pure Whole Red Chilli (Sabut Lal Mirch)', N'["Grind fresh chilli powder", "Homemade masala blends", "Chutneys and pastes", "Kashmiri-style gravies"]', N'whole-spices', NULL,
        79, 90, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/sabut-lal-mirch.png', 0, 0, 1, 0, 0, N'09042110', 5, N'Sabut Lal Mirch (Whole Red Chilli) | WAH! Spices', N'Buy Sabut Lal Mirch online from WAH! Spices. Sabut Lal Mirch are long, whole dried red chillies with a deep red colour and moderate heat.',
        318, N'12g', N'17g', N'57g', N'27g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 79, 90, N'WAH-SABUT-LAL-MIRCH-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-SABUT-LAL-MIRCH-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 325, 370, N'WAH-SABUT-LAL-MIRCH-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 599, 680, N'WAH-SABUT-LAL-MIRCH-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sabut Lal Mirch are long, whole dried red chillies with a deep red colour and moderate heat. Grind at home for fresh chilli powder or use whole in masalas.', Ingredients=N'100% Pure Whole Red Chilli (Sabut Lal Mirch)', Usages=N'["Grind fresh chilli powder", "Homemade masala blends", "Chutneys and pastes", "Kashmiri-style gravies"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09042110', GstRate=5, SeoTitle=N'Sabut Lal Mirch (Whole Red Chilli) | WAH! Spices', SeoDescription=N'Buy Sabut Lal Mirch online from WAH! Spices. Sabut Lal Mirch are long, whole dried red chillies with a deep red colour and moderate heat.', ImageUrl=N'/uploads/products/catalog/sabut-lal-mirch.png', CaloriesPer100g=318, ProteinPer100g=N'12g', FatPer100g=N'17g', CarbsPer100g=N'57g', FiberPer100g=N'27g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sabut-lal-mirch';

-- Safed Mirch
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'safed-mirch')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Safed Mirch', N'safed-mirch', N'Safed Mirch (white pepper) offers a clean, earthy heat without the dark specks of black pepper. Ideal for white gravies, soups and Mughlai dishes.', N'100% Pure Safed Mirch (White Pepper)', N'["Shahi and white gravies", "Soups and sauces", "Malai tikka marinade", "Mashed potatoes and Asian stir-fries"]', N'whole-spices', NULL,
        69, 79, N'20g', N'PouchPack',
        NULL, N'/uploads/products/catalog/safed-mirch.png', 0, 0, 1, 0, 0, N'09041150', 5, N'Safed Mirch (White Pepper) – Whole | WAH! Spices', N'Buy Safed Mirch online from WAH! Spices. Safed Mirch (white pepper) offers a clean, earthy heat without the dark specks of black pepper.',
        296, N'10g', N'2g', N'69g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 69, 79, N'WAH-SAFED-MIRCH-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'50g', 50, 149, 170, N'WAH-SAFED-MIRCH-50G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 279, 315, N'WAH-SAFED-MIRCH-100G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Safed Mirch (white pepper) offers a clean, earthy heat without the dark specks of black pepper. Ideal for white gravies, soups and Mughlai dishes.', Ingredients=N'100% Pure Safed Mirch (White Pepper)', Usages=N'["Shahi and white gravies", "Soups and sauces", "Malai tikka marinade", "Mashed potatoes and Asian stir-fries"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09041150', GstRate=5, SeoTitle=N'Safed Mirch (White Pepper) – Whole | WAH! Spices', SeoDescription=N'Buy Safed Mirch online from WAH! Spices. Safed Mirch (white pepper) offers a clean, earthy heat without the dark specks of black pepper.', ImageUrl=N'/uploads/products/catalog/safed-mirch.png', CaloriesPer100g=296, ProteinPer100g=N'10g', FatPer100g=N'2g', CarbsPer100g=N'69g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'safed-mirch';

-- Safed Til
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'safed-til')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Safed Til', N'safed-til', N'Safed Til (hulled white sesame seeds) with a sweet, nutty flavour that deepens on roasting. Perfect for chikkis, laddoos, chutneys and garnishing.', N'100% Pure Safed Til (White Sesame Seeds)', N'["Til chikki and laddoo", "Garnish on breads and stir-fries", "Til chutney", "Dhokla and handvo tempering"]', N'whole-spices', NULL,
        59, 69, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/safed-til.png', 0, 0, 1, 0, 0, N'12074090', 5, N'Safed Til (White Sesame Seeds) | WAH! Spices', N'Buy Safed Til online from WAH! Spices. Safed Til (hulled white sesame seeds) with a sweet, nutty flavour that deepens on roasting.',
        573, N'18g', N'50g', N'24g', N'12g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 59, 69, N'WAH-SAFED-TIL-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 109, 125, N'WAH-SAFED-TIL-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 245, 280, N'WAH-SAFED-TIL-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 449, 510, N'WAH-SAFED-TIL-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Safed Til (hulled white sesame seeds) with a sweet, nutty flavour that deepens on roasting. Perfect for chikkis, laddoos, chutneys and garnishing.', Ingredients=N'100% Pure Safed Til (White Sesame Seeds)', Usages=N'["Til chikki and laddoo", "Garnish on breads and stir-fries", "Til chutney", "Dhokla and handvo tempering"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'12074090', GstRate=5, SeoTitle=N'Safed Til (White Sesame Seeds) | WAH! Spices', SeoDescription=N'Buy Safed Til online from WAH! Spices. Safed Til (hulled white sesame seeds) with a sweet, nutty flavour that deepens on roasting.', ImageUrl=N'/uploads/products/catalog/safed-til.png', CaloriesPer100g=573, ProteinPer100g=N'18g', FatPer100g=N'50g', CarbsPer100g=N'24g', FiberPer100g=N'12g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'safed-til';

-- Saunf
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'saunf')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Saunf', N'saunf', N'Plump green Saunf (fennel seeds) with a sweet, cooling, anise-like flavour. Wonderful as an after-meal mukhwas and in pickles, gravies and chai.', N'100% Pure Saunf (Fennel Seeds)', N'["After-meal mukhwas", "Achaar and panch phoron", "Kashmiri gravies", "Saunf sharbat and chai"]', N'whole-spices', NULL,
        69, 79, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/saunf.png', 0, 0, 1, 0, 0, N'09096139', 5, N'Saunf (Fennel Seeds) | WAH! Spices', N'Buy Saunf online from WAH! Spices. Plump green Saunf (fennel seeds) with a sweet, cooling, anise-like flavour.',
        345, N'16g', N'15g', N'52g', N'40g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 69, 79, N'WAH-SAUNF-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 125, 140, N'WAH-SAUNF-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 285, 325, N'WAH-SAUNF-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 525, 595, N'WAH-SAUNF-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Plump green Saunf (fennel seeds) with a sweet, cooling, anise-like flavour. Wonderful as an after-meal mukhwas and in pickles, gravies and chai.', Ingredients=N'100% Pure Saunf (Fennel Seeds)', Usages=N'["After-meal mukhwas", "Achaar and panch phoron", "Kashmiri gravies", "Saunf sharbat and chai"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09096139', GstRate=5, SeoTitle=N'Saunf (Fennel Seeds) | WAH! Spices', SeoDescription=N'Buy Saunf online from WAH! Spices. Plump green Saunf (fennel seeds) with a sweet, cooling, anise-like flavour.', ImageUrl=N'/uploads/products/catalog/saunf.png', CaloriesPer100g=345, ProteinPer100g=N'16g', FatPer100g=N'15g', CarbsPer100g=N'52g', FiberPer100g=N'40g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'saunf';

-- Sendha Namak Powder
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sendha-namak-powder')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sendha Namak Powder', N'sendha-namak-powder', N'Sendha Namak (rock salt) powder with a mild, clean saltiness. A favourite for vrat and upvas cooking and everyday seasoning.', N'100% Sendha Namak (Rock Salt) Powder', N'["Vrat and upvas recipes", "Everyday seasoning", "Fruit chaat and salads", "Shikanji and lemonade"]', N'ground-spices', NULL,
        39, 45, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/sendha-namak-powder.png', 0, 0, 1, 0, 0, N'25010020', 0, N'Sendha Namak Powder (Rock Salt) | WAH! Spices', N'Buy Sendha Namak Powder online from WAH! Spices. Sendha Namak (rock salt) powder with a mild, clean saltiness.',
        0, N'0g', N'0g', N'0g', N'0g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 39, 45, N'WAH-SENDHA-NAMAK-POWDER-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 69, 79, N'WAH-SENDHA-NAMAK-POWDER-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 159, 180, N'WAH-SENDHA-NAMAK-POWDER-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 295, 335, N'WAH-SENDHA-NAMAK-POWDER-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sendha Namak (rock salt) powder with a mild, clean saltiness. A favourite for vrat and upvas cooking and everyday seasoning.', Ingredients=N'100% Sendha Namak (Rock Salt) Powder', Usages=N'["Vrat and upvas recipes", "Everyday seasoning", "Fruit chaat and salads", "Shikanji and lemonade"]', CategorySlug=N'ground-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'25010020', GstRate=0, SeoTitle=N'Sendha Namak Powder (Rock Salt) | WAH! Spices', SeoDescription=N'Buy Sendha Namak Powder online from WAH! Spices. Sendha Namak (rock salt) powder with a mild, clean saltiness.', ImageUrl=N'/uploads/products/catalog/sendha-namak-powder.png', CaloriesPer100g=0, ProteinPer100g=N'0g', FatPer100g=N'0g', CarbsPer100g=N'0g', FiberPer100g=N'0g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sendha-namak-powder';

-- Sowa Beej
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sowa-beej')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sowa Beej', N'sowa-beej', N'Sowa Beej (dill seeds) have a warm, slightly bitter, caraway-like flavour. Used in pickles, digestive mixes and traditional post-pregnancy recipes.', N'100% Pure Sowa Beej (Dill Seeds)', N'["Pickles and achaar", "Mukhwas and digestive mixes", "Dal and vegetable tadka", "Breads and crackers"]', N'whole-spices', NULL,
        45, 50, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/sowa-beej.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Sowa Beej (Dill Seeds) | WAH! Spices', N'Buy Sowa Beej online from WAH! Spices. Sowa Beej (dill seeds) have a warm, slightly bitter, caraway-like flavour.',
        305, N'16g', N'15g', N'55g', N'21g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-SOWA-BEEJ-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-SOWA-BEEJ-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-SOWA-BEEJ-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sowa Beej (dill seeds) have a warm, slightly bitter, caraway-like flavour. Used in pickles, digestive mixes and traditional post-pregnancy recipes.', Ingredients=N'100% Pure Sowa Beej (Dill Seeds)', Usages=N'["Pickles and achaar", "Mukhwas and digestive mixes", "Dal and vegetable tadka", "Breads and crackers"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Sowa Beej (Dill Seeds) | WAH! Spices', SeoDescription=N'Buy Sowa Beej online from WAH! Spices. Sowa Beej (dill seeds) have a warm, slightly bitter, caraway-like flavour.', ImageUrl=N'/uploads/products/catalog/sowa-beej.png', CaloriesPer100g=305, ProteinPer100g=N'16g', FatPer100g=N'15g', CarbsPer100g=N'55g', FiberPer100g=N'21g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sowa-beej';

-- Sukhi Lal Mirch
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sukhi-lal-mirch')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sukhi Lal Mirch', N'sukhi-lal-mirch', N'Sukhi Lal Mirch are hot, sun-dried red chillies that bring smoky heat to tadkas. Fry a couple in ghee to finish dals, sambhar and chutneys.', N'100% Pure Dried Red Chilli (Sukhi Lal Mirch)', N'["Dal and sambhar tadka", "Coconut and peanut chutneys", "Indo-Chinese stir-fries", "Pickles"]', N'whole-spices', NULL,
        69, 79, N'100g', N'PouchPack',
        NULL, N'/uploads/products/catalog/sukhi-lal-mirch.png', 0, 0, 1, 0, 0, N'09042110', 5, N'Sukhi Lal Mirch (Dried Red Chilli) | WAH! Spices', N'Buy Sukhi Lal Mirch online from WAH! Spices. Sukhi Lal Mirch are hot, sun-dried red chillies that bring smoky heat to tadkas.',
        318, N'12g', N'17g', N'57g', N'27g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'100g', 100, 69, 79, N'WAH-SUKHI-LAL-MIRCH-100G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 125, 140, N'WAH-SUKHI-LAL-MIRCH-200G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'500g', 500, 285, 325, N'WAH-SUKHI-LAL-MIRCH-500G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'1kg', 1000, 525, 595, N'WAH-SUKHI-LAL-MIRCH-1KG', 1, 0, 3, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sukhi Lal Mirch are hot, sun-dried red chillies that bring smoky heat to tadkas. Fry a couple in ghee to finish dals, sambhar and chutneys.', Ingredients=N'100% Pure Dried Red Chilli (Sukhi Lal Mirch)', Usages=N'["Dal and sambhar tadka", "Coconut and peanut chutneys", "Indo-Chinese stir-fries", "Pickles"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09042110', GstRate=5, SeoTitle=N'Sukhi Lal Mirch (Dried Red Chilli) | WAH! Spices', SeoDescription=N'Buy Sukhi Lal Mirch online from WAH! Spices. Sukhi Lal Mirch are hot, sun-dried red chillies that bring smoky heat to tadkas.', ImageUrl=N'/uploads/products/catalog/sukhi-lal-mirch.png', CaloriesPer100g=318, ProteinPer100g=N'12g', FatPer100g=N'17g', CarbsPer100g=N'57g', FiberPer100g=N'27g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sukhi-lal-mirch';

-- Sukhi Methi Patta
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'sukhi-methi-patta')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Sukhi Methi Patta', N'sukhi-methi-patta', N'Sukhi Methi Patta (kasuri methi) are dried fenugreek leaves with a beautiful, slightly bitter aroma. Crush between your palms to finish butter masala, dals and parathas.', N'100% Dried Fenugreek Leaves (Kasuri Methi)', N'["Butter masala and makhani gravies", "Methi parathas and theplas", "Dal makhani", "Naan and kulcha"]', N'dry-dehydrated', NULL,
        45, 50, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/sukhi-methi-patta.png', 0, 0, 1, 0, 0, N'07129090', 5, N'Sukhi Methi Patta (Kasuri Methi) | WAH! Spices', N'Buy Sukhi Methi Patta online from WAH! Spices. Sukhi Methi Patta (kasuri methi) are dried fenugreek leaves with a beautiful, slightly bitter aroma.',
        300, N'22g', N'5g', N'50g', N'25g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 45, 50, N'WAH-SUKHI-METHI-PATTA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 79, 90, N'WAH-SUKHI-METHI-PATTA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 145, 165, N'WAH-SUKHI-METHI-PATTA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Sukhi Methi Patta (kasuri methi) are dried fenugreek leaves with a beautiful, slightly bitter aroma. Crush between your palms to finish butter masala, dals and parathas.', Ingredients=N'100% Dried Fenugreek Leaves (Kasuri Methi)', Usages=N'["Butter masala and makhani gravies", "Methi parathas and theplas", "Dal makhani", "Naan and kulcha"]', CategorySlug=N'dry-dehydrated', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'07129090', GstRate=5, SeoTitle=N'Sukhi Methi Patta (Kasuri Methi) | WAH! Spices', SeoDescription=N'Buy Sukhi Methi Patta online from WAH! Spices. Sukhi Methi Patta (kasuri methi) are dried fenugreek leaves with a beautiful, slightly bitter aroma.', ImageUrl=N'/uploads/products/catalog/sukhi-methi-patta.png', CaloriesPer100g=300, ProteinPer100g=N'22g', FatPer100g=N'5g', CarbsPer100g=N'50g', FiberPer100g=N'25g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'sukhi-methi-patta';

-- Tej Patta
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'tej-patta')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Tej Patta', N'tej-patta', N'Tej Patta (Indian bay leaves) are fragrant leaves with a cinnamon-like aroma. Drop one or two into hot oil for biryanis, pulao, dals and gravies.', N'100% Pure Tej Patta (Indian Bay Leaves)', N'["Biryani and pulao", "Dal and rajma tadka", "Garam masala", "Kheer and sweet dishes"]', N'whole-spices', NULL,
        25, 29, N'50g', N'PouchPack',
        NULL, N'/uploads/products/catalog/tej-patta.png', 0, 0, 1, 0, 0, N'09109990', 5, N'Tej Patta (Indian Bay Leaves) | WAH! Spices', N'Buy Tej Patta online from WAH! Spices. Tej Patta (Indian bay leaves) are fragrant leaves with a cinnamon-like aroma.',
        313, N'8g', N'8g', N'75g', N'26g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'50g', 50, 25, 29, N'WAH-TEJ-PATTA-50G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'100g', 100, 49, 55, N'WAH-TEJ-PATTA-100G', 1, 0, 1, SYSUTCDATETIME(), 0, N'seed'),
        (@pid, N'200g', 200, 89, 100, N'WAH-TEJ-PATTA-200G', 1, 0, 2, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Tej Patta (Indian bay leaves) are fragrant leaves with a cinnamon-like aroma. Drop one or two into hot oil for biryanis, pulao, dals and gravies.', Ingredients=N'100% Pure Tej Patta (Indian Bay Leaves)', Usages=N'["Biryani and pulao", "Dal and rajma tadka", "Garam masala", "Kheer and sweet dishes"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09109990', GstRate=5, SeoTitle=N'Tej Patta (Indian Bay Leaves) | WAH! Spices', SeoDescription=N'Buy Tej Patta online from WAH! Spices. Tej Patta (Indian bay leaves) are fragrant leaves with a cinnamon-like aroma.', ImageUrl=N'/uploads/products/catalog/tej-patta.png', CaloriesPer100g=313, ProteinPer100g=N'8g', FatPer100g=N'8g', CarbsPer100g=N'75g', FiberPer100g=N'26g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'tej-patta';

-- Saffron
IF NOT EXISTS (SELECT 1 FROM Products WHERE Slug=N'saffron')
BEGIN
    INSERT INTO Products (Name, Slug, Description, Ingredients, Usages, CategorySlug, SubCategorySlug, Price, Mrp, Weight, PackType,
        Badge, ImageUrl, Rating, ReviewCount, InStock, Featured, IsDraft, HsnCode, GstRate, SeoTitle, SeoDescription,
        CaloriesPer100g, ProteinPer100g, FatPer100g, CarbsPer100g, FiberPer100g, CreatedAt, IsDeleted, CreatedBy)
    VALUES (N'Saffron', N'saffron', N'Hand-picked Kashmiri saffron (kesar) threads with deep crimson colour, honeyed aroma and rich flavour. A few strands steeped in warm milk transform biryanis, kheer and festive sweets.', N'100% Pure Kashmiri Saffron (Kesar) Threads', N'["Biryani and zafrani pulao", "Kheer, phirni and shrikhand", "Kesar milk and thandai", "Mithai and festive desserts"]', N'whole-spices', NULL,
        5999, 6999, N'20g', N'PouchPack',
        N'Premium', N'/uploads/products/catalog/saffron.png', 0, 0, 1, 1, 0, N'09102010', 5, N'Saffron (Kashmiri Kesar) – Premium Threads | WAH! Spices', N'Buy Saffron online from WAH! Spices. Hand-picked Kashmiri saffron (kesar) threads with deep crimson colour, honeyed aroma and rich flavour.',
        310, N'11g', N'6g', N'65g', N'4g', SYSUTCDATETIME(), 0, N'seed');
    SET @pid = SCOPE_IDENTITY();
    INSERT INTO ProductVariants (ProductId, Weight, WeightGrams, Price, Mrp, Sku, InStock, IsDefault, DisplayOrder, CreatedAt, IsDeleted, CreatedBy)
    VALUES
        (@pid, N'20g', 20, 5999, 6999, N'WAH-SAFFRON-20G', 1, 1, 0, SYSUTCDATETIME(), 0, N'seed');
END
ELSE
    UPDATE Products SET Description=N'Hand-picked Kashmiri saffron (kesar) threads with deep crimson colour, honeyed aroma and rich flavour. A few strands steeped in warm milk transform biryanis, kheer and festive sweets.', Ingredients=N'100% Pure Kashmiri Saffron (Kesar) Threads', Usages=N'["Biryani and zafrani pulao", "Kheer, phirni and shrikhand", "Kesar milk and thandai", "Mithai and festive desserts"]', CategorySlug=N'whole-spices', SubCategorySlug=NULL, PackType=N'PouchPack', HsnCode=N'09102010', GstRate=5, SeoTitle=N'Saffron (Kashmiri Kesar) – Premium Threads | WAH! Spices', SeoDescription=N'Buy Saffron online from WAH! Spices. Hand-picked Kashmiri saffron (kesar) threads with deep crimson colour, honeyed aroma and rich flavour.', ImageUrl=N'/uploads/products/catalog/saffron.png', CaloriesPer100g=310, ProteinPer100g=N'11g', FatPer100g=N'6g', CarbsPer100g=N'65g', FiberPer100g=N'4g', UpdatedAt=SYSUTCDATETIME(), UpdatedBy=N'seed' WHERE Slug=N'saffron';

COMMIT TRANSACTION;
SELECT (SELECT COUNT(*) FROM Categories WHERE IsDeleted=0) AS Categories, (SELECT COUNT(*) FROM Products WHERE IsDeleted=0) AS Products, (SELECT COUNT(*) FROM ProductVariants WHERE IsDeleted=0) AS Variants;
GO
