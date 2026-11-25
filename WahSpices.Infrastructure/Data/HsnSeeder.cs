using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WahSpices.Domain.Entities;

namespace WahSpices.Infrastructure.Data;

/// <summary>
/// Starter HSN master for a spices &amp; masala business (CGST + SGST = IGST rate below), from the GST rate
/// schedules. Runs only while the HsnCodes table is completely empty — once anything exists (even deleted rows),
/// Admin → GST &amp; Tax → HSN Master owns the list and nothing is re-added. Products whose HSN starts with one of
/// these headings get its rate automatically (see Services.HsnRates).
/// </summary>
public static class HsnSeeder
{
    private static readonly (string Code, string Description, decimal Rate)[] Defaults =
    [
        ("0904", "Pepper of the genus Piper; dried or crushed or ground fruits of the genus Capsicum or Pimenta (black pepper, red chilli, chilli powder)", 5),
        ("0905", "Vanilla", 5),
        ("0906", "Cinnamon and cinnamon-tree flowers (dalchini)", 5),
        ("0907", "Cloves — whole fruit, cloves and stems (laung)", 5),
        ("0908", "Nutmeg, mace and cardamoms (jaiphal, javitri, elaichi)", 5),
        ("0909", "Seeds of anise, badian (star anise), fennel, coriander, cumin or caraway; juniper berries — other than of seed quality", 5),
        ("0910", "Ginger (other than fresh), saffron, turmeric (other than fresh), thyme, bay leaves, curry and other spices, including mixtures of spices (masalas)", 5),
        ("1207", "Other oil seeds — sesame, mustard, poppy, ajwain (ajams), niger, melon seeds, kokam etc. — other than of seed quality", 5),
        ("1211", "Plants and parts of plants (incl. seeds and fruits) used in pharmacy / perfumery, dried, whether or not cut, crushed or powdered (jadi-booti)", 5),
        ("1301", "Compounded asafoetida, commonly known as heeng", 5),
        ("2103", "Sauces and preparations therefor, mixed condiments and mixed seasonings; mustard flour and meal, prepared mustard; curry paste", 12),
        ("2106", "Food preparations not elsewhere specified or included", 18),
        ("2501", "Salt, including table salt, black salt and rock salt (sendha namak)", 0),
    ];

    public static void Seed(AppDbContext db, ILogger logger)
    {
        try
        {
            if (db.HsnCodes.IgnoreQueryFilters().Any()) return;   // the admin manages the list from here on
            foreach (var (code, description, rate) in Defaults)
                db.HsnCodes.Add(new HsnCode { Code = code, Description = description, GstRate = rate, IsActive = true, CreatedBy = "system" });
            db.SaveChanges();
            logger.LogInformation("HSN master seeded with {Count} spice & masala codes", Defaults.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HSN master seeding failed");
        }
    }
}
