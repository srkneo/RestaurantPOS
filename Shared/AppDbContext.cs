using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using POSSystem.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace POSSystem.Data
{
    public partial class AppDbContext : IdentityDbContext<AppUser>
    {
        // Set per request from the signed-in user's claims (see AuthSetup.UsePosAuth).
        public int CurrentTenantId { get; set; } = 1;
        public int CurrentOutletId { get; set; } = 1;
        public string? CurrentUserId { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public AppDbContext() : base()
        {
        }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Outlet> Outlets { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<MenuCategory> MenuCategories { get; set; }
        public DbSet<OrderCounter> OrderCounters { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }
        public DbSet<KitchenOrder> KitchenOrders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var seededAt = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

            // Existing data becomes restaurant 1, outlet 1. New ids start at 2.
            modelBuilder.Entity<Tenant>(b =>
            {
                b.Property(t => t.Id).HasIdentityOptions(startValue: 2);
                b.Property(t => t.Name).HasMaxLength(200);
                b.HasData(new Tenant { Id = 1, Name = "My Restaurant", IsActive = true, CreatedAt = seededAt });
            });

            modelBuilder.Entity<Outlet>(b =>
            {
                b.Property(o => o.Id).HasIdentityOptions(startValue: 2);
                b.Property(o => o.Name).HasMaxLength(200);
                b.Property(o => o.TimeZone).HasMaxLength(64);
                b.Property(o => o.Settings).HasColumnType("jsonb");
                b.HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
                b.HasData(new Outlet
                {
                    Id = 1,
                    TenantId = 1,
                    Name = "Main",
                    TimeZone = BusinessClock.DefaultTimeZone,
                    BusinessDayStartHour = BusinessClock.DefaultDayStartHour,
                    Settings = "{}",
                    CreatedAt = seededAt
                });
            });

            modelBuilder.Entity<KitchenOrder>().Property(o => o.ClientOrderId).HasDefaultValueSql("gen_random_uuid()");
            modelBuilder.Entity<KitchenOrder>().HasIndex(o => new { o.TenantId, o.ClientOrderId }).IsUnique();
            modelBuilder.Entity<KitchenOrder>().HasIndex(o => new { o.TenantId, o.OutletId, o.Status });
            modelBuilder.Entity<KitchenOrder>().HasIndex(o => new { o.TenantId, o.OutletId, o.CreatedAt });
            modelBuilder.Entity<MenuItem>().HasIndex(m => new { m.TenantId, m.OutletId });
            modelBuilder.Entity<RestaurantTable>().HasIndex(t => new { t.TenantId, t.OutletId, t.TableNumber }).IsUnique();
            modelBuilder.Entity<MenuCategory>().HasIndex(c => new { c.TenantId, c.OutletId, c.SortOrder });
            modelBuilder.Entity<KitchenOrder>().HasIndex(o => new { o.TenantId, o.OutletId, o.BusinessDate, o.TokenNumber });

            foreach (var type in new[] { typeof(KitchenOrder), typeof(MenuItem), typeof(RestaurantTable), typeof(MenuCategory) })
            {
                modelBuilder.Entity(type).Property(nameof(TenantEntity.TenantId)).HasDefaultValue(1);
                modelBuilder.Entity(type).Property(nameof(TenantEntity.OutletId)).HasDefaultValue(1);
                modelBuilder.Entity(type).Property(nameof(TenantEntity.CreatedAt)).HasDefaultValueSql("now()");
                // A restaurant or outlet with data can never be deleted by accident.
                modelBuilder.Entity(type).HasOne(typeof(Tenant)).WithMany().HasForeignKey(nameof(TenantEntity.TenantId)).OnDelete(DeleteBehavior.Restrict);
                modelBuilder.Entity(type).HasOne(typeof(Outlet)).WithMany().HasForeignKey(nameof(TenantEntity.OutletId)).OnDelete(DeleteBehavior.Restrict);
            }

            modelBuilder.Entity<KitchenOrder>().Property(o => o.Status).HasConversion<string>();
            modelBuilder.Entity<KitchenOrder>().Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(OrderType.DineIn);
            modelBuilder.Entity<KitchenOrder>().Property(o => o.Version).IsConcurrencyToken();

            modelBuilder.Entity<OrderCounter>(b =>
            {
                b.ToTable("OrderCounters");
                b.HasKey(c => new { c.TenantId, c.OutletId, c.BusinessDate });
            });

            // Menu: existing items stay available and veg; GST 5% unless set. Sentinels let EF send false/0 explicitly.
            modelBuilder.Entity<MenuItem>().Property(m => m.IsAvailable).HasDefaultValue(true).HasSentinel(true);
            modelBuilder.Entity<RestaurantTable>().Property(t => t.IsActive).HasDefaultValue(true).HasSentinel(true);

            modelBuilder.Entity<OutletLogo>(b =>
            {
                b.HasKey(l => l.OutletId);
                b.Property(l => l.OutletId).ValueGeneratedNever();
                b.HasOne<Outlet>().WithOne().HasForeignKey<OutletLogo>(l => l.OutletId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AuditLog>(b =>
            {
                b.Property(a => a.Details).HasColumnType("jsonb");
                b.HasIndex(a => new { a.TenantId, a.Id });
                b.HasQueryFilter(a => a.TenantId == CurrentTenantId);
            });
            modelBuilder.Entity<MenuItem>().Property(m => m.IsVeg).HasDefaultValue(true).HasSentinel(true);
            modelBuilder.Entity<MenuItem>().Property(m => m.TaxRate).HasPrecision(5, 2).HasDefaultValue(5m).HasSentinel(-1m);
            modelBuilder.Entity<MenuItem>().HasOne<MenuCategory>().WithMany().HasForeignKey(m => m.CategoryId).OnDelete(DeleteBehavior.Restrict);

            // Deleting a table must not delete its orders.
            modelBuilder.Entity<KitchenOrder>()
                .HasOne(o => o.Table)
                .WithMany()
                .HasForeignKey(o => o.TableId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<KitchenOrder>()
                .HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.KitchenOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Money: numeric(12,2).
            modelBuilder.Entity<MenuItem>().Property(m => m.Price).HasPrecision(12, 2);
            modelBuilder.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(12, 2);
            modelBuilder.Entity<OrderItem>().Property(i => i.TenantId).HasDefaultValue(1);
            modelBuilder.Entity<OrderItem>().HasIndex(i => new { i.TenantId, i.KitchenOrderId });

            modelBuilder.Entity<KitchenOrder>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
            modelBuilder.Entity<MenuItem>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
            modelBuilder.Entity<RestaurantTable>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
            modelBuilder.Entity<OrderItem>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
            modelBuilder.Entity<MenuCategory>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

            base.OnModelCreating(modelBuilder);

            // New users are active; sentinel true lets EF send an explicit false when needed.
            modelBuilder.Entity<AppUser>().Property(u => u.IsActive).HasDefaultValue(true).HasSentinel(true);
        }

        // SaveChanges and SaveChangesAsync are in AppDbContext.Audit.cs (they also write the audit log).

        // New rows get the current restaurant, outlet and user; changed rows get who and when.
        private void ApplyAuditFields()
        {
            var now = DateTime.UtcNow;
            foreach (var entry in ChangeTracker.Entries<TenantEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.TenantId = CurrentTenantId;
                    entry.Entity.OutletId = CurrentOutletId;
                    entry.Entity.CreatedBy ??= CurrentUserId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = CurrentUserId;
                }
            }

            foreach (var entry in ChangeTracker.Entries<OrderItem>().Where(e => e.State == EntityState.Added))
                entry.Entity.TenantId = CurrentTenantId;
        }
    }
}
