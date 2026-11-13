using Microsoft.AspNetCore.Identity;

namespace POSSystem.Models
{
    public class AppUser : IdentityUser
    {
        public int TenantId { get; set; } = 1;
        public int OutletId { get; set; } = 1;
    }
}
