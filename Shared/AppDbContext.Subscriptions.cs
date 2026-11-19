using Microsoft.EntityFrameworkCore;
using POSSystem.Platform;

namespace POSSystem.Data
{
    // Payments restaurants made for their cloud subscription (recorded by the platform owner).
    public partial class AppDbContext
    {
        public DbSet<TenantPayment> TenantPayments { get; set; }
    }
}
