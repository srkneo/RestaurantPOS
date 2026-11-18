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
    // A combo is a normal menu item (own price and GST rate, one line on the bill) that lists the items it includes,
    // so the kitchen screen and the KOT show each part to cook.
    public class ComboPart : TenantEntity
    {
        public int Id { get; set; }
        public int ComboItemId { get; set; }
        public int PartItemId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public record ComboPartDto(int PartItemId, string Name, int Quantity);
    public record ComboPartRequest(int PartItemId, int Quantity);
    public record ComboRequest(List<ComboPartRequest>? Parts);

    public static class Combos
    {
        public const int MaxParts = 10;
        public const int MaxQuantity = 20;

        // "1 x Burger, 1 x Fries, 1 x Cola" per combo item, for the order lines.
        public static async Task<Dictionary<int, string>> IncludesFor(AppDbContext db, ICollection<int> itemIds)
        {
            var parts = await db.ComboParts.Where(p => itemIds.Contains(p.ComboItemId)).ToListAsync();
            if (parts.Count == 0) return new Dictionary<int, string>();
            var partIds = parts.Select(p => p.PartItemId).Distinct().ToList();
            var names = await db.MenuItems.Where(m => partIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
            return parts.GroupBy(p => p.ComboItemId).ToDictionary(g => g.Key, g =>
            {
                var text = string.Join(", ", g.OrderBy(p => p.Id)
                    .Select(p => $"{p.Quantity} x {(names.TryGetValue(p.PartItemId, out var name) ? name : "?")}"));
                return text.Length > 300 ? text[..300] : text;
            });
        }

        public static WebApplication MapComboEndpoints(this WebApplication app)
        {
            var admin = app.MapGroup("/api/admin/menu/items/{id:int}/combo")
                .RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Menu admin");

            admin.MapGet("/", async (int id, AppDbContext db) =>
            {
                if (!await db.MenuItems.AnyAsync(m => m.Id == id && !m.IsDeleted)) return Results.NotFound();
                return Results.Ok(await Load(db, id));
            }).WithName("GetCombo").WithSummary("Items a combo includes");

            admin.MapPut("/", async (int id, ComboRequest req, AppDbContext db) =>
            {
                if (!await db.MenuItems.AnyAsync(m => m.Id == id && !m.IsDeleted)) return Results.NotFound();
                var parts = req.Parts ?? new List<ComboPartRequest>();
                var error = await Validate(db, id, parts);
                if (error != null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["parts"] = new[] { error } });

                db.ComboParts.RemoveRange(await db.ComboParts.Where(p => p.ComboItemId == id).ToListAsync());
                foreach (var part in parts)
                    db.ComboParts.Add(new ComboPart { ComboItemId = id, PartItemId = part.PartItemId, Quantity = part.Quantity });
                await db.SaveChangesAsync();
                return Results.Ok(await Load(db, id));
            }).WithName("SaveCombo").WithSummary("Set the items a combo includes (an empty list makes it a normal item again)");

            return app;
        }

        private static async Task<List<ComboPartDto>> Load(AppDbContext db, int id)
        {
            var parts = await db.ComboParts.Where(p => p.ComboItemId == id).OrderBy(p => p.Id).ToListAsync();
            var partIds = parts.Select(p => p.PartItemId).ToList();
            var names = await db.MenuItems.Where(m => partIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
            return parts.Select(p => new ComboPartDto(p.PartItemId, names.TryGetValue(p.PartItemId, out var n) ? n : "?", p.Quantity)).ToList();
        }

        private static async Task<string?> Validate(AppDbContext db, int id, List<ComboPartRequest> parts)
        {
            if (parts.Count > MaxParts) return $"A combo can include at most {MaxParts} items.";
            if (parts.Select(p => p.PartItemId).Distinct().Count() != parts.Count) return "Each item can be in the combo once; raise its quantity instead.";
            if (parts.Any(p => p.Quantity < 1 || p.Quantity > MaxQuantity)) return $"Quantities are 1 to {MaxQuantity}.";
            if (parts.Any(p => p.PartItemId == id)) return "A combo cannot include itself.";
            var ids = parts.Select(p => p.PartItemId).ToList();
            var found = await db.MenuItems.Where(m => ids.Contains(m.Id) && !m.IsDeleted && m.OutletId == db.CurrentOutletId).Select(m => m.Id).ToListAsync();
            if (found.Count != ids.Count) return "Choose items from this menu.";
            if (await db.ComboParts.AnyAsync(p => ids.Contains(p.ComboItemId))) return "A combo cannot include another combo.";
            if (parts.Count > 0 && await db.ComboParts.AnyAsync(p => p.PartItemId == id)) return "This item is part of another combo, so it cannot be a combo itself.";
            return null;
        }
    }
}
