using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Licences the platform owner sells (v1.7, cloud side).
    public partial class AppDbContext
    {
        public DbSet<ShopLicence> ShopLicences { get; set; }

        private void ConfigureShopLicences(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ShopLicence>(b =>
            {
                b.Property(x => x.Key).HasMaxLength(40);
                b.Property(x => x.Customer).HasMaxLength(200);
                b.Property(x => x.Plan).HasMaxLength(20);
                b.Property(x => x.DeviceId).HasMaxLength(64);
                b.Property(x => x.LastVersion).HasMaxLength(40);
                b.Property(x => x.UpdateMode).HasMaxLength(10);
                b.Property(x => x.PinnedVersion).HasMaxLength(20);
                b.Property(x => x.OwnerResetHash).HasMaxLength(64);
                b.HasIndex(x => x.Key).IsUnique();
            });
        }
    }
}
