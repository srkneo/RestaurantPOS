using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // One row of the Activity log screen (UI1). Details is the JSON text as stored.
    public sealed record AuditEntry(long Id, DateTime CreatedAt, string By, string Action, string EntityType, string? EntityId, string? Details, bool Support);

    // Reads the audit log for GET /api/admin/audit (owner) and GET /api/platform/tenants/{id}/audit (platform).
    public static class AuditLogQuery
    {
        public const int DefaultTake = 100;
        public const int MaxTake = 500;
        public const string SupportType = "Support";

        public static async Task<List<AuditEntry>> ReadAsync(IQueryable<AuditLog> logs, int? take, string? type, UserManager<AppUser> users)
        {
            var count = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
            if (!string.IsNullOrWhiteSpace(type))
            {
                var wanted = type.Trim();
                logs = logs.Where(l => l.EntityType == wanted);
            }
            var rows = await logs.OrderByDescending(l => l.Id).Take(count).ToListAsync();

            var ids = rows.Where(l => !string.IsNullOrEmpty(l.CreatedBy)).Select(l => l.CreatedBy!).Distinct().ToList();
            var people = await users.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.Email ?? u.UserName ?? u.Id, u.IsSupport })
                .ToListAsync();
            var byId = people.ToDictionary(p => p.Id);

            return rows.Select(l =>
            {
                var person = string.IsNullOrEmpty(l.CreatedBy) ? null : byId.GetValueOrDefault(l.CreatedBy);
                var by = string.IsNullOrEmpty(l.CreatedBy) ? "system" : person?.Name ?? l.CreatedBy;
                var support = l.EntityType == SupportType || person?.IsSupport == true;
                return new AuditEntry(l.Id, l.CreatedAt, by, l.Action, l.EntityType, l.EntityId, l.Details, support);
            }).ToList();
        }
    }
}
