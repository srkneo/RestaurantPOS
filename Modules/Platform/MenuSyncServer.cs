using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // v1.7 cloud-to-shop menu sync, cloud side. The platform owner links a shop licence to a cloud restaurant; that shop PC
    // then copies the restaurant's menu from here (the cloud is the boss) and its own menu page becomes read-only.
    public record MenuSyncRequest(string? Key, string? DeviceId);
    public record LinkRestaurantRequest(int? TenantId);

    public record SyncCategory(int Id, string Name, int SortOrder, bool IsActive, string? AvailableFrom, string? AvailableTo);
    public record SyncStation(int Id, string Name, int SortOrder, bool IsActive);
    public record SyncItem(int Id, string Name, decimal Price, int? CategoryId, string Category, int? StationId, string? Description,
        bool IsVeg, bool IsAvailable, decimal TaxRate, int SortOrder, string SellUnit = "piece");
    public record SyncOption(int Id, int MenuItemId, string Name, decimal Price, int SortOrder, bool IsAvailable);
    public record SyncComboPart(int ComboItemId, int PartItemId, int Quantity);

    // The whole menu of one restaurant. Version changes whenever anything in it changes, so a shop applies it only once.
    public record MenuSnapshot(bool Linked, string? Version, List<SyncCategory> Categories, List<SyncStation> Stations, List<SyncItem> Items,
        List<SyncOption> Variants, List<SyncOption> AddOns, List<SyncComboPart> ComboParts, SyncStore? Store = null);

    // The restaurant's name, its first outlet's store profile (printed on bills) and its outlet settings (tax mode, tokens, ...).
    public record SyncStore(string RestaurantName, string OutletName, string TimeZone, int BusinessDayStartHour, string Settings,
        string? Address, string? Phone, string? Gstin, string? Fssai, string? ReceiptFooter);

    public static class MenuSyncServer
    {
        public static MenuSnapshot NotLinked() => new(false, null, new(), new(), new(), new(), new(), new());

        private static string? Time(TimeOnly? t) => t?.ToString("HH:mm", CultureInfo.InvariantCulture);

        // Reads one restaurant's menu without the signed-in user's restaurant filter, so the tenant is always given explicitly.
        public static async Task<MenuSnapshot> Build(AppDbContext db, int tenantId, CancellationToken ct)
        {
            var categories = (await db.MenuCategories.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Id).ToListAsync(ct))
                .Select(x => new SyncCategory(x.Id, x.Name, x.SortOrder, x.IsActive, Time(x.AvailableFrom), Time(x.AvailableTo))).ToList();
            var stations = (await db.KitchenStations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Id).ToListAsync(ct))
                .Select(x => new SyncStation(x.Id, x.Name, x.SortOrder, x.IsActive)).ToList();
            var items = (await db.MenuItems.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).OrderBy(x => x.Id).ToListAsync(ct))
                .Select(x => new SyncItem(x.Id, x.Name, x.Price, x.CategoryId, x.Category, x.StationId, x.Description, x.IsVeg, x.IsAvailable, x.TaxRate, x.SortOrder, x.SellUnit)).ToList();
            var itemIds = items.Select(x => x.Id).ToHashSet();
            var variants = (await db.ItemVariants.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Id).ToListAsync(ct))
                .Where(x => itemIds.Contains(x.MenuItemId))
                .Select(x => new SyncOption(x.Id, x.MenuItemId, x.Name, x.Price, x.SortOrder, x.IsAvailable)).ToList();
            var addOns = (await db.ItemAddOns.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Id).ToListAsync(ct))
                .Where(x => itemIds.Contains(x.MenuItemId))
                .Select(x => new SyncOption(x.Id, x.MenuItemId, x.Name, x.Price, x.SortOrder, x.IsAvailable)).ToList();
            var combos = (await db.ComboParts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Id).ToListAsync(ct))
                .Where(x => itemIds.Contains(x.ComboItemId) && itemIds.Contains(x.PartItemId))
                .Select(x => new SyncComboPart(x.ComboItemId, x.PartItemId, x.Quantity)).ToList();

            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            var outlet = await db.Outlets.IgnoreQueryFilters().AsNoTracking().Where(o => o.TenantId == tenantId).OrderBy(o => o.Id).FirstOrDefaultAsync(ct);
            var store = tenant is null || outlet is null ? null : new SyncStore(tenant.Name, outlet.Name, outlet.TimeZone, outlet.BusinessDayStartHour,
                outlet.Settings, outlet.Address, outlet.Phone, outlet.Gstin, outlet.Fssai, outlet.ReceiptFooter);

            var content = JsonSerializer.Serialize(new { categories, stations, items, variants, addOns, combos, store });
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..32].ToLowerInvariant();
            return new MenuSnapshot(true, version, categories, stations, items, variants, addOns, combos, store);
        }

        public static WebApplication MapMenuSyncServerEndpoints(this WebApplication app)
        {
            // Open to shop PCs (no sign-in), like licence renewal: the licence key and the PC it is bound to are the proof.
            app.MapPost("/api/licence/menu", async (MenuSyncRequest request, AppDbContext db, IConfiguration config, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(config["Licence:PrivateKey"])) return Results.NotFound(new { message = "The licence server is not switched on here." });
                var key = (request.Key ?? "").Trim().ToUpperInvariant();
                var deviceId = (request.DeviceId ?? "").Trim();
                if (key.Length == 0 || deviceId.Length is 0 or > 64) return Results.BadRequest(new { message = "The licence key or device id is missing." });

                var licence = await db.ShopLicences.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
                if (licence is null) return Results.NotFound(new { message = "This licence key is not known." });
                if (licence.Revoked || DateTime.UtcNow > licence.PaidUntil) return Results.Json(new { message = "This licence is not active." }, statusCode: 403);
                if (licence.DeviceId is null || licence.DeviceId != deviceId)
                    return Results.Json(new { message = "This licence key belongs to another PC." }, statusCode: 403);

                if (licence.TenantId is not int tenantId) return Results.Ok(NotLinked());
                return Results.Ok(await Build(db, tenantId, ct));
            })
                .AllowAnonymous().RequireRateLimiting(RateLimits.Login)
                .WithTags("Licence")
                .WithName("GetShopMenu").WithSummary("A shop PC linked to a cloud restaurant fetches that restaurant's menu");

            var group = app.MapGroup("/api/platform/licences").RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole)).WithTags("Platform");

            group.MapPost("/{id:int}/restaurant", async (int id, LinkRestaurantRequest request, AppDbContext db, LicenceIssuer issuer, CancellationToken ct) =>
            {
                var licence = await db.ShopLicences.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (licence is null) return Results.NotFound(new { message = "Licence not found." });
                if (request.TenantId is int tenantId && !await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
                    return Results.BadRequest(new { message = "That business does not exist." });
                licence.TenantId = request.TenantId;
                await db.SaveChangesAsync(ct);
                return Results.Ok(issuer.Describe(licence));
            })
                .WithName("LinkShopLicenceRestaurant")
                .WithSummary("Link a shop licence to a cloud restaurant so the shop PC copies its menu (empty = the shop keeps its own menu)");

            return app;
        }
    }
}
