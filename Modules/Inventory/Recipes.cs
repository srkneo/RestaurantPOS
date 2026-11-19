using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Inventory
{
    // How much of a stock item one portion of a menu item uses (Masala Chai: 0.15 l milk, 0.01 kg sugar).
    // When an order is completed, its portions are taken from the outlet's stock as "Used" with the token number.
    public class RecipeLine : TenantEntity
    {
        public int Id { get; set; }
        public int MenuItemId { get; set; }
        public int StockItemId { get; set; }
        public decimal Quantity { get; set; }
    }

    public record RecipeLineRequest(int StockItemId, decimal Quantity);
    public record RecipeRequest(List<RecipeLineRequest>? Lines);
    public record RecipeLineDto(int StockItemId, string Name, string Unit, decimal Quantity);

    public static class RecipeEndpoints
    {
        public const int MaxLines = 30;
        public const decimal MaxQuantity = 1000m;
        private static readonly string[] Managers = { "manager", "admin" };

        private static IResult Invalid(string message) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = new[] { message } });

        private static async Task<List<RecipeLineDto>> Load(AppDbContext db, int menuItemId) =>
            await (from r in db.RecipeLines
                   where r.MenuItemId == menuItemId
                   join s in db.StockItems on r.StockItemId equals s.Id
                   orderby s.Name
                   select new RecipeLineDto(s.Id, s.Name, s.Unit, r.Quantity)).ToListAsync();

        public static WebApplication MapRecipeEndpoints(this WebApplication app)
        {
            app.MapGet("/api/menu/{id:int}/recipe", async (int id, AppDbContext db) =>
            {
                if (!await db.MenuItems.AnyAsync(m => m.Id == id && !m.IsDeleted)) return Results.NotFound();
                return Results.Ok(await Load(db, id));
            })
                .RequireAuthorization(p => p.RequireRole(Managers)).WithTags("Stock")
                .WithName("GetRecipe").WithSummary("Stock one portion of this menu item uses");

            app.MapPut("/api/menu/{id:int}/recipe", async (int id, RecipeRequest request, AppDbContext db) =>
            {
                if (!await db.MenuItems.AnyAsync(m => m.Id == id && !m.IsDeleted)) return Results.NotFound();
                var lines = request.Lines ?? new List<RecipeLineRequest>();
                if (lines.Count > MaxLines) return Invalid($"A recipe can have at most {MaxLines} stock items.");
                if (lines.Select(l => l.StockItemId).Distinct().Count() != lines.Count) return Invalid("Each stock item can be in the recipe only once.");
                if (lines.Any(l => l.Quantity <= 0 || l.Quantity > MaxQuantity)) return Invalid("Enter a quantity above 0, up to 1,000, for each stock item.");
                var ids = lines.Select(l => l.StockItemId).ToList();
                var found = await db.StockItems.CountAsync(s => ids.Contains(s.Id) && s.OutletId == db.CurrentOutletId && !s.IsDeleted);
                if (found != ids.Count) return Invalid("Choose stock items from this outlet's stock list.");

                db.RecipeLines.RemoveRange(await db.RecipeLines.Where(r => r.MenuItemId == id).ToListAsync());
                db.RecipeLines.AddRange(lines.Select(l => new RecipeLine
                {
                    MenuItemId = id, StockItemId = l.StockItemId, Quantity = Math.Round(l.Quantity, 3, MidpointRounding.AwayFromZero)
                }));
                await db.SaveChangesAsync();
                return Results.Ok(await Load(db, id));
            })
                .RequireAuthorization(p => p.RequireRole(Managers)).WithTags("Stock")
                .WithName("SaveRecipe").WithSummary("Replace the recipe of a menu item (an empty list removes it)");

            return app;
        }
    }
}
