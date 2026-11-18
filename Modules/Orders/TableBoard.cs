using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POSSystem.Billing;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Orders
{
    // One table on the POS: Free, Occupied (an open order) or Billed (the bill is printed, not paid yet).
    public record TableStatusDto(int Id, string TableNumber, string Status, int? OrderId, int? TokenNumber, int? OrderVersion, DateTime? Since);

    public record MoveTableRequest(int TableId, int Version);

    public static class TableBoard
    {
        public static readonly OrderStatus[] Open = { OrderStatus.Received, OrderStatus.Preparing, OrderStatus.Ready };

        public static async Task<List<TableStatusDto>> Load(AppDbContext db)
        {
            var tables = await db.RestaurantTables.Where(t => t.IsActive && t.OutletId == db.CurrentOutletId)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => new { t.Id, t.TableNumber }).ToListAsync();
            var ids = tables.Select(t => t.Id).ToList();
            var open = await db.KitchenOrders
                .Where(o => o.TableId != null && ids.Contains(o.TableId.Value) && Open.Contains(o.Status))
                .Select(o => new { o.Id, TableId = o.TableId!.Value, o.TokenNumber, o.Version, o.CreatedAt }).ToListAsync();
            var orderIds = open.Select(o => o.Id).ToList();
            var billed = await db.Bills.Where(b => orderIds.Contains(b.KitchenOrderId) && b.Status == BillStatus.Unpaid)
                .Select(b => b.KitchenOrderId).ToListAsync();
            return tables.Select(t =>
            {
                // A table normally has one open order; if an older screen made two, the newest one is the running tab.
                var order = open.Where(o => o.TableId == t.Id).OrderByDescending(o => o.Id).FirstOrDefault();
                var status = order == null ? "Free" : billed.Contains(order.Id) ? "Billed" : "Occupied";
                return new TableStatusDto(t.Id, t.TableNumber, status, order?.Id, order?.TokenNumber, order?.Version, order?.CreatedAt);
            }).ToList();
        }
    }
}
