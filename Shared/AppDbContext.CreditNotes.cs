using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Credit notes (v1.6 F7).
    public partial class AppDbContext
    {
        public DbSet<CreditNote> CreditNotes { get; set; }
        public DbSet<CreditNoteCounter> CreditNoteCounters { get; set; }

        private void ConfigureCreditNotes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CreditNote>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
                b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Outlet>().WithMany().HasForeignKey(x => x.OutletId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
                b.Property(x => x.RefundMethod).HasConversion<string>().HasMaxLength(10);
                foreach (var money in new[] { nameof(CreditNote.TaxableValue), nameof(CreditNote.Cgst), nameof(CreditNote.Sgst),
                             nameof(CreditNote.RoundOff), nameof(CreditNote.Total), nameof(CreditNote.RefundAmount) })
                    b.Property(money).HasPrecision(12, 2);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.Number }).IsUnique();
                b.HasIndex(x => new { x.TenantId, x.BillId }).IsUnique();
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.BusinessDate });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });

            modelBuilder.Entity<CreditNoteCounter>(b =>
            {
                b.ToTable("CreditNoteCounters");
                b.HasKey(x => new { x.TenantId, x.OutletId, x.FinancialYear });
            });
        }
    }
}
