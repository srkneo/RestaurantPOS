using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Cash shifts and drawer movements (v1.6 day close).
    public partial class AppDbContext
    {
        public DbSet<CashShift> CashShifts { get; set; }
        public DbSet<CashMovement> CashMovements { get; set; }

        private void ConfigureShifts(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CashShift>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
                b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Outlet>().WithMany().HasForeignKey(x => x.OutletId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Movements).WithOne().HasForeignKey(m => m.CashShiftId).OnDelete(DeleteBehavior.Restrict);
                b.Property(x => x.Version).IsConcurrencyToken();
                foreach (var money in new[] { nameof(CashShift.OpeningCash), nameof(CashShift.CashSales), nameof(CashShift.ExpectedCash),
                             nameof(CashShift.CountedCash) })
                    b.Property(money).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.BusinessDate });
                // At most one open shift per outlet.
                b.HasIndex(x => new { x.TenantId, x.OutletId }).IsUnique().HasFilter("\"ClosedAt\" IS NULL");
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<CashMovement>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
                b.Property(x => x.Amount).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.CashShiftId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
