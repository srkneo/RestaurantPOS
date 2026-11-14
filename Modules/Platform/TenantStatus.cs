using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using POSSystem.Data;

namespace POSSystem.Platform
{
    // Is a restaurant disabled? Cached for a minute, cleared at once when the platform owner changes it.
    public class TenantStatus
    {
        private readonly IMemoryCache _cache;

        public TenantStatus(IMemoryCache cache)
        {
            _cache = cache;
        }

        private static string Key(int tenantId) => $"tenant-disabled:{tenantId}";

        // Unknown restaurants count as not disabled; their data is still kept apart by the tenant filter.
        public async Task<bool> IsDisabledAsync(AppDbContext db, int tenantId)
        {
            if (tenantId <= 0) return false;
            return await _cache.GetOrCreateAsync(Key(tenantId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
                return tenant != null && !tenant.IsActive;
            });
        }

        public void Forget(int tenantId) => _cache.Remove(Key(tenantId));
    }
}
