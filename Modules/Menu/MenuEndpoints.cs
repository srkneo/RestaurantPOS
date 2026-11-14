using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;

namespace POSSystem.Menu
{
    public record CategoryDto(int Id, string Name, int SortOrder, bool IsActive);
    public record CategoryRequest(string? Name, int SortOrder, bool IsActive = true);
    public record MenuItemAdminDto(int Id, string Name, decimal Price, int? CategoryId, string Category, string? Description,
        bool IsVeg, bool IsAvailable, decimal TaxRate, int SortOrder);
    public record MenuItemRequest(string? Name, decimal Price, int? CategoryId, string? Description, bool IsVeg, bool IsAvailable, decimal TaxRate, int SortOrder);
    public record AvailabilityRequest(bool IsAvailable);

    public static class MenuEndpoints
    {
        private static readonly decimal[] GstRates = { 0m, 5m, 12m, 18m, 28m };

        public static WebApplication MapMenuEndpoints(this WebApplication app)
        {
            // POS: items that can be sold right now.
            app.MapGet("/api/menuitems", async (IMenuItemService menuItemService) =>
                Results.Ok(await menuItemService.GetMenuItems()))
                .WithName("GetMenuItems").WithSummary("List menu items");

            // Owner and manager: full menu, including sold-out items.
            var admin = app.MapGroup("/api/admin/menu").RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Menu admin");

            admin.MapGet("/categories", async (AppDbContext db) =>
                Results.Ok(await db.MenuCategories.Where(c => c.OutletId == db.CurrentOutletId)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                    .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive)).ToListAsync()))
                .WithName("GetMenuCategories").WithSummary("List menu categories");

            admin.MapPost("/categories", async (CategoryRequest req, AppDbContext db) =>
            {
                var errors = await ValidateCategory(db, req, null);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                var category = new MenuCategory { Name = req.Name!.Trim(), SortOrder = req.SortOrder, IsActive = req.IsActive };
                db.MenuCategories.Add(category);
                await db.SaveChangesAsync();
                return Results.Created($"/api/admin/menu/categories/{category.Id}", new CategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive));
            }).WithName("CreateMenuCategory").WithSummary("Add a menu category");

            admin.MapPut("/categories/{id:int}", async (int id, CategoryRequest req, AppDbContext db) =>
            {
                var category = await db.MenuCategories.FirstOrDefaultAsync(c => c.Id == id);
                if (category == null) return Results.NotFound();
                var errors = await ValidateCategory(db, req, id);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                category.Name = req.Name!.Trim();
                category.SortOrder = req.SortOrder;
                category.IsActive = req.IsActive;
                // Keep the category name copied on items in step.
                foreach (var item in await db.MenuItems.Where(m => m.CategoryId == id).ToListAsync()) item.Category = category.Name;
                await db.SaveChangesAsync();
                return Results.Ok(new CategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive));
            }).WithName("UpdateMenuCategory").WithSummary("Rename or reorder a menu category");

            admin.MapGet("/items", async (AppDbContext db) =>
            {
                var categories = await db.MenuCategories.ToDictionaryAsync(c => c.Id);
                var items = await db.MenuItems.Where(m => m.OutletId == db.CurrentOutletId && !m.IsDeleted).ToListAsync();
                return Results.Ok(items
                    .OrderBy(m => m.CategoryId.HasValue && categories.TryGetValue(m.CategoryId.Value, out var c) ? c.SortOrder : int.MaxValue)
                    .ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
                    .Select(ToDto));
            }).WithName("GetMenuAdminItems").WithSummary("List all menu items, including sold out");

            admin.MapPost("/items", async (MenuItemRequest req, AppDbContext db) =>
            {
                var (errors, category) = await ValidateItem(db, req);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                var item = new MenuItem();
                Apply(item, req, category!);
                db.MenuItems.Add(item);
                await db.SaveChangesAsync();
                return Results.Created($"/api/admin/menu/items/{item.Id}", ToDto(item));
            }).WithName("CreateMenuItem").WithSummary("Add a menu item");

            admin.MapPut("/items/{id:int}", async (int id, MenuItemRequest req, AppDbContext db) =>
            {
                var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
                if (item == null) return Results.NotFound();
                var (errors, category) = await ValidateItem(db, req);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                Apply(item, req, category!);
                await db.SaveChangesAsync();
                return Results.Ok(ToDto(item));
            }).WithName("UpdateMenuItem").WithSummary("Change a menu item");

            admin.MapPut("/items/{id:int}/availability", async (int id, AvailabilityRequest req, AppDbContext db) =>
            {
                var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
                if (item == null) return Results.NotFound();
                item.IsAvailable = req.IsAvailable;
                await db.SaveChangesAsync();
                return Results.Ok(ToDto(item));
            }).WithName("SetMenuItemAvailability").WithSummary("Mark an item sold out or available again");

            admin.MapDelete("/items/{id:int}", async (int id, AppDbContext db) =>
            {
                var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
                if (item == null) return Results.NotFound();
                // Soft delete: past orders and bills keep pointing at the item.
                item.IsDeleted = true;
                item.IsAvailable = false;
                await db.SaveChangesAsync();
                return Results.NoContent();
            }).WithName("DeleteMenuItem").WithSummary("Remove an item from the menu");

            return app;
        }

        private static MenuItemAdminDto ToDto(MenuItem m) =>
            new(m.Id, m.Name, m.Price, m.CategoryId, m.Category, m.Description, m.IsVeg, m.IsAvailable, m.TaxRate, m.SortOrder);

        private static void Apply(MenuItem item, MenuItemRequest req, MenuCategory category)
        {
            item.Name = req.Name!.Trim();
            item.Price = Math.Round(req.Price, 2);
            item.CategoryId = category.Id;
            item.Category = category.Name;
            item.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            item.IsVeg = req.IsVeg;
            item.IsAvailable = req.IsAvailable;
            item.TaxRate = req.TaxRate;
            item.SortOrder = req.SortOrder;
        }

        private static async Task<Dictionary<string, string[]>> ValidateCategory(AppDbContext db, CategoryRequest req, int? id)
        {
            var errors = new Dictionary<string, string[]>();
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100)
                errors["name"] = new[] { "Enter a name of up to 100 characters." };
            else if (await db.MenuCategories.AnyAsync(c => c.OutletId == db.CurrentOutletId && c.Name == name && c.Id != id))
                errors["name"] = new[] { "A category with this name already exists." };
            return errors;
        }

        private static async Task<(Dictionary<string, string[]> Errors, MenuCategory? Category)> ValidateItem(AppDbContext db, MenuItemRequest req)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 100)
                errors["name"] = new[] { "Enter a name of up to 100 characters." };
            if (req.Price < 0 || req.Price > 100000)
                errors["price"] = new[] { "Enter a price between 0 and 100000." };
            if (!GstRates.Contains(req.TaxRate))
                errors["taxrate"] = new[] { "GST rate must be 0, 5, 12, 18 or 28." };
            if (req.Description is { Length: > 500 })
                errors["description"] = new[] { "The description can be at most 500 characters." };

            MenuCategory? category = null;
            if (req.CategoryId is not > 0 || (category = await db.MenuCategories.FirstOrDefaultAsync(c => c.Id == req.CategoryId)) == null)
                errors["categoryid"] = new[] { "Choose a category." };
            return (errors, category);
        }
    }
}
