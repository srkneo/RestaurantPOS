using System;

namespace POSSystem.Models
{
    // One licence sold to one shop. It belongs to the platform owner, not to a restaurant, so it has no tenant.
    public class ShopLicence
    {
        public int Id { get; set; }
        // What the shop owner types in, like POS-7KQ2M-X4TNB-C9WRD-3HFZ6.
        public string Key { get; set; } = "";
        public string Customer { get; set; } = "";
        public string Plan { get; set; } = "trial"; // trial or yearly
        public DateTime PaidUntil { get; set; }
        public bool Revoked { get; set; }
        // The first PC that renews with this key keeps it. Another PC is refused until the owner resets this.
        public string? DeviceId { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public string? LastVersion { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
