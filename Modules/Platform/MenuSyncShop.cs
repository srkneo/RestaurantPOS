using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POSSystem.Data;
using POSSystem.Menu;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // v1.7 cloud-to-shop menu sync, shop PC side. When the platform owner links this PC's licence to a cloud restaurant,
    // the PC copies that restaurant's menu every few minutes (the cloud is the boss). Rows copied from the cloud keep the
    // cloud's id in CloudId, so applying the same menu again changes nothing and nothing is ever duplicated.
    public record MenuSyncStatusDto(bool ManagedByCloud, string? Version, DateTime? SyncedAt, string? Error);

    public interface IMenuCloud
    {
        Task<(MenuSnapshot? Snapshot, string? Error)> GetMenu(MenuSyncRequest request, CancellationToken ct);
    }

    public class HttpMenuCloud : IMenuCloud
    {
        private readonly HttpClient _http;
        private readonly string _url;

        public HttpMenuCloud(HttpClient http, IConfiguration config)
        {
            _http = http;
            _url = (config["Licence:ServerUrl"] ?? "").TrimEnd('/');
        }

        public async Task<(MenuSnapshot? Snapshot, string? Error)> GetMenu(MenuSyncRequest request, CancellationToken ct)
        {
            if (_url.Length == 0) return (null, "The licence server address is not set up on this PC.");
            try
            {
                using var res = await _http.PostAsJsonAsync(_url + "/api/licence/menu", request, ct);
                if (res.IsSuccessStatusCode) return (await res.Content.ReadFromJsonAsync<MenuSnapshot>(ct), null);
                return (null, $"The licence server answered {(int)res.StatusCode} for the menu.");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                return (null, "Could not reach the licence server for the menu. Check the internet connection.");
            }
        }
    }

    public static class MenuSyncApplier
    {
        private static TimeOnly? Time(string? text) =>
            TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

        // Makes this PC's menu the same as the snapshot. Rows the cloud no longer has are hidden, never removed,
        // because old orders and bills point at them. Saved in steps (new ids are needed for the next step);
        // if it stops half way the version is not stored, so the next sync simply applies it again.
        public static async Task Apply(AppDbContext db, MenuSnapshot s, CancellationToken ct)
        {
            var tenant = db.CurrentTenantId;

            var stations = await db.KitchenStations.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync(ct);
            var keptStations = new HashSet<KitchenStation>();
            foreach (var c in s.Stations)
            {
                var row = stations.FirstOrDefault(x => x.CloudId == c.Id);
                if (row is null) { row = new KitchenStation { CloudId = c.Id }; db.KitchenStations.Add(row); stations.Add(row); }
                row.Name = c.Name; row.SortOrder = c.SortOrder; row.IsActive = c.IsActive;
                keptStations.Add(row);
            }
            foreach (var row in stations.Where(x => !keptStations.Contains(x))) row.IsActive = false;

            var categories = await db.MenuCategories.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync(ct);
            var keptCategories = new HashSet<MenuCategory>();
            foreach (var c in s.Categories)
            {
                var row = categories.FirstOrDefault(x => x.CloudId == c.Id);
                if (row is null) { row = new MenuCategory { CloudId = c.Id }; db.MenuCategories.Add(row); categories.Add(row); }
                row.Name = c.Name; row.SortOrder = c.SortOrder; row.IsActive = c.IsActive;
                row.AvailableFrom = Time(c.AvailableFrom); row.AvailableTo = Time(c.AvailableTo);
                keptCategories.Add(row);
            }
            foreach (var row in categories.Where(x => !keptCategories.Contains(x))) row.IsActive = false;
            await db.SaveChangesAsync(ct);

            var stationIds = stations.Where(x => x.CloudId is not null).ToDictionary(x => x.CloudId!.Value, x => x.Id);
            var categoryIds = categories.Where(x => x.CloudId is not null).ToDictionary(x => x.CloudId!.Value, x => x.Id);

            var items = await db.MenuItems.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync(ct);
            var keptItems = new HashSet<MenuItem>();
            foreach (var c in s.Items)
            {
                var row = items.FirstOrDefault(x => x.CloudId == c.Id);
                // Sold out is decided in the shop: the cloud's value is used only when the item first arrives.
                if (row is null) { row = new MenuItem { CloudId = c.Id, IsAvailable = c.IsAvailable }; db.MenuItems.Add(row); items.Add(row); }
                row.Name = c.Name; row.Price = c.Price; row.Category = c.Category; row.Description = c.Description;
                row.CategoryId = c.CategoryId is int cat && categoryIds.TryGetValue(cat, out var localCat) ? localCat : null;
                row.StationId = c.StationId is int st && stationIds.TryGetValue(st, out var localSt) ? localSt : null;
                row.IsVeg = c.IsVeg; row.TaxRate = c.TaxRate; row.SortOrder = c.SortOrder; row.IsDeleted = false;
                keptItems.Add(row);
            }
            foreach (var row in items.Where(x => !keptItems.Contains(x))) row.IsDeleted = true;
            await db.SaveChangesAsync(ct);

            var itemIds = items.Where(x => x.CloudId is not null).ToDictionary(x => x.CloudId!.Value, x => x.Id);

            var variants = await db.ItemVariants.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync(ct);
            var keptVariants = new HashSet<ItemVariant>();
            foreach (var c in s.Variants.Where(v => itemIds.ContainsKey(v.MenuItemId)))
            {
                var row = variants.FirstOrDefault(x => x.CloudId == c.Id);
                if (row is null) { row = new ItemVariant { CloudId = c.Id }; db.ItemVariants.Add(row); variants.Add(row); }
                row.MenuItemId = itemIds[c.MenuItemId]; row.Name = c.Name; row.Price = c.Price; row.SortOrder = c.SortOrder; row.IsAvailable = c.IsAvailable;
                keptVariants.Add(row);
            }
            foreach (var row in variants.Where(x => !keptVariants.Contains(x))) row.IsAvailable = false;

            var addOns = await db.ItemAddOns.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync(ct);
            var keptAddOns = new HashSet<ItemAddOn>();
            foreach (var c in s.AddOns.Where(a => itemIds.ContainsKey(a.MenuItemId)))
            {
                var row = addOns.FirstOrDefault(x => x.CloudId == c.Id);
                if (row is null) { row = new ItemAddOn { CloudId = c.Id }; db.ItemAddOns.Add(row); addOns.Add(row); }
                row.MenuItemId = itemIds[c.MenuItemId]; row.Name = c.Name; row.Price = c.Price; row.SortOrder = c.SortOrder; row.IsAvailable = c.IsAvailable;
                keptAddOns.Add(row);
            }
            foreach (var row in addOns.Where(x => !keptAddOns.Contains(x))) row.IsAvailable = false;

            // Combo contents are only settings (orders copy them when placed), so they are simply rebuilt.
            db.ComboParts.RemoveRange(await db.ComboParts.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync(ct));
            foreach (var c in s.ComboParts.Where(p => itemIds.ContainsKey(p.ComboItemId) && itemIds.ContainsKey(p.PartItemId)))
                db.ComboParts.Add(new ComboPart { ComboItemId = itemIds[c.ComboItemId], PartItemId = itemIds[c.PartItemId], Quantity = c.Quantity });
            await db.SaveChangesAsync(ct);
        }
    }

    public class MenuSyncService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IMenuCloud _cloud;
        private readonly LicenceService _licence;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public MenuSyncService(IServiceScopeFactory scopes, IMenuCloud cloud, LicenceService licence)
        {
            _scopes = scopes;
            _cloud = cloud;
            _licence = licence;
        }

        private static MenuSyncStatusDto Describe(LicenceRecord? r) =>
            r is null ? new MenuSyncStatusDto(false, null, null, null) : new MenuSyncStatusDto(r.MenuFromCloud, r.MenuVersion, r.MenuSyncedUtc, r.MenuSyncError);

        public async Task<MenuSyncStatusDto> Status(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return Describe(await db.LicenceRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct));
        }

        // One sync: ask the cloud for the menu and apply it if it changed. Only on a shop PC with a licence key.
        public async Task<MenuSyncStatusDto> SyncOnce(CancellationToken ct)
        {
            if (!_licence.Required) return await Status(ct);
            var (key, deviceId) = await _licence.StoredKey(ct);
            if (string.IsNullOrEmpty(key)) return await Status(ct);

            await _gate.WaitAsync(ct);
            try
            {
                var (snapshot, error) = await _cloud.GetMenu(new MenuSyncRequest(key, deviceId), ct);
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var record = await db.LicenceRecords.FirstOrDefaultAsync(x => x.Id == 1, ct);
                if (record is null) return Describe(null);

                if (snapshot is null)
                {
                    record.MenuSyncError = error;
                }
                else if (!snapshot.Linked)
                {
                    record.MenuFromCloud = false;
                    record.MenuVersion = null;
                    record.MenuSyncError = null;
                    record.MenuSyncedUtc = DateTime.UtcNow;
                }
                else
                {
                    if (snapshot.Version != record.MenuVersion)
                    {
                        await MenuSyncApplier.Apply(db, snapshot, ct);
                        record.MenuVersion = snapshot.Version;
                    }
                    record.MenuFromCloud = true;
                    record.MenuSyncError = null;
                    record.MenuSyncedUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(ct);
                return Describe(record);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    // Checks the cloud for menu changes every 5 minutes while the PC is online.
    public class MenuSyncWorker : BackgroundService
    {
        private readonly MenuSyncService _sync;
        private readonly LicenceService _licence;
        private readonly ILogger<MenuSyncWorker> _log;

        public MenuSyncWorker(MenuSyncService sync, LicenceService licence, ILogger<MenuSyncWorker> log)
        {
            _sync = sync;
            _licence = licence;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            if (!_licence.Required) return;
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stop);
                while (!stop.IsCancellationRequested)
                {
                    try { await _sync.SyncOnce(stop); }
                    catch (Exception e) when (e is not OperationCanceledException) { _log.LogWarning(e, "Menu sync failed"); }
                    await Task.Delay(TimeSpan.FromMinutes(5), stop);
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    public static class MenuSyncShopSetup
    {
        public static IServiceCollection AddPosMenuSync(this IServiceCollection services)
        {
            services.AddHttpClient<IMenuCloud, HttpMenuCloud>(c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddSingleton<MenuSyncService>();
            services.AddHostedService<MenuSyncWorker>();
            return services;
        }

        public static WebApplication MapMenuSyncShopEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/admin/menu/sync").RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Menu");

            group.MapGet("/", async (MenuSyncService sync, CancellationToken ct) => Results.Ok(await sync.Status(ct)))
                .WithName("GetMenuSyncStatus").WithSummary("Is this PC's menu copied from the cloud, and when was it last updated?");

            group.MapPost("/now", async (MenuSyncService sync, CancellationToken ct) => Results.Ok(await sync.SyncOnce(ct)))
                .WithName("SyncMenuNow").WithSummary("Fetch the menu from the cloud now instead of waiting a few minutes");

            return app;
        }
    }
}
