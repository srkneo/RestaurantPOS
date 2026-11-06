using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Models;

namespace RestaurantPOS.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }
        public DbSet<KitchenOrder> KitchenOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure relationships
            modelBuilder.Entity<KitchenOrder>()
                .HasOne(ko => ko.Table)
                .WithMany()
                .HasForeignKey(ko => ko.TableId);
        }
    }
}
