using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Support details shown in the footer and on the About page (v1.7).
    public partial class AppDbContext
    {
        public DbSet<SupportSetting> SupportSettings { get; set; }

        private void ConfigureSupport(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SupportSetting>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedNever();
                b.Property(x => x.CompanyName).HasMaxLength(100);
                b.Property(x => x.ProductName).HasMaxLength(60);
                b.Property(x => x.Phone).HasMaxLength(20);
                b.Property(x => x.WhatsApp).HasMaxLength(20);
                b.Property(x => x.Email).HasMaxLength(200);
                b.Property(x => x.Hours).HasMaxLength(100);
                b.Property(x => x.Website).HasMaxLength(200);
            });
        }
    }
}
