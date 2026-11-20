using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POSSystem.Data;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace POSSystem.Logging
{
    // OB8: one row per security-relevant request. No passwords, codes or request bodies are stored.
    public class SecurityEvent
    {
        public long Id { get; set; }
        public DateTime AtUtc { get; set; }
        public int? TenantId { get; set; }
        public string? UserId { get; set; }
        public string Type { get; set; } = "";
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public int Status { get; set; }
        public bool Success { get; set; }
        public string? Ip { get; set; }
        public string? RequestId { get; set; }
        public string? TargetId { get; set; }
    }

    public static class SecurityEvents
    {
        public const int KeepDays = 365;

        // Set by the sign-in endpoint once the account is found, so failed passwords are tied to the account.
        public const string UserItem = "SecurityEvents.UserId";

        // Which requests are security events, by their route (GET requests never are).
        public static string? Classify(string method, string? route)
        {
            if (string.IsNullOrEmpty(route) || HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
                return null;
            var r = route.TrimEnd('/').ToLowerInvariant();
            if (r == "/api/auth/login") return "sign-in";
            if (r == "/api/auth/login/code") return "sign-in-code";
            if (r == "/api/auth/logout") return "sign-out";
            if (r == "/api/auth/owner-reset") return "owner-password-reset";
            if (r.StartsWith("/api/auth/2fa/")) return "2fa-" + r.Substring("/api/auth/2fa/".Length);
            if (r == "/api/staff") return HttpMethods.IsPost(method) ? "staff-create" : null;
            if (r == "/api/staff/{id}") return HttpMethods.IsDelete(method) ? "staff-delete" : "staff-update";
            if (r == "/api/staff/{id}/password") return "staff-password-reset";
            if (r == "/api/staff/{id}/pin") return "staff-pin";
            if (r == "/api/platform" || r.StartsWith("/api/platform/")) return "platform-change";
            return null;
        }

        private static string? Cut(string? s, int max) =>
            string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s.Substring(0, max);

        // After sign-in (so the caller is known). Saved through its own database context, so nothing half-done
        // from the request itself is ever saved with it. A failure to save never breaks the request.
        public static IApplicationBuilder UseSecurityEvents(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                await next();
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
                var type = Classify(context.Request.Method, route);
                if (type == null) return;
                try
                {
                    var user = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? context.Items[UserItem] as string;
                    var tenantClaim = context.User?.FindFirst("tenant_id")?.Value;
                    context.Request.RouteValues.TryGetValue("id", out var target);
                    using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.SecurityEvents.Add(new SecurityEvent
                    {
                        AtUtc = DateTime.UtcNow,
                        TenantId = int.TryParse(tenantClaim, out var t) && t > 0 ? t : null,
                        UserId = Cut(user, 64),
                        Type = type,
                        Method = context.Request.Method,
                        Path = Cut(context.Request.Path.Value, 200) ?? "",
                        Status = context.Response.StatusCode,
                        Success = context.Response.StatusCode is >= 200 and < 300,
                        Ip = Cut(context.Connection.RemoteIpAddress?.ToString(), 45),
                        RequestId = Cut(context.TraceIdentifier, 64),
                        TargetId = Cut(target?.ToString(), 64),
                    });
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SecurityEvents")
                        .LogWarning(ex, "Security event {Type} could not be saved", type);
                }
            });

        public record SecurityEventRow(DateTime AtUtc, int? TenantId, string? UserId, string Type, string Path, int Status, bool Success, string? Ip, string? RequestId, string? TargetId);

        public static WebApplication MapSecurityEventEndpoints(this WebApplication app)
        {
            app.MapGet("/api/platform/security-events", async (AppDbContext db, int? days, string? type) =>
            {
                var since = DateTime.UtcNow.AddDays(-Math.Clamp(days ?? 7, 1, KeepDays));
                var q = db.SecurityEvents.AsNoTracking().Where(e => e.AtUtc >= since);
                if (!string.IsNullOrWhiteSpace(type)) q = q.Where(e => e.Type == type);
                var rows = await q.OrderByDescending(e => e.Id).Take(500)
                    .Select(e => new SecurityEventRow(e.AtUtc, e.TenantId, e.UserId, e.Type, e.Path, e.Status, e.Success, e.Ip, e.RequestId, e.TargetId))
                    .ToListAsync();
                return Results.Ok(rows);
            })
            .RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole))
            .WithTags("Platform")
            .WithName("ListSecurityEvents")
            .WithSummary("Security events (sign-ins, password and staff changes), newest first");
            return app;
        }
    }

    // Deletes security events older than a year, once a day (first run 10 minutes after start).
    public class SecurityEventCleanup : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<SecurityEventCleanup> _log;

        public SecurityEventCleanup(IServiceScopeFactory scopes, ILogger<SecurityEventCleanup> log)
        {
            _scopes = scopes;
            _log = log;
        }

        public static async Task<int> RemoveOldAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct = default)
        {
            var cutoff = nowUtc.AddDays(-SecurityEvents.KeepDays);
            var removed = 0;
            while (true)
            {
                var old = await db.SecurityEvents.Where(e => e.AtUtc < cutoff).OrderBy(e => e.Id).Take(1000).ToListAsync(ct);
                if (old.Count == 0) return removed;
                db.SecurityEvents.RemoveRange(old);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                removed += old.Count;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var removed = await RemoveOldAsync(db, DateTime.UtcNow, stoppingToken);
                    if (removed > 0) _log.LogInformation("Removed {Count} security events older than {Days} days", removed, SecurityEvents.KeepDays);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _log.LogWarning(ex, "Security event cleanup failed"); }
                try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
