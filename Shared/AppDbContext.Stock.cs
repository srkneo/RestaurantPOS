using Microsoft.EntityFrameworkCore;
using POSSystem.Inventory;

namespace POSSystem.Data
{
    // Stock / inventory (v2.1).
    public partial class AppDbContext
    {
        public DbSet<StockItem> StockItems { get; set; }
        public DbSet<StockMove> StockMoves { get; set; }

        private void ConfigureStock(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StockItem>(b =>
            {
                b.Property(x => x.Quantity).HasPrecision(12, 3);
                b.Property(x => x.LowLevel).HasPrecision(12, 3);
                b.Property(x => x.CostPerUnit).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.Name });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<StockMove>(b =>
            {
                b.Property(x => x.Change).HasPrecision(12, 3);
                b.Property(x => x.QuantityAfter).HasPrecision(12, 3);
                b.Property(x => x.Cost).HasPrecision(12, 2);
                b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
                b.HasOne<StockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.StockItemId, x.At });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
