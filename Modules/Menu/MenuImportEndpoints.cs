using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;

namespace POSSystem.Menu
{
    public record ScannedItemDto(string Category, string Name, decimal Price, bool IsVeg, string? Description, bool AlreadyOnMenu);
    public record MenuScanResult(IReadOnlyList<ScannedItemDto> Items, int ScansLeftToday);

    public record ImportItem(string? Category, string? Name, decimal Price, bool IsVeg, string? Description);
    public record MenuImportRequest(List<ImportItem>? Items, decimal TaxRate = 5m);
    public record MenuImportResult(int Created, int Skipped, int CategoriesCreated);

    // Menu from photos (Gemini) or a CSV: scan returns a draft for the owner to check; import saves the checked rows.
    public static class MenuImportEndpoints
    {
        public const int MaxPhotos = 4;
        public const int MaxPhotoBytes = 5 * 1024 * 1024;
        public const int ScansPerDay = 20;
        private static readonly decimal[] GstRates = { 0m, 5m, 12m, 18m, 28m };

        public static IServiceCollection AddMenuScan(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddHttpClient<IMenuReader, GeminiMenuReader>(c => c.Timeout = TimeSpan.FromSeconds(120));
            return services;
        }

        public static WebApplication MapMenuImportEndpoints(this WebApplication app)
        {
            var admin = app.MapGroup("/api/admin/menu").RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Menu admin");

            admin.MapPost("/scan", async (HttpRequest request, AppDbContext db, IMenuReader reader, IMemoryCache cache, CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Problem("photos", "Send the menu photos as a form upload.");
                var form = await request.ReadFormAsync(ct);
                var files = form.Files;
                if (files.Count == 0 || files.Count > MaxPhotos)
                    return Problem("photos", $"Choose 1 to {MaxPhotos} photos of the menu.");

                var photos = new List<MenuPhoto>();
                foreach (var file in files)
                {
                    if (file.Length == 0 || file.Length > MaxPhotoBytes)
                        return Problem("photos", "Each photo must be at most 5 MB.");
                    using var buffer = new MemoryStream();
                    await file.CopyToAsync(buffer, ct);
                    var bytes = buffer.ToArray();
                    // Trust the file content, not its name or the browser's content type.
                    var type = LogoAndAuditEndpoints.DetectImageType(bytes);
                    if (type == null) return Problem("photos", "Use JPG, PNG or WebP photos.");
                    photos.Add(new MenuPhoto(bytes, type));
                }

                // Each restaurant gets a fixed number of scans per day (counted on this server).
                var key = $"menu-scans:{db.CurrentTenantId}:{DateTime.UtcNow:yyyyMMdd}";
                var used = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1); return 0; });
                if (used >= ScansPerDay)
                    return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                        title: $"You can scan {ScansPerDay} times a day. Try again tomorrow, or add items by hand.");
                cache.Set(key, used + 1, TimeSpan.FromDays(1));

                IReadOnlyList<MenuDraftItem> draft;
                try
                {
                    draft = await reader.ReadAsync(photos, ct);
                }
                catch (MenuScanUnavailableException ex)
                {
                    cache.Set(key, used, TimeSpan.FromDays(1));
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ex.Message);
                }
                catch (MenuScanFailedException ex)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
                }

                var existing = await ExistingNames(db);
                var items = MenuDraftRules.Clean(draft)
                    .Select(i => new ScannedItemDto(i.Category, i.Name, i.Price, i.IsVeg, i.Description, existing.Contains(i.Name)))
                    .ToList();
                return Results.Ok(new MenuScanResult(items, ScansPerDay - used - 1));
            }).DisableAntiforgery()
              .WithName("ScanMenuPhotos").WithSummary("Read dishes and prices from 1-4 photos of a paper menu (nothing is saved)");

            admin.MapPost("/import", async (MenuImportRequest req, AppDbContext db) =>
            {
                var errors = Validate(req);
                if (errors.Count > 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = errors.ToArray() });

                var existing = await ExistingNames(db);
                var categories = await db.MenuCategories.Where(c => c.OutletId == db.CurrentOutletId).ToListAsync();
                var byName = new Dictionary<string, MenuCategory>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in categories) byName.TryAdd(c.Name.Trim(), c);
                var nextCategoryOrder = categories.Select(c => c.SortOrder).DefaultIfEmpty(0).Max() + 1;

                var rows = MenuDraftRules.Clean(req.Items!.Select(i => new MenuDraftItem(i.Category ?? "", i.Name ?? "", i.Price, i.IsVeg, i.Description)));
                var toAdd = rows.Where(r => !existing.Contains(r.Name)).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                var skipped = req.Items!.Count - toAdd.Count;

                // Categories first (items need their ids), then items; one database transaction on PostgreSQL.
                await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
                var newCategories = 0;
                foreach (var name in toAdd.Select(r => r.Category).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (byName.ContainsKey(name)) continue;
                    var category = new MenuCategory { Name = name, SortOrder = nextCategoryOrder++, IsActive = true };
                    db.MenuCategories.Add(category);
                    byName[name] = category;
                    newCategories++;
                }
                await db.SaveChangesAsync();

                var itemOrder = await db.MenuItems.Where(m => m.OutletId == db.CurrentOutletId && m.CategoryId != null)
                    .GroupBy(m => m.CategoryId!.Value).Select(g => new { g.Key, Max = g.Max(m => m.SortOrder) })
                    .ToDictionaryAsync(x => x.Key, x => x.Max);
                foreach (var row in toAdd)
                {
                    var category = byName[row.Category];
                    var order = itemOrder.GetValueOrDefault(category.Id) + 1;
                    itemOrder[category.Id] = order;
                    db.MenuItems.Add(new MenuItem
                    {
                        Name = row.Name,
                        Price = row.Price,
                        CategoryId = category.Id,
                        Category = category.Name,
                        Description = row.Description,
                        IsVeg = row.IsVeg,
                        IsAvailable = true,
                        TaxRate = req.TaxRate,
                        SortOrder = order
                    });
                }
                await db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();

                return Results.Ok(new MenuImportResult(toAdd.Count, skipped, newCategories));
            }).WithName("ImportMenuItems").WithSummary("Add checked menu rows (from a scan or a CSV); items already on the menu are skipped");

            return app;
        }

        private static IResult Problem(string field, string message) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { [field] = new[] { message } });

        private static async Task<HashSet<string>> ExistingNames(AppDbContext db)
        {
            var names = await db.MenuItems.Where(m => m.OutletId == db.CurrentOutletId && !m.IsDeleted).Select(m => m.Name).ToListAsync();
            return new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        // Every row is checked before anything is saved; messages name the row as the owner sees it (row 1 = first).
        private static List<string> Validate(MenuImportRequest req)
        {
            var errors = new List<string>();
            if (req.Items == null || req.Items.Count == 0) return new List<string> { "Choose at least one item to add." };
            if (req.Items.Count > MenuDraftRules.MaxItems) return new List<string> { $"Add at most {MenuDraftRules.MaxItems} items at a time." };
            if (!GstRates.Contains(req.TaxRate)) errors.Add("GST rate must be 0, 5, 12, 18 or 28.");
            for (var i = 0; i < req.Items.Count && errors.Count < 20; i++)
            {
                var item = req.Items[i];
                var row = i + 1;
                var name = item.Name?.Trim() ?? string.Empty;
                if (name.Length == 0 || name.Length > 100) errors.Add($"Row {row}: enter a name of up to 100 characters.");
                if ((item.Category?.Trim().Length ?? 0) > 100) errors.Add($"Row {row}: the category can be at most 100 characters.");
                if (item.Price < 0 || item.Price > 100000) errors.Add($"Row {row}: enter a price between 0 and 100000.");
                if ((item.Description?.Length ?? 0) > 500) errors.Add($"Row {row}: the description can be at most 500 characters.");
            }
            return errors;
        }
    }
}
