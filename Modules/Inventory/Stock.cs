using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Inventory
{
    public enum StockMoveKind { Purchase, Used, Waste, Count }

    // Something the outlet keeps in stock (milk, paneer, oil, cups...). Quantity is kept per outlet in its own unit.
    public class StockItem : TenantEntity
    {
        public int Id { get; set; }
        [MaxLength(60)] public string Name { get; set; } = string.Empty;
        [MaxLength(10)] public string Unit { get; set; } = "kg";
        public decimal Quantity { get; set; }
        public decimal LowLevel { get; set; }
        public decimal? CostPerUnit { get; set; }
        public bool IsDeleted { get; set; }
    }

    // Every change to an item's quantity, so the owner can see who bought, used or threw away what.
    public class StockMove : TenantEntity
    {
        public int Id { get; set; }
        public int StockItemId { get; set; }
        public StockMoveKind Kind { get; set; }
        public decimal Change { get; set; }
        public decimal QuantityAfter { get; set; }
        public decimal? Cost { get; set; }
        [MaxLength(100)] public string? Note { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
        public string? By { get; set; }
    }

    public record StockItemRequest(string? Name, string? Unit, decimal? LowLevel, decimal? Quantity);
    public record StockMoveRequest(string? Kind, decimal? Quantity, decimal? Cost, string? Note);
    public record StockItemDto(int Id, string Name, string Unit, decimal Quantity, decimal LowLevel, decimal? CostPerUnit, bool IsLow);
    public record StockMoveDto(int Id, string Kind, decimal Change, decimal QuantityAfter, decimal? Cost, string? Note, DateTime At, string? By);

    public class StockException : Exception
    {
        public string Field { get; }
        public StockException(string field, string message) : base(message) => Field = field;
    }

    public class StockService
    {
        public static readonly string[] Units = { "kg", "g", "l", "ml", "pcs", "pack", "box", "dozen" };
        public const decimal MaxQuantity = 1_000_000m;
        public const decimal MaxCost = 1_000_000m;

        // One change at a time per outlet, so two quick entries never lose each other's quantity.
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

        private readonly AppDbContext _db;
        public StockService(AppDbContext db) => _db = db;

        private IQueryable<StockItem> Items => _db.StockItems.Where(s => s.OutletId == _db.CurrentOutletId && !s.IsDeleted);

        public static StockItemDto ToDto(StockItem s) =>
            new(s.Id, s.Name, s.Unit, s.Quantity, s.LowLevel, s.CostPerUnit, s.LowLevel > 0 && s.Quantity <= s.LowLevel);

        public static StockMoveDto ToDto(StockMove m) => new(m.Id, m.Kind.ToString(), m.Change, m.QuantityAfter, m.Cost, m.Note, m.At, m.By);

        private static decimal Qty(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

        public async Task<List<StockItemDto>> List() =>
            (await Items.OrderBy(s => s.Name).ToListAsync()).Select(ToDto).ToList();

        private async Task<(string Name, string Unit, decimal LowLevel)> Check(StockItemRequest r, int? id)
        {
            var name = r.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 60) throw new StockException("name", "Enter a name of up to 60 characters.");
            var unit = Units.FirstOrDefault(u => string.Equals(u, r.Unit?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new StockException("unit", "Choose a unit from the list.");
            var low = Qty(r.LowLevel ?? 0);
            if (low < 0 || low > MaxQuantity) throw new StockException("lowLevel", "Enter a low level from 0 up to 10,00,000.");
            var lower = name.ToLower();
            if (await Items.AnyAsync(s => s.Id != id && s.Name.ToLower() == lower))
                throw new StockException("name", "An item with this name is already in stock.");
            return (name, unit, low);
        }

        public async Task<StockItemDto> Add(StockItemRequest r)
        {
            var (name, unit, low) = await Check(r, null);
            var opening = Qty(r.Quantity ?? 0);
            if (opening < 0 || opening > MaxQuantity) throw new StockException("quantity", "Enter a quantity from 0 up to 10,00,000.");
            var item = new StockItem { Name = name, Unit = unit, LowLevel = low, Quantity = opening };
            _db.StockItems.Add(item);
            await _db.SaveChangesAsync();
            if (opening > 0)
            {
                _db.StockMoves.Add(new StockMove { StockItemId = item.Id, Kind = StockMoveKind.Count, Change = opening, QuantityAfter = opening, Note = "Opening stock", By = _db.CurrentUserId });
                await _db.SaveChangesAsync();
            }
            return ToDto(item);
        }

        public async Task<StockItemDto?> Update(int id, StockItemRequest r)
        {
            var item = await Items.FirstOrDefaultAsync(s => s.Id == id);
            if (item == null) return null;
            var (name, unit, low) = await Check(r, id);
            item.Name = name;
            item.Unit = unit;
            item.LowLevel = low;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return ToDto(item);
        }

        // Removed items leave the list; their history stays.
        public async Task<bool> Remove(int id)
        {
            var item = await Items.FirstOrDefaultAsync(s => s.Id == id);
            if (item == null) return false;
            item.IsDeleted = true;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<StockItemDto?> Move(int id, StockMoveRequest r)
        {
            if (!Enum.TryParse<StockMoveKind>(r.Kind, true, out var kind) || int.TryParse(r.Kind, out _))
                throw new StockException("kind", "Choose purchase, used, waste or count.");
            var qty = Qty(r.Quantity ?? -1);
            if (kind == StockMoveKind.Count ? qty < 0 : qty <= 0) throw new StockException("quantity", kind == StockMoveKind.Count ? "Enter the quantity you counted." : "Enter a quantity above 0.");
            if (qty > MaxQuantity) throw new StockException("quantity", "Enter a quantity up to 10,00,000.");
            decimal? cost = r.Cost is { } c ? Math.Round(c, 2, MidpointRounding.AwayFromZero) : null;
            if (cost is < 0 or > MaxCost) throw new StockException("cost", "Enter a cost from 0 up to 10,00,000.");
            if (cost != null && kind != StockMoveKind.Purchase) cost = null;
            var note = r.Note?.Trim();
            if (note is { Length: > 100 }) throw new StockException("note", "The note can be at most 100 characters.");

            var gate = Locks.GetOrAdd(_db.CurrentOutletId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var item = await Items.FirstOrDefaultAsync(s => s.Id == id);
                if (item == null) return null;
                var change = kind switch
                {
                    StockMoveKind.Purchase => qty,
                    StockMoveKind.Count => qty - item.Quantity,
                    _ => -qty
                };
                var after = item.Quantity + change;
                if (after < 0) throw new StockException("quantity", $"Only {item.Quantity:0.###} {item.Unit} in stock.");
                item.Quantity = after;
                if (kind == StockMoveKind.Purchase && cost is > 0) item.CostPerUnit = Math.Round(cost.Value / qty, 2, MidpointRounding.AwayFromZero);
                item.UpdatedAt = DateTime.UtcNow;
                _db.StockMoves.Add(new StockMove
                {
                    StockItemId = item.Id, Kind = kind, Change = change, QuantityAfter = after, Cost = cost,
                    Note = string.IsNullOrEmpty(note) ? null : note, At = DateTime.UtcNow, By = _db.CurrentUserId
                });
                await _db.SaveChangesAsync();
                return ToDto(item);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<List<StockMoveDto>?> Moves(int id, int take)
        {
            if (!await Items.AnyAsync(s => s.Id == id)) return null;
            return (await _db.StockMoves.Where(m => m.StockItemId == id && m.OutletId == _db.CurrentOutletId)
                .OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Take(Math.Clamp(take, 1, 200)).ToListAsync())
                .Select(ToDto).ToList();
        }
    }

    public static class StockEndpoints
    {
        private static readonly string[] Managers = { "manager", "admin" };
        private static readonly string[] Staff = { "cashier", "kitchen", "manager", "admin" };

        private static IResult Invalid(StockException e) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { [e.Field] = new[] { e.Message } });

        public static WebApplication MapStockEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/stock").WithTags("Stock");

            group.MapGet("/", async (StockService stock) => Results.Ok(await stock.List()))
                .RequireAuthorization(p => p.RequireRole(Staff))
                .WithName("GetStock").WithSummary("Stock items of this outlet, with the ones at or under their low level marked");

            group.MapPost("/", async (StockItemRequest request, StockService stock) =>
            {
                try
                {
                    var item = await stock.Add(request);
                    return Results.Created($"/api/stock/{item.Id}", item);
                }
                catch (StockException e) { return Invalid(e); }
            })
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithName("AddStockItem").WithSummary("Add a stock item, optionally with its opening quantity");

            group.MapPut("/{id:int}", async (int id, StockItemRequest request, StockService stock) =>
            {
                try
                {
                    var item = await stock.Update(id, request);
                    return item == null ? Results.NotFound() : Results.Ok(item);
                }
                catch (StockException e) { return Invalid(e); }
            })
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithName("UpdateStockItem").WithSummary("Rename a stock item or change its unit or low level");

            group.MapDelete("/{id:int}", async (int id, StockService stock) =>
                await stock.Remove(id) ? Results.NoContent() : Results.NotFound())
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithName("RemoveStockItem").WithSummary("Take an item off the stock list (its history stays)");

            group.MapPost("/{id:int}/moves", async (int id, StockMoveRequest request, StockService stock) =>
            {
                try
                {
                    var item = await stock.Move(id, request);
                    return item == null ? Results.NotFound() : Results.Ok(item);
                }
                catch (StockException e) { return Invalid(e); }
            })
                .RequireAuthorization(p => p.RequireRole(Staff))
                .WithName("AddStockMove").WithSummary("Record a purchase, use, waste or a stock count");

            group.MapGet("/{id:int}/moves", async (int id, int? take, StockService stock) =>
            {
                var list = await stock.Moves(id, take ?? 50);
                return list == null ? Results.NotFound() : Results.Ok(list);
            })
                .RequireAuthorization(p => p.RequireRole(Staff))
                .WithName("GetStockMoves").WithSummary("Latest changes of one stock item");

            return app;
        }
    }
}
