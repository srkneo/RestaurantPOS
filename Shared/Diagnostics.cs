using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using POSSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace POSSystem.Logging
{
    // OB9: a quick look at the server's health for the platform owner, and a public version check.
    public static class Diagnostics
    {
        public record VersionDto(string Version, DateTime StartedAtUtc, long UptimeSeconds);

        public record DiagnosticsDto(string Version, string Environment, DateTime StartedAtUtc, long UptimeSeconds,
            bool DatabaseOk, bool SentryOn, bool TelemetryOn, int Restaurants, int FailedSignIns24h,
            IReadOnlyList<SecurityEvents.SecurityEventRow> RecentEvents);

        private static readonly DateTime Started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

        public static VersionDto Version(IConfiguration config)
        {
            var v = config["App:Version"];
            var up = (long)Math.Max(0, (DateTime.UtcNow - Started).TotalSeconds);
            return new VersionDto(string.IsNullOrWhiteSpace(v) ? "dev" : v.Trim(), Started, up);
        }

        public static WebApplication MapDiagnosticsEndpoints(this WebApplication app)
        {
            app.MapGet("/health/version", (IConfiguration config) => Results.Ok(Version(config)))
                .AllowAnonymous()
                .WithTags("About")
                .WithName("GetVersion")
                .WithSummary("The running version and how long the server has been up (no settings or secrets)");

            app.MapGet("/api/platform/diagnostics", async (AppDbContext db, IConfiguration config, IWebHostEnvironment env, CancellationToken ct) =>
            {
                var v = Version(config);
                bool dbOk;
                try { dbOk = await db.Database.CanConnectAsync(ct); }
                catch (Exception) { dbOk = false; }

                int restaurants = 0, failed = 0;
                var recent = new List<SecurityEvents.SecurityEventRow>();
                if (dbOk)
                {
                    try
                    {
                        restaurants = await db.Tenants.CountAsync(ct);
                        var since = DateTime.UtcNow.AddHours(-24);
                        failed = await db.SecurityEvents.CountAsync(e => e.AtUtc >= since && !e.Success
                            && (e.Type == "sign-in" || e.Type == "sign-in-code"), ct);
                        recent = await db.SecurityEvents.AsNoTracking().OrderByDescending(e => e.Id).Take(10)
                            .Select(e => new SecurityEvents.SecurityEventRow(e.AtUtc, e.TenantId, e.UserId, e.Type, e.Path, e.Status, e.Success, e.Ip, e.RequestId, e.TargetId))
                            .ToListAsync(ct);
                    }
                    catch (Exception) { dbOk = false; }
                }

                return Results.Ok(new DiagnosticsDto(v.Version, env.EnvironmentName, v.StartedAtUtc, v.UptimeSeconds,
                    dbOk, SentrySetup.Dsn(config) != null, TelemetrySetup.Enabled(config), restaurants, failed, recent));
            })
            .RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole))
            .WithTags("Platform")
            .WithName("GetDiagnostics")
            .WithSummary("Server health for the platform owner: version, database, monitoring on/off, recent security events");
            return app;
        }
    }
}
