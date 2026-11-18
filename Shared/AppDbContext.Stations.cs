using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Kitchen stations (v1.8).
    public partial class AppDbContext
    {
        public DbSet<KitchenStation> KitchenStations { get; set; }

        private void ConfigureStations(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<KitchenStation>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.SortOrder });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<OrderItem>().Property(x => x.StationName).HasMaxLength(40);
        }
    }
}
