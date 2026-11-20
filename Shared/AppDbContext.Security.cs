using Microsoft.EntityFrameworkCore;
using POSSystem.Logging;

namespace POSSystem.Data
{
    // OB8: security event log (platform-wide, read only by the platform owner).
    public partial class AppDbContext
    {
        public DbSet<SecurityEvent> SecurityEvents { get; set; }

        private void ConfigureSecurityEvents(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SecurityEvent>(b =>
            {
                b.Property(x => x.Type).HasMaxLength(40);
                b.Property(x => x.Method).HasMaxLength(10);
                b.Property(x => x.Path).HasMaxLength(200);
                b.Property(x => x.UserId).HasMaxLength(64);
                b.Property(x => x.Ip).HasMaxLength(45);
                b.Property(x => x.RequestId).HasMaxLength(64);
                b.Property(x => x.TargetId).HasMaxLength(64);
                b.HasIndex(x => x.AtUtc);
                b.HasIndex(x => new { x.UserId, x.AtUtc });
                b.HasIndex(x => new { x.TenantId, x.AtUtc });
            });
        }
    }
}
