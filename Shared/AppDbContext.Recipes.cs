using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POSSystem.Inventory;
using POSSystem.Models;

namespace POSSystem.Data
{
    // Recipes (v2.1): every save that completes an order takes its recipe ingredients from the outlet's stock, once.
    // Stock may go below zero here (sales are never blocked); the next stock count corrects it.
    public partial class AppDbContext
    {
        public DbSet<RecipeLine> RecipeLines { get; set; }

        private void ConfigureRecipes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RecipeLine>(b =>
            {
                b.Property(x => x.Quantity).HasPrecision(12, 3);
                b.HasOne<StockItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.TenantId, x.MenuItemId });
                b.HasQueryFilter(x => x.TenantId == CurrentTenantId);
            });
        }

        private record OrderLineUse(int OrderId, int MenuItemId, int Quantity);

        private List<KitchenOrder> NewlyCompletedOrders() =>
            ChangeTracker.Entries<KitchenOrder>()
                .Where(e => e.State == EntityState.Modified
                    && e.Property(o => o.Status).IsModified
                    && e.Entity.Status == OrderStatus.Completed
                    && e.Property(o => o.Status).OriginalValue != OrderStatus.Completed)
                .Select(e => e.Entity)
                .ToList();

        private void UseStockForCompletedOrders()
        {
            var orders = NewlyCompletedOrders();
            if (orders.Count == 0) return;
            var ids = orders.Select(o => o.Id).ToList();
            var lines = OrderItems.IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.KitchenOrderId))
                .Select(i => new OrderLineUse(i.KitchenOrderId, i.MenuItemId, i.Quantity)).ToList();
            var menuIds = lines.Select(l => l.MenuItemId).Distinct().ToList();
            var recipes = RecipeLines.IgnoreQueryFilters().AsNoTracking().Where(r => menuIds.Contains(r.MenuItemId)).ToList();
            if (recipes.Count == 0) return;
            var stockIds = recipes.Select(r => r.StockItemId).Distinct().ToList();
            var stock = StockItems.IgnoreQueryFilters().Where(s => stockIds.Contains(s.Id) && !s.IsDeleted).ToList();
            ApplyStockUse(orders, lines, recipes, stock);
        }

        private async Task UseStockForCompletedOrdersAsync(CancellationToken ct)
        {
            var orders = NewlyCompletedOrders();
            if (orders.Count == 0) return;
            var ids = orders.Select(o => o.Id).ToList();
            var lines = await OrderItems.IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.KitchenOrderId))
                .Select(i => new OrderLineUse(i.KitchenOrderId, i.MenuItemId, i.Quantity)).ToListAsync(ct);
            var menuIds = lines.Select(l => l.MenuItemId).Distinct().ToList();
            var recipes = await RecipeLines.IgnoreQueryFilters().AsNoTracking().Where(r => menuIds.Contains(r.MenuItemId)).ToListAsync(ct);
            if (recipes.Count == 0) return;
            var stockIds = recipes.Select(r => r.StockItemId).Distinct().ToList();
            var stock = await StockItems.IgnoreQueryFilters().Where(s => stockIds.Contains(s.Id) && !s.IsDeleted).ToListAsync(ct);
            ApplyStockUse(orders, lines, recipes, stock);
        }

        private void ApplyStockUse(List<KitchenOrder> orders, List<OrderLineUse> lines, List<RecipeLine> recipes, List<StockItem> stock)
        {
            var now = DateTime.UtcNow;
            foreach (var order in orders)
            {
                var use = new Dictionary<int, decimal>();
                foreach (var line in lines.Where(l => l.OrderId == order.Id))
                    foreach (var r in recipes.Where(r => r.MenuItemId == line.MenuItemId && r.TenantId == order.TenantId))
                        use[r.StockItemId] = use.GetValueOrDefault(r.StockItemId) + r.Quantity * line.Quantity;

                foreach (var (stockId, amount) in use)
                {
                    var item = stock.FirstOrDefault(s => s.Id == stockId && s.TenantId == order.TenantId && s.OutletId == order.OutletId);
                    if (item == null || amount <= 0) continue;
                    var change = -Math.Round(amount, 3, MidpointRounding.AwayFromZero);
                    item.Quantity += change;
                    item.UpdatedAt = now;
                    StockMoves.Add(new StockMove
                    {
                        TenantId = order.TenantId, OutletId = order.OutletId, StockItemId = item.Id, Kind = StockMoveKind.Used,
                        Change = change, QuantityAfter = item.Quantity, Note = $"Token {order.TokenNumber}", At = now, By = CurrentUserId
                    });
                }
            }
        }
    }
}
