using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Entities;
using WahSpices.Infrastructure.Data;
using WahSpices.Infrastructure.Email;
using WahSpices.Infrastructure.Messaging;

namespace WahSpices.API.Services.Marketing;

/// <summary>Turns a campaign into the actual email / WhatsApp payload (also used for the admin preview and test sends).</summary>
public sealed class CampaignContent
{
    private readonly AppDbContext _db;
    private readonly IEmailBrandProvider _brand;
    private readonly CampaignTypeLabels _labels;
    private readonly UnsubscribeTokens _tokens;
    private readonly IMemoryCache _cache;

    public CampaignContent(AppDbContext db, IEmailBrandProvider brand, CampaignTypeLabels labels, UnsubscribeTokens tokens, IMemoryCache cache)
    {
        _db = db; _brand = brand; _labels = labels; _tokens = tokens; _cache = cache;
    }

    public async Task<IReadOnlyList<CampaignEmailProduct>> ProductsAsync(string? productIds, CancellationToken ct = default)
    {
        var ids = MarketingRules.ParseIds(productIds).Take(MarketingRules.MaxProducts).ToList();
        if (ids.Count == 0) return Array.Empty<CampaignEmailProduct>();
        var key = "campaign-products:" + string.Join(',', ids);
        if (_cache.TryGetValue(key, out IReadOnlyList<CampaignEmailProduct>? hit) && hit != null) return hit;
        var rows = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && !p.IsDraft)
            .Select(p => new { p.Id, p.Name, p.Weight, p.Price, p.Mrp, p.ImageUrl, p.Slug }).ToListAsync(ct);
        IReadOnlyList<CampaignEmailProduct> list = ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).Where(r => r != null)
            .Select(r => new CampaignEmailProduct(r!.Name, r.Weight, r.Price, r.Mrp, r.ImageUrl, r.Slug)).ToList();
        _cache.Set(key, list, TimeSpan.FromMinutes(2));
        return list;
    }

    public async Task<CampaignEmailModel> ModelAsync(MarketingCampaign c, string recipientName, string? unsubscribeUrl, CancellationToken ct = default) => new()
    {
        TypeValue = c.Type,
        TypeLabel = await _labels.LabelAsync(c.Type),
        Title = c.Title,
        Body = c.Body,
        ImagePath = c.ImageUrl,
        ButtonLabel = c.ButtonLabel,
        ButtonUrl = c.ButtonUrl,
        CouponCode = c.CouponCode,
        Products = await ProductsAsync(c.ProductIds, ct),
        RecipientName = recipientName,
        UnsubscribeUrl = unsubscribeUrl,
    };

    public async Task<RenderedEmail> PreviewAsync(MarketingCampaign c, string recipientName = "Priya Sharma", CancellationToken ct = default)
    {
        var b = await _brand.GetAsync(ct);
        return CampaignEmails.Render(b, await ModelAsync(c, recipientName, null, ct));
    }

    /// <summary>The real email for one customer: signed unsubscribe link in the footer + RFC 8058 one-click headers.</summary>
    public async Task<EmailMessage> BuildEmailAsync(MarketingCampaign c, int userId, string toEmail, string toName, CancellationToken ct = default)
    {
        var b = await _brand.GetAsync(ct);
        var token = Uri.EscapeDataString(_tokens.Create(userId));
        var page = b.Link("/unsubscribe?token=" + token);
        var oneClick = $"{b.AssetBaseUrl}/api/marketing/unsubscribe?token={token}";
        var r = CampaignEmails.Render(b, await ModelAsync(c, toName, page, ct));
        return new EmailMessage(toEmail, toName, r.Subject, r.Html, r.Text, "campaign")
        {
            Headers = new Dictionary<string, string>
            {
                ["List-Unsubscribe"] = $"<{oneClick}>",
                ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                ["X-Campaign-Id"] = $"wah-{c.Id}",
            },
        };
    }

    /// <summary>Admin "send test" — same content, "[TEST]" subject, no unsubscribe token (not a customer).</summary>
    public async Task<EmailMessage> BuildTestEmailAsync(MarketingCampaign c, string toEmail, CancellationToken ct = default)
    {
        var r = await PreviewAsync(c, "", ct);
        return new EmailMessage(toEmail, "", "[TEST] " + r.Subject, r.Html, r.Text, "campaign-test");
    }

    public async Task<WhatsAppTemplateData> BuildWhatsAppAsync(MarketingCampaign c, string name, CancellationToken ct = default)
    {
        var b = await _brand.GetAsync(ct);
        var link = string.IsNullOrWhiteSpace(c.ButtonUrl) ? b.Link("/products") : CampaignEmails.ResolveLink(b, c.ButtonUrl);
        return new WhatsAppTemplateData(name, c.Title, c.Body, link, c.CouponCode, b.Asset(c.ImageUrl));
    }
}
