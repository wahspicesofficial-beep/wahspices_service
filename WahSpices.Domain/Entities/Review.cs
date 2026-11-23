using WahSpices.Domain.Common;

namespace WahSpices.Domain.Entities;

public class Review : BaseEntity
{
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Comment { get; set; }
    public bool IsVerifiedPurchase { get; set; } = false;
    public int HelpfulCount { get; set; } = 0;
    public bool IsApproved { get; set; } = false;         // only approved reviews are public / count in ratings
    public string Status { get; set; } = "Pending";      // Pending | Approved | Rejected
    public string? AdminReply { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public string? ModeratedBy { get; set; }
    public bool IsFeatured { get; set; } = false;        // shown in "What Our Customers Say" on the home page

    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
