using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Sizes and extras of menu items (v1.8).
    public partial class AppDbContext
    {
        public DbSet<ItemVariant> ItemVariants { get; set; }
        public DbSet<ItemAddOn> ItemAddOns { get; set; }

        private void ConfigureMenuOptions(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ItemVariant>(b =>
            {
                b.Property(x => x.Price).HasPrecision(12, 2);
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.HasIndex(x => new { x.TenantId, x.MenuItemId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<ItemAddOn>(b =>
            {
                b.Property(x => x.Price).HasPrecision(12, 2);
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.HasIndex(x => new { x.TenantId, x.MenuItemId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<OrderItem>(b =>
            {
                b.Property(x => x.AddOnIds).HasMaxLength(200);
                b.Property(x => x.Options).HasMaxLength(500);
            });
        }
    }
}
