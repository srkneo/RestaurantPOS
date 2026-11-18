using Microsoft.EntityFrameworkCore;
using POSSystem.Billing;

namespace POSSystem.Data
{
    // Expenses (v1.9).
    public partial class AppDbContext
    {
        public DbSet<Expense> Expenses { get; set; }

        private void ConfigureExpenses(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Expense>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.Property(x => x.Amount).HasPrecision(12, 2);
                b.Property(x => x.PaidFrom).HasConversion<string>().HasMaxLength(10);
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.BusinessDate });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
