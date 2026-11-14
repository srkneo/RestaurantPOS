using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Bills (v1.6): money as numeric(12,2), enums as text, every bill tied to its restaurant, outlet and order.
    public partial class AppDbContext
    {
        public DbSet<Bill> Bills { get; set; }
        public DbSet<BillLine> BillLines { get; set; }
        public DbSet<BillCounter> BillCounters { get; set; }
        public DbSet<BillPayment> BillPayments { get; set; }

        private void ConfigureBilling(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Bill>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
                b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Outlet>().WithMany().HasForeignKey(x => x.OutletId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<KitchenOrder>().WithMany().HasForeignKey(x => x.KitchenOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.BillId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.BillId).OnDelete(DeleteBehavior.Restrict);

                b.Property(x => x.TaxMode).HasConversion<string>().HasMaxLength(20);
                b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                b.Property(x => x.Version).IsConcurrencyToken();
                foreach (var money in new[] { nameof(Bill.ItemsTotal), nameof(Bill.Discount), nameof(Bill.TaxableValue), nameof(Bill.Cgst),
                             nameof(Bill.Sgst), nameof(Bill.RoundOff), nameof(Bill.Total) })
                    b.Property(money).HasPrecision(12, 2);

                b.HasIndex(x => new { x.TenantId, x.OutletId, x.InvoiceNumber }).IsUnique();
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.BusinessDate });
                // At most one bill per order that is not void.
                b.HasIndex(x => new { x.TenantId, x.KitchenOrderId }).IsUnique().HasFilter("\"Status\" <> 'Void'");
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<BillLine>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.TaxRate).HasPrecision(5, 2);
                foreach (var money in new[] { nameof(BillLine.UnitPrice), nameof(BillLine.Amount), nameof(BillLine.Discount),
                             nameof(BillLine.TaxableValue), nameof(BillLine.Cgst), nameof(BillLine.Sgst) })
                    b.Property(money).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.BillId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<BillPayment>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.Method).HasConversion<string>().HasMaxLength(10);
                b.Property(x => x.Amount).HasPrecision(12, 2);
                b.Property(x => x.Tendered).HasPrecision(12, 2);
                b.Property(x => x.Change).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.BillId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<BillCounter>(b =>
            {
                b.ToTable("BillCounters");
                b.HasKey(x => new { x.TenantId, x.OutletId, x.FinancialYear });
            });
        }
    }
}
