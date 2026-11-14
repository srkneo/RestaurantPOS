using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace POSSystem.Models
{
    public class AppUser : IdentityUser
    {
        public int TenantId { get; set; } = 1;
        public int OutletId { get; set; } = 1;

        [MaxLength(100)]
        public string? DisplayName { get; set; }

        // Inactive staff cannot sign in; existing sessions end at the next security check.
        public bool IsActive { get; set; } = true;

        // A fixed kitchen screen: stays signed in for 30 days, kitchen role only.
        public bool IsDevice { get; set; }
    }
}
