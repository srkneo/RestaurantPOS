using Microsoft.EntityFrameworkCore;
using POSSystem.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            modelBuilder.Entity<KitchenOrder>().Property(o => o.ClientOrderId).HasDefaultValueSql("gen_random_uuid()");
            modelBuilder.Entity<KitchenOrder>().HasIndex(o => new { o.TenantId, o.ClientOrderId }).IsUnique();

            foreach (var type in new[] { typeof(KitchenOrder), typeof(MenuItem), typeof(RestaurantTable) })
            {
                modelBuilder.Entity(type).Property(nameof(TenantEntity.TenantId)).HasDefaultValue(1);
                modelBuilder.Entity(type).Property(nameof(TenantEntity.OutletId)).HasDefaultValue(1);
                modelBuilder.Entity(type).Property(nameof(TenantEntity.CreatedAt)).HasDefaultValueSql("now()");
            }

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

        public override int SaveChanges()
        {
            SetUpdatedAt();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SetUpdatedAt();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void SetUpdatedAt()
        {
            foreach (var entry in ChangeTracker.Entries<TenantEntity>().Where(e => e.State == EntityState.Modified))
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }
}
