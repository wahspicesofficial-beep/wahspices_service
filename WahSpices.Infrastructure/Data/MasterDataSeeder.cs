using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Data;

/// <summary>
/// Seeds default master-data lists (the values the admin used to hard-code) the first time
/// a list type is missing. Never overwrites values an admin has added, renamed or hidden.
/// </summary>
public static class MasterDataSeeder
{
    public static readonly Dictionary<string, (string Value, string Label)[]> Defaults = new()
    {
        ["InventoryLocation"] = new[] { ("Warehouse A", "Warehouse A"), ("Warehouse B", "Warehouse B"), ("Cold Storage", "Cold Storage") },
        ["Unit"]              = new[] { ("pcs", "Pieces (pcs)"), ("kg", "Kilogram (kg)"), ("g", "Gram (g)"), ("box", "Box"), ("pkt", "Packet (pkt)"), ("ltr", "Litre (ltr)") },
        ["RecipeCategory"]    = new[] { ("Breakfast", "Breakfast"), ("Lunch", "Lunch"), ("Dinner", "Dinner"), ("Snacks", "Snacks"), ("Dessert", "Dessert"), ("Drinks", "Drinks"), ("Other", "Other") },
        ["RecipeDifficulty"]  = new[] { ("Easy", "Easy"), ("Medium", "Medium"), ("Hard", "Hard") },
        ["BlogCategory"]      = new[] { ("Recipe", "Recipe"), ("Health", "Health"), ("Knowledge", "Knowledge"), ("Other", "Other") },
        ["CustomerCategory"]  = new[] { ("Retail", "Retail"), ("Wholesale", "Wholesale"), ("Ecom", "E-commerce"), ("Distributor", "Distributor") },
        ["SalesClientType"]   = new[] { ("Retailer", "Retailer"), ("Wholesaler", "Wholesaler"), ("Distributor", "Distributor") },
        ["PaymentMode"]       = new[] { ("Cash", "Cash"), ("UPI", "UPI"), ("NEFT", "NEFT"), ("RTGS", "RTGS"), ("Cheque", "Cheque"), ("Card", "Card") },
        ["ExpensePaidBy"]     = new[] { ("PettyCash", "Petty Cash"), ("BankTransfer", "Bank Transfer"), ("UPI", "UPI") },
        ["StaffDocumentType"] = new[] { ("Aadhar", "Aadhaar Card"), ("Pan", "PAN Card"), ("OfferLetter", "Offer Letter"), ("ResumeCv", "Resume / CV"), ("BankPassbook", "Bank Passbook"), ("ProfilePhoto", "Profile Photo"), ("Other", "Other") },
        ["HolidayType"]       = new[] { ("National", "National"), ("Festival", "Festival"), ("Optional", "Optional") },
        ["GstSlab"]           = new[] { ("0", "0%"), ("5", "5%"), ("12", "12%"), ("18", "18%"), ("28", "28%") },
        ["GstSupplyType"]     = new[] { ("IntraState", "Intra-state (CGST + SGST)"), ("InterState", "Inter-state (IGST)"), ("Export", "Export") },
        ["BarcodeType"]       = new[] { ("Code128", "Code 128"), ("EAN13", "EAN-13"), ("QR", "QR Code") },
        ["InvoiceType"]       = new[] { ("B2C", "B2C"), ("B2B", "B2B") },
        ["ProductionShift"]   = new[] { ("Morning", "Morning"), ("Evening", "Evening"), ("Night", "Night") },
        ["ProductBadge"]      = new[] { ("Bestseller", "Bestseller"), ("Premium", "Premium"), ("New", "New"), ("Limited", "Limited") },
        ["CampaignType"]      = new[] { ("Offer", "Offer"), ("NewProduct", "New product launch"), ("Sale", "Sale"), ("Announcement", "Announcement") },
        ["PackSize"]          = new[] { ("20g", "20g · premium / expensive spices"), ("50g", "50g · small retail pack"), ("100g", "100g · regular retail pack"), ("200g", "200g · family pack"), ("500g", "500g · value pack"), ("1kg", "1kg · wholesale / bulk pack") },
    };

    // Lists whose values are referenced by code paths (keep them, allow relabel/hide only)
    private static readonly HashSet<string> SystemTypes = new() { "ExpensePaidBy", "StaffDocumentType", "GstSlab", "GstSupplyType", "BarcodeType", "InvoiceType" };

    public static void Seed(AppDbContext db, ILogger logger)
    {
        try
        {
            var existingTypes = db.MasterDataValues.IgnoreQueryFilters().Select(m => m.Type).Distinct().ToHashSet();
            var added = 0;
            foreach (var (type, values) in Defaults)
            {
                if (existingTypes.Contains(type)) continue;
                var i = 0;
                foreach (var (value, label) in values)
                {
                    db.MasterDataValues.Add(new MasterDataValue
                    {
                        Type = type, Value = value, Label = label, SortOrder = i++,
                        IsActive = true, IsSystem = SystemTypes.Contains(type), CreatedBy = "seed",
                    });
                    added++;
                }
            }
            if (added > 0)
            {
                db.SaveChanges();
                logger.LogInformation("Seeded {Count} master-data values", added);
            }
        }
        catch (Exception ex)
        {
            // Table may not exist yet if the migration hasn't been applied
            logger.LogWarning(ex, "Master data seeding skipped");
        }
    }
}
