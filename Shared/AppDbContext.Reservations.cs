using Microsoft.EntityFrameworkCore;
using POSSystem.Reservations;

namespace POSSystem.Data
{
    // Table reservations (v2.1).
    public partial class AppDbContext
    {
        public DbSet<Reservation> Reservations { get; set; }

        private void ConfigureReservations(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reservation>(b =>
            {
                b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.Date });
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.Status, x.AtUtc });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
