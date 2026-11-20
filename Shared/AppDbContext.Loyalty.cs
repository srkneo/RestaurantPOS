using Microsoft.EntityFrameworkCore;
using POSSystem.Loyalty;

namespace POSSystem.Data
{
    // Loyalty guests and their points (v2.2).
    public partial class AppDbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<LoyaltyMove> LoyaltyMoves { get; set; }

        private void ConfigureLoyalty(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(b =>
            {
                b.Property(x => x.Spent).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.Phone }).IsUnique();
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<LoyaltyMove>(b =>
            {
                b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
                b.HasIndex(x => new { x.TenantId, x.CustomerId, x.At });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
