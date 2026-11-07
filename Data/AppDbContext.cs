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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=pos_system.db");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configuring the many-to-many relationship between KitchenOrder and MenuItem
            modelBuilder.Entity<KitchenOrder>()
                .HasMany(o => o.MenuItems)
                .WithMany()
                .UsingEntity(j => j.ToTable(name: "KitchenOrderItems"));

            base.OnModelCreating(modelBuilder);
        }
    }
}
