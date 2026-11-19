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
        // Served still holds the table: the guests are eating until the bill is paid.
        public static readonly OrderStatus[] Open = { OrderStatus.Received, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Served };

        // Only today's open orders hold a table. An order left open on an earlier business day is not the
        // table's running tab: new orders (POS or QR) must start fresh so they show on today's kitchen screen.
        public static DateOnly Today(Outlet? outlet) =>
            BusinessClock.BusinessDate(DateTime.UtcNow, outlet?.TimeZone, outlet?.BusinessDayStartHour ?? BusinessClock.DefaultDayStartHour);

        public static async Task<List<TableStatusDto>> Load(AppDbContext db)
        {
            var tables = await db.RestaurantTables.Where(t => t.IsActive && t.OutletId == db.CurrentOutletId)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => new { t.Id, t.TableNumber }).ToListAsync();
            var ids = tables.Select(t => t.Id).ToList();
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId);
            var today = Today(outlet);
            // A table is held by today's open orders, and by paid (completed) orders until staff tap Customer left.
            var held = await db.KitchenOrders
                .Where(o => o.TableId != null && ids.Contains(o.TableId.Value) && o.BusinessDate == today && o.TableFreedAt == null
                    && (Open.Contains(o.Status) || o.Status == OrderStatus.Completed))
                .Select(o => new { o.Id, TableId = o.TableId!.Value, o.TokenNumber, o.Version, o.CreatedAt, o.CompletedAt, o.Status }).ToListAsync();
            var open = held.Where(o => o.Status != OrderStatus.Completed).ToList();
            var orderIds = open.Select(o => o.Id).ToList();
            var billed = await db.Bills.Where(b => orderIds.Contains(b.KitchenOrderId) && b.Status == BillStatus.Unpaid)
                .Select(b => b.KitchenOrderId).ToListAsync();
            return tables.Select(t =>
            {
                // A table normally has one open order; if an older screen made two, the newest one is the running tab.
                var order = open.Where(o => o.TableId == t.Id).OrderByDescending(o => o.Id).FirstOrDefault();
                if (order != null)
                    return new TableStatusDto(t.Id, t.TableNumber, billed.Contains(order.Id) ? "Billed" : "Occupied", order.Id, order.TokenNumber, order.Version, order.CreatedAt);
                // Paid, guests still at the table: no running tab (a new order starts a new token), Since = paid time.
                var paid = held.Where(o => o.TableId == t.Id && o.Status == OrderStatus.Completed).OrderByDescending(o => o.Id).FirstOrDefault();
                return paid == null
                    ? new TableStatusDto(t.Id, t.TableNumber, "Free", null, null, null, null)
                    : new TableStatusDto(t.Id, t.TableNumber, "Paid", null, paid.TokenNumber, null, paid.CompletedAt ?? paid.CreatedAt);
            }).ToList();
        }

        // Customer left: the table is free again. Refused while an order on it is still open (not paid or not done).
        public static async Task<string?> Clear(AppDbContext db, int tableId)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId);
            var today = Today(outlet);
            var orders = await db.KitchenOrders
                .Where(o => o.TableId == tableId && o.BusinessDate == today && o.TableFreedAt == null && o.Status != OrderStatus.Cancelled)
                .ToListAsync();
            var open = orders.FirstOrDefault(o => o.Status != OrderStatus.Completed);
            if (open != null) return $"Token {open.TokenNumber} on this table is still open. Take payment (or mark the food delivered) first.";
            var now = DateTime.UtcNow;
            foreach (var o in orders) o.TableFreedAt = now;
            await db.SaveChangesAsync();
            return null;
        }
    }
}
