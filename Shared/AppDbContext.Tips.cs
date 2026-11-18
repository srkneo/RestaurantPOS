using Microsoft.EntityFrameworkCore;
using POSSystem.Billing;

namespace POSSystem.Data
{
    // Tips per waiter (v1.8), kept apart from bills.
    public partial class AppDbContext
    {
        public DbSet<Tip> Tips { get; set; }

        private void ConfigureTips(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Tip>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.Property(x => x.Amount).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.BusinessDate });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
