using System;

namespace POSSystem.Models
{
    // The one licence this PC holds (Id is always 1). It belongs to the PC, not to a restaurant, so it has no tenant.
    public class LicenceRecord
    {
        public int Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string? LicenceKey { get; set; }
        public string? Payload { get; set; }
        public string? Signature { get; set; }
        // The latest time this PC has seen. A clock set back behind it locks the PC (see Licence.Evaluate).
        public DateTime LastSeenUtc { get; set; }
        public DateTime? LastRenewedUtc { get; set; }
        public string? LastRenewError { get; set; }
    }
}
