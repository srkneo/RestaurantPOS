using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Automatic audit log: every save records who created, changed or deleted menu, tables, store, logo and staff,
    // with old and new values, plus order cancellations. Secrets (password hash etc.) are never written.
    public partial class AppDbContext
    {
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<OutletLogo> OutletLogos { get; set; }

        private static readonly HashSet<string> AuditedTypes = new()
        {
            nameof(MenuItem), nameof(MenuCategory), nameof(RestaurantTable), nameof(Outlet), nameof(Tenant), nameof(AppUser), nameof(OutletLogo)
        };

        private static readonly HashSet<string> HiddenProperties = new()
        {
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "NormalizedEmail", "NormalizedUserName", "Bytes",
            "AccessFailedCount", "LockoutEnd", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
        };

        private List<(EntityEntry Entry, AuditLog Log)> CollectAudit()
        {
            var pending = new List<(EntityEntry Entry, AuditLog Log)>();
            foreach (var entry in ChangeTracker.Entries().ToList())
            {
                if (entry.Entity is AuditLog) continue;
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

                var type = entry.Entity.GetType().Name;
                var isCancel = entry.Entity is KitchenOrder && entry.State == EntityState.Modified
                    && entry.Property(nameof(KitchenOrder.CancelReason)).IsModified;
                if (!AuditedTypes.Contains(type) && !isCancel) continue;

                var changes = new Dictionary<string, object?[]>();
                foreach (var p in entry.Properties)
                {
                    var name = p.Metadata.Name;
                    if (HiddenProperties.Contains(name)) continue;
                    if (entry.State == EntityState.Modified && (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue))) continue;
                    var before = entry.State == EntityState.Added ? null : p.OriginalValue;
                    var after = entry.State == EntityState.Deleted ? null : p.CurrentValue;
                    changes[name] = new[] { before, after };
                }
                if (entry.State == EntityState.Modified && changes.Count == 0) continue;

                var action = entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Deleted => "Deleted",
                    _ => isCancel ? "Cancelled" : "Changed"
                };
                pending.Add((entry, new AuditLog
                {
                    Action = action,
                    EntityType = isCancel ? nameof(KitchenOrder) : type,
                    EntityId = entry.State == EntityState.Added ? null : KeyOf(entry),
                    TenantId = TenantOf(entry.Entity),
                    Details = JsonSerializer.Serialize(new { changes })
                }));
            }
            return pending;
        }

        private static string? KeyOf(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();
            return key == null ? null : string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
        }

        private int TenantOf(object entity) => entity switch
        {
            AppUser u => u.TenantId,
            Tenant t => t.Id,
            Outlet o => o.TenantId,
            OutletLogo l => l.TenantId,
            TenantEntity e => e.TenantId,
            _ => CurrentTenantId
        };

        // Runs after the main save, so new rows already have their ids.
        private bool AddAuditRows(List<(EntityEntry Entry, AuditLog Log)> pending)
        {
            if (pending.Count == 0) return false;
            var now = DateTime.UtcNow;
            foreach (var (entry, log) in pending)
            {
                // New rows only have their id after the main save.
                log.EntityId ??= KeyOf(entry);
                log.OutletId = CurrentOutletId;
                log.CreatedBy = CurrentUserId;
                log.CreatedAt = now;
                AuditLogs.Add(log);
            }
            return true;
        }

        public override int SaveChanges()
        {
            ApplyAuditFields();
            var pending = CollectAudit();
            var result = base.SaveChanges();
            if (AddAuditRows(pending)) base.SaveChanges();
            return result;
        }

        public override async System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            ApplyAuditFields();
            var pending = CollectAudit();
            var result = await base.SaveChangesAsync(cancellationToken);
            if (AddAuditRows(pending)) await base.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
