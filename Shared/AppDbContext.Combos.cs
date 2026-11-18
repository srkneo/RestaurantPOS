using Microsoft.EntityFrameworkCore;
using POSSystem.Menu;

namespace POSSystem.Data
{
    // Combos (v1.8): the items a combo menu item includes.
    public partial class AppDbContext
    {
        public DbSet<ComboPart> ComboParts { get; set; }

        private void ConfigureCombos(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ComboPart>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.HasIndex(x => new { x.TenantId, x.ComboItemId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<POSSystem.Models.OrderItem>().Property(x => x.Includes).HasMaxLength(300);
        }
    }
}
