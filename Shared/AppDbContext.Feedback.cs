using Microsoft.EntityFrameworkCore;
using POSSystem.Billing;

namespace POSSystem.Data
{
    // Guest feedback (v2.0): one rating per bill.
    public partial class AppDbContext
    {
        public DbSet<GuestFeedback> GuestFeedbacks { get; set; }

        private void ConfigureFeedback(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GuestFeedback>(b =>
            {
                b.Property(x => x.TenantId).HasDefaultValue(1);
                b.Property(x => x.OutletId).HasDefaultValue(1);
                b.HasIndex(x => new { x.TenantId, x.BillId }).IsUnique();
                b.HasIndex(x => new { x.TenantId, x.OutletId, x.Id });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }
    }
}
