using WahSpices.Domain.Common;
using WahSpices.Domain.Enums;

namespace WahSpices.Domain.Entities;

public class Address : BaseEntity
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string? Landmark { get; set; }
    public AddressType Type { get; set; } = AddressType.Home;
    public bool IsDefault { get; set; } = false;

    public User User { get; set; } = null!;
}
