using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public static class LogoAndAuditEndpoints
    {
        public const int MaxLogoBytes = 512 * 1024;

        public static WebApplication MapLogoAndAuditEndpoints(this WebApplication app)
        {
            // Any signed-in screen may show the logo of its own outlet.
            app.MapGet("/api/store/logo", async (AppDbContext db) =>
            {
                var logo = await db.OutletLogos.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.OutletId == db.CurrentOutletId && l.TenantId == db.CurrentTenantId);
                return logo == null ? Results.NotFound() : Results.File(logo.Bytes, logo.ContentType);
            }).WithName("GetStoreLogo").WithSummary("Get the outlet logo");

            app.MapPost("/api/admin/store/logo", async (IFormFile file, AppDbContext db) =>
            {
                if (file.Length == 0 || file.Length > MaxLogoBytes)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["logo"] = new[] { "The logo must be an image of up to 512 KB." } });

                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                var bytes = buffer.ToArray();
                // Trust the file content, not its name or the browser's content type.
                var contentType = DetectImageType(bytes);
                if (contentType == null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["logo"] = new[] { "Use a PNG, JPG or WebP image." } });

                var logo = await db.OutletLogos.FirstOrDefaultAsync(l => l.OutletId == db.CurrentOutletId && l.TenantId == db.CurrentTenantId);
                if (logo == null)
                {
                    logo = new OutletLogo { OutletId = db.CurrentOutletId, TenantId = db.CurrentTenantId };
                    db.OutletLogos.Add(logo);
                }
                logo.Bytes = bytes;
                logo.ContentType = contentType;
                logo.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Results.NoContent();
            }).RequireAuthorization(p => p.RequireRole("admin")).DisableAntiforgery()
              .WithName("UploadStoreLogo").WithSummary("Upload the outlet logo (PNG, JPG or WebP, up to 512 KB)");

            app.MapDelete("/api/admin/store/logo", async (AppDbContext db) =>
            {
                var logo = await db.OutletLogos.FirstOrDefaultAsync(l => l.OutletId == db.CurrentOutletId && l.TenantId == db.CurrentTenantId);
                if (logo != null)
                {
                    db.OutletLogos.Remove(logo);
                    await db.SaveChangesAsync();
                }
                return Results.NoContent();
            }).RequireAuthorization(p => p.RequireRole("admin")).WithName("DeleteStoreLogo").WithSummary("Remove the outlet logo");

            // Owner only: latest changes first, with the email of who made them.
            app.MapGet("/api/admin/audit", async (int? take, AppDbContext db, UserManager<AppUser> users) =>
            {
                var count = Math.Clamp(take ?? 100, 1, 500);
                var logs = await db.AuditLogs.OrderByDescending(l => l.Id).Take(count).ToListAsync();
                var ids = logs.Where(l => l.CreatedBy != null).Select(l => l.CreatedBy!).Distinct().ToList();
                var emails = await users.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);
                return Results.Ok(logs.Select(l => new
                {
                    l.Id,
                    l.CreatedAt,
                    By = l.CreatedBy == null ? "system" : emails.GetValueOrDefault(l.CreatedBy, l.CreatedBy),
                    l.Action,
                    l.EntityType,
                    l.EntityId,
                    l.Details
                }));
            }).RequireAuthorization(p => p.RequireRole("admin")).WithName("GetAuditLog").WithSummary("Latest changes: who changed what and when");

            return app;
        }

        public static string? DetectImageType(byte[] b)
        {
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return "image/webp";
            return null;
        }
    }
}
