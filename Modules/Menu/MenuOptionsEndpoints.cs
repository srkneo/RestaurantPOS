using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Menu
{
    public record MenuOptionDto(int Id, string Name, decimal Price, bool IsAvailable);
    public record MenuOptionRequest(int? Id, string? Name, decimal Price, bool IsAvailable = true);
    public record MenuOptionsDto(List<MenuOptionDto> Variants, List<MenuOptionDto> AddOns);
    public record MenuOptionsRequest(List<MenuOptionRequest>? Variants, List<MenuOptionRequest>? AddOns);

    // What the POS gets for each item: the item plus its sizes and extras.
    public record PosMenuItemDto(int Id, string Name, decimal Price, string Category, int? CategoryId, string? Description,
        bool IsVeg, bool IsAvailable, decimal TaxRate, int SortOrder, bool IsDeleted, int OutletId,
        List<MenuOptionDto> Variants, List<MenuOptionDto> AddOns, string? Includes = null, string SellUnit = "piece");

    public static class MenuOptions
    {
        public const int MaxVariants = 10;
        public const int MaxAddOns = 20;
        public const decimal MaxPrice = 100000m;

        public static async Task<MenuOptionsDto> Load(AppDbContext db, int menuItemId)
        {
            var all = await ForItems(db, new List<int> { menuItemId });
            return all.TryGetValue(menuItemId, out var found) ? found : new MenuOptionsDto(new(), new());
        }

        public static async Task<Dictionary<int, MenuOptionsDto>> ForItems(AppDbContext db, List<int> menuItemIds)
        {
            var variants = await db.ItemVariants.Where(v => menuItemIds.Contains(v.MenuItemId)).ToListAsync();
            var addOns = await db.ItemAddOns.Where(a => menuItemIds.Contains(a.MenuItemId)).ToListAsync();
            return menuItemIds.Distinct().ToDictionary(id => id, id => new MenuOptionsDto(
                variants.Where(v => v.MenuItemId == id).OrderBy(v => v.SortOrder).ThenBy(v => v.Id)
                    .Select(v => new MenuOptionDto(v.Id, v.Name, v.Price, v.IsAvailable)).ToList(),
                addOns.Where(a => a.MenuItemId == id).OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                    .Select(a => new MenuOptionDto(a.Id, a.Name, a.Price, a.IsAvailable)).ToList()));
        }

        // includes: "1 x Dal, 2 x Naan" for combo items (Combos.IncludesFor), shown under the name on the POS and QR menu.
        public static PosMenuItemDto ToPosDto(MenuItem m, Dictionary<int, MenuOptionsDto> options, Dictionary<int, string>? includes = null)
        {
            var o = options.TryGetValue(m.Id, out var found) ? found : new MenuOptionsDto(new(), new());
            return new PosMenuItemDto(m.Id, m.Name, m.Price, m.Category, m.CategoryId, m.Description, m.IsVeg, m.IsAvailable,
                m.TaxRate, m.SortOrder, m.IsDeleted, m.OutletId, o.Variants, o.AddOns,
                includes != null && includes.TryGetValue(m.Id, out var parts) ? parts : null, m.SellUnit);
        }

        public static Dictionary<string, string[]> Validate(MenuOptionsRequest req)
        {
            var errors = new Dictionary<string, string[]>();
            Check("variants", req.Variants ?? new(), MaxVariants, "sizes", errors);
            Check("addOns", req.AddOns ?? new(), MaxAddOns, "extras", errors);
            return errors;
        }

        private static void Check(string field, List<MenuOptionRequest> list, int max, string what, Dictionary<string, string[]> errors)
        {
            var problems = new List<string>();
            if (list.Count > max) problems.Add($"At most {max} {what}.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < list.Count; i++)
            {
                var name = list[i].Name?.Trim() ?? "";
                var row = i + 1;
                if (name == "") problems.Add($"Row {row}: enter a name.");
                else if (name.Length > 40) problems.Add($"Row {row}: keep the name under 40 characters.");
                else if (!names.Add(name)) problems.Add($"Row {row}: {name} is there twice.");
                if (list[i].Price < 0 || list[i].Price > MaxPrice || decimal.Round(list[i].Price, 2) != list[i].Price)
                    problems.Add($"Row {row}: enter a price between 0 and {MaxPrice:0} (at most 2 decimals).");
            }
            if (problems.Count > 0) errors[field] = problems.ToArray();
        }

        // Replaces the item's sizes and extras with the list sent: rows with a known Id are changed, rows without one are
        // added, missing rows are removed. Past orders keep the names and prices they were sold with.
        public static async Task Save(AppDbContext db, int menuItemId, MenuOptionsRequest req)
        {
            var variants = await db.ItemVariants.Where(v => v.MenuItemId == menuItemId).ToListAsync();
            var wantedVariants = req.Variants ?? new();
            db.ItemVariants.RemoveRange(variants.Where(v => !wantedVariants.Any(w => w.Id == v.Id)));
            for (var i = 0; i < wantedVariants.Count; i++)
            {
                var w = wantedVariants[i];
                var row = variants.FirstOrDefault(v => v.Id == w.Id);
                if (row == null) { row = new ItemVariant { MenuItemId = menuItemId }; db.ItemVariants.Add(row); }
                row.Name = w.Name!.Trim();
                row.Price = w.Price;
                row.IsAvailable = w.IsAvailable;
                row.SortOrder = i;
            }

            var addOns = await db.ItemAddOns.Where(a => a.MenuItemId == menuItemId).ToListAsync();
            var wantedAddOns = req.AddOns ?? new();
            db.ItemAddOns.RemoveRange(addOns.Where(a => !wantedAddOns.Any(w => w.Id == a.Id)));
            for (var i = 0; i < wantedAddOns.Count; i++)
            {
                var w = wantedAddOns[i];
                var row = addOns.FirstOrDefault(a => a.Id == w.Id);
                if (row == null) { row = new ItemAddOn { MenuItemId = menuItemId }; db.ItemAddOns.Add(row); }
                row.Name = w.Name!.Trim();
                row.Price = w.Price;
                row.IsAvailable = w.IsAvailable;
                row.SortOrder = i;
            }
            await db.SaveChangesAsync();
        }

        // The price and the names an order line is sold with. The server always prices from the menu; what a till sends
        // as a price is never used.
        public static OrderItem PriceLine(OrderItemRequest line, MenuItem item, List<ItemVariant> allVariants, List<ItemAddOn> allAddOns)
        {
            var name = item.Name;
            var price = item.Price;
            var variants = allVariants.Where(v => v.MenuItemId == item.Id).ToList();
            ItemVariant? variant = null;
            if (variants.Count > 0)
            {
                variant = variants.FirstOrDefault(v => v.Id == line.VariantId);
                if (variant == null) throw new ArgumentException($"Choose a size for {item.Name}.");
                if (!variant.IsAvailable) throw new ArgumentException($"{item.Name} ({variant.Name}) is sold out.");
                name = $"{item.Name} ({variant.Name})";
                price = variant.Price;
            }
            else if (line.VariantId != null)
            {
                throw new ArgumentException($"{item.Name} has no sizes.");
            }

            // Sold by weight or volume: the menu price is per kg or litre, the line is that price times the amount.
            decimal? measure = null;
            if (item.SellUnit is "kg" or "litre")
            {
                if (line.Measure is not decimal amount || amount <= 0 || amount > MaxMeasure)
                    throw new ArgumentException($"Enter how much {item.Name} (more than 0, at most {MaxMeasure} {item.SellUnit}).");
                measure = Math.Round(amount, 3, MidpointRounding.AwayFromZero);
                if (measure <= 0) throw new ArgumentException($"Enter how much {item.Name}.");
                name = $"{name} ({MeasureText(measure.Value, item.SellUnit)})";
                price = Math.Round(price * measure.Value, 2, MidpointRounding.AwayFromZero);
            }
            else if (line.Measure != null)
            {
                throw new ArgumentException($"{item.Name} is sold by the piece.");
            }

            var chosen = (line.AddOnIds ?? new List<int>()).Distinct().ToList();
            if (chosen.Count > MaxAddOns) throw new ArgumentException($"Too many extras for {item.Name}.");
            var extras = new List<ItemAddOn>();
            foreach (var id in chosen)
            {
                var extra = allAddOns.FirstOrDefault(a => a.Id == id && a.MenuItemId == item.Id);
                if (extra == null) throw new ArgumentException($"An extra chosen for {item.Name} is not on the menu.");
                if (!extra.IsAvailable) throw new ArgumentException($"{extra.Name} is not available now.");
                extras.Add(extra);
            }
            extras = extras.OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToList();

            return new OrderItem
            {
                MenuItemId = item.Id,
                ItemName = name,
                UnitPrice = price + extras.Sum(a => a.Price),
                Quantity = line.Quantity,
                Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(),
                VariantId = variant?.Id,
                Measure = measure,
                AddOnIds = extras.Count > 0 ? string.Join(",", extras.Select(a => a.Id)) : null,
                Options = extras.Count > 0 ? string.Join(", ", extras.Select(a => a.Name)) : null
            };
        }

        public const decimal MaxMeasure = 100m;

        // 0.25 kg -> "250 g", 1.5 kg -> "1.5 kg", 0.2 litre -> "200 ml", 2 litre -> "2 L".
        public static string MeasureText(decimal amount, string unit)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (amount < 1m) return (amount * 1000m).ToString("0.#", c) + (unit == "litre" ? " ml" : " g");
            return amount.ToString("0.###", c) + (unit == "litre" ? " L" : " kg");
        }

        // The name printed on the bill: the item (with its size) and its extras, within the bill's 100 characters.
        public static string BillName(OrderItem line)
        {
            var name = string.IsNullOrEmpty(line.Options) ? line.ItemName : $"{line.ItemName} + {line.Options}";
            return name.Length <= 100 ? name : name[..99] + "…";
        }

        public static List<int> AddOnIdList(string? text) =>
            string.IsNullOrEmpty(text) ? new List<int>()
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToList();

        public static WebApplication MapMenuOptionsEndpoints(this WebApplication app)
        {
            var admin = app.MapGroup("/api/admin/menu/items/{id:int}/options")
                .RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Menu admin");

            admin.MapGet("/", async (int id, AppDbContext db) =>
            {
                if (!await db.MenuItems.AnyAsync(m => m.Id == id && !m.IsDeleted)) return Results.NotFound();
                return Results.Ok(await Load(db, id));
            }).WithName("GetMenuItemOptions").WithSummary("Sizes and extras of a menu item");

            admin.MapPut("/", async (int id, MenuOptionsRequest req, AppDbContext db) =>
            {
                if (!await db.MenuItems.AnyAsync(m => m.Id == id && !m.IsDeleted)) return Results.NotFound();
                var errors = Validate(req);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                await Save(db, id, req);
                return Results.Ok(await Load(db, id));
            }).WithName("SaveMenuItemOptions").WithSummary("Replace the sizes and extras of a menu item");

            return app;
        }
    }
}
