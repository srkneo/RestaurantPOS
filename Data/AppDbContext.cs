using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public AppDbContext() : base()
        {
        }

        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }
        public DbSet<KitchenOrder> KitchenOrders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<KitchenOrder>().Property(o => o.Status).HasConversion<string>();

            // Configuring the one-to-many relationship between KitchenOrder and OrderItem
            modelBuilder.Entity<KitchenOrder>()
                .HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.KitchenOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(10, 2);

            base.OnModelCreating(modelBuilder);
        }
    }
}
