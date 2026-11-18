using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    // QR table ordering (v1.8b): each table's secret QR token is unique.
    public partial class AppDbContext
    {
        private void ConfigureQr(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RestaurantTable>().Property(t => t.OrderToken).HasMaxLength(40);
            modelBuilder.Entity<RestaurantTable>().HasIndex(t => t.OrderToken).IsUnique();
        }
    }
}
