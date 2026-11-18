using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // The shop PC licence (v1.7).
    public partial class AppDbContext
    {
        public DbSet<LicenceRecord> LicenceRecords { get; set; }

        private void ConfigureLicence(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LicenceRecord>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedNever();
                b.Property(x => x.DeviceId).HasMaxLength(64);
                b.Property(x => x.LicenceKey).HasMaxLength(100);
                b.Property(x => x.LastRenewError).HasMaxLength(300);
                b.Property(x => x.OwnerResetHash).HasMaxLength(64);
                b.Property(x => x.OwnerResetUsedHash).HasMaxLength(64);
            });
        }
    }
}
