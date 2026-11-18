using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Menu;
using POSSystem.Models;
using POSSystem.Platform;

namespace POSSystem.Orders
{
    public record QrMenuDto(string RestaurantName, string OutletName, string TableNumber, bool Ordering, IReadOnlyList<PosMenuItemDto> Items);
    public record QrOrderRequest(List<OrderItemRequest>? Items, Guid? ClientOrderId);
    public record QrOrderResult(int TokenNumber, int Round);
    public record QrCallRequest(string? Kind);
    public record QrTableDto(int Id, string TableNumber, string Token);
    public record QrSettingDto(bool Enabled);

    // QR table ordering (v1.8b): each table has a secret token in its QR code. A guest opens the menu without signing in
    // and orders; the order goes straight to the kitchen and onto the table's running tab. Off until the owner turns it on.
    public static class QrOrdering
    {
        public const string RateLimit = RateLimits.Qr;
        public const int MaxQuantityPerOrder = 30;
        public const string TakenBy = "QR (guest)";

        public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // The table of a token, with the restaurant's data opened for this request. Null = unknown token, table off or restaurant disabled.
        private static async Task<RestaurantTable?> Open(AppDbContext db, TenantStatus tenants, string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 40) return null;
            var table = await db.RestaurantTables.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.OrderToken == token && t.IsActive);
            if (table == null || await tenants.IsDisabledAsync(db, table.TenantId)) return null;
            db.CurrentTenantId = table.TenantId;
            db.CurrentOutletId = table.OutletId;
            return table;
        }

        public static WebApplication MapQrOrderingEndpoints(this WebApplication app)
        {
            var guest = app.MapGroup("/api/qr/{token}").AllowAnonymous().RequireRateLimiting(RateLimit).WithTags("QR ordering");

            guest.MapGet("/", async (string token, AppDbContext db, TenantStatus tenants, IMenuItemService menu) =>
            {
                var table = await Open(db, tenants, token);
                if (table == null) return Results.NotFound();
                var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == table.OutletId);
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == table.TenantId);
                var closed = await MenuHours.ClosedCategoryIds(db);
                var items = (await menu.GetMenuItems()).Where(i => i.CategoryId == null || !closed.Contains(i.CategoryId.Value)).ToList();
                var options = await MenuOptions.ForItems(db, items.Select(i => i.Id).ToList());
                return Results.Ok(new QrMenuDto(tenant?.Name ?? string.Empty, outlet.Name, table.TableNumber,
                    outlet.GetSettings().QrOrderingEnabled, items.Select(i => MenuOptions.ToPosDto(i, options)).ToList()));
            }).WithName("GetQrMenu").WithSummary("Menu for a guest who scanned a table's QR code");

            guest.MapPost("/order", async (string token, QrOrderRequest request, AppDbContext db, TenantStatus tenants, IOrderService orders) =>
            {
                var table = await Open(db, tenants, token);
                if (table == null) return Results.NotFound();
                var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == table.OutletId);
                if (!outlet.GetSettings().QrOrderingEnabled)
                    return Results.Json(new { message = "Ordering by QR is off here. Please call the staff.", code = "qr-off" }, statusCode: 403);
                var items = request.Items ?? new List<OrderItemRequest>();
                if (items.Sum(i => Math.Max(i.Quantity, 0)) > MaxQuantityPerOrder)
                    return Results.BadRequest(new { message = $"At most {MaxQuantityPerOrder} items at a time. Please ask the staff for big orders." });

                try
                {
                    // Two tries: if staff change the table's order at the same moment, the second try uses the new version.
                    for (var attempt = 0; ; attempt++)
                    {
                        var open = await db.KitchenOrders.AsNoTracking()
                            .Where(o => o.TableId == table.Id && TableBoard.Open.Contains(o.Status))
                            .OrderByDescending(o => o.Id).Select(o => new { o.Id, o.Version }).FirstOrDefaultAsync();
                        try
                        {
                            KitchenOrder order;
                            if (open == null)
                            {
                                order = await orders.CreateOrder(new OrderRequest
                                {
                                    OrderType = OrderType.DineIn, Table = table.Id, Items = items,
                                    ClientOrderId = request.ClientOrderId, TakenBy = TakenBy
                                });
                            }
                            else
                            {
                                order = await orders.AddItems(open.Id, items, open.Version);
                            }
                            return Results.Ok(new QrOrderResult(order.TokenNumber, order.Items.Count == 0 ? 1 : order.Items.Max(i => i.Round)));
                        }
                        catch (OrderConflictException) when (attempt == 0)
                        {
                            db.ChangeTracker.Clear();
                        }
                    }
                }
                catch (POSSystem.Billing.BilledOrderException)
                {
                    return Results.Conflict(new { message = "The bill for this table is ready. Please ask the staff to add more.", code = "billed" });
                }
                catch (OrderConflictException)
                {
                    return Results.Conflict(new { message = "Please try again in a moment.", code = "busy" });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            }).WithName("PlaceQrOrder").WithSummary("A guest orders from the table: a new order, or the next round of the table's open order");

            guest.MapPost("/call", async (string token, QrCallRequest request, AppDbContext db, TenantStatus tenants, IHubContext<OrderHub> hub) =>
            {
                var table = await Open(db, tenants, token);
                if (table == null) return Results.NotFound();
                var kind = request.Kind?.Trim().ToLowerInvariant();
                if (kind is not ("waiter" or "bill")) return Results.BadRequest(new { message = "Choose waiter or bill." });
                await hub.Clients.Group($"tenant-{table.TenantId}").SendAsync("TableCall",
                    new { tableId = table.Id, tableNumber = table.TableNumber, kind, at = DateTime.UtcNow });
                return Results.Ok(new { message = kind == "bill" ? "The staff will bring your bill." : "A waiter is on the way." });
            }).WithName("CallFromTable").WithSummary("A guest calls a waiter or asks for the bill");

            // Owner and manager: QR tokens of the tables (made on first use), a new token for one table, and the on/off switch.
            var admin = app.MapGroup("/api/admin/tables").RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Tables");

            admin.MapGet("/qr", async (AppDbContext db) =>
            {
                var tables = await db.RestaurantTables.Where(t => t.OutletId == db.CurrentOutletId && t.IsActive)
                    .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
                foreach (var t in tables.Where(t => string.IsNullOrEmpty(t.OrderToken))) t.OrderToken = NewToken();
                await db.SaveChangesAsync();
                return Results.Ok(tables.Select(t => new QrTableDto(t.Id, t.TableNumber, t.OrderToken!)).ToList());
            }).WithName("GetTableQrCodes").WithSummary("QR tokens of the tables in use, for the printable QR sheet");

            admin.MapPost("/{id:int}/qr", async (int id, AppDbContext db) =>
            {
                var table = await db.RestaurantTables.FirstOrDefaultAsync(t => t.Id == id && t.OutletId == db.CurrentOutletId);
                if (table == null) return Results.NotFound();
                table.OrderToken = NewToken();
                await db.SaveChangesAsync();
                return Results.Ok(new QrTableDto(table.Id, table.TableNumber, table.OrderToken));
            }).WithName("RenewTableQr").WithSummary("New QR code for a table; the old printed code stops working");

            // The QR image of the guest link. The address comes from the owner's screen (the POS address guests can reach).
            admin.MapGet("/{id:int}/qr.svg", async (int id, string? origin, AppDbContext db) =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var site) || (site.Scheme != Uri.UriSchemeHttp && site.Scheme != Uri.UriSchemeHttps))
                    return Results.BadRequest(new { message = "Give the address of this POS, like https://pos.example.com." });
                var table = await db.RestaurantTables.FirstOrDefaultAsync(t => t.Id == id && t.OutletId == db.CurrentOutletId);
                if (table == null) return Results.NotFound();
                if (string.IsNullOrEmpty(table.OrderToken))
                {
                    table.OrderToken = NewToken();
                    await db.SaveChangesAsync();
                }
                var link = $"{site.GetLeftPart(UriPartial.Authority)}/t/{table.OrderToken}";
                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
                return Results.Content(new QRCoder.SvgQRCode(data).GetGraphic(8), "image/svg+xml");
            }).WithName("GetTableQrImage").WithSummary("QR code (SVG) a guest scans at the table");

            admin.MapGet("/qr-setting", async (AppDbContext db) =>
            {
                var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
                return outlet == null ? Results.NotFound() : Results.Ok(new QrSettingDto(outlet.GetSettings().QrOrderingEnabled));
            }).WithName("GetQrSetting").WithSummary("Is QR table ordering on?");

            admin.MapPut("/qr-setting", async (QrSettingDto request, AppDbContext db) =>
            {
                var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
                if (outlet == null) return Results.NotFound();
                var settings = outlet.GetSettings();
                settings.QrOrderingEnabled = request.Enabled;
                outlet.Settings = settings.ToJson();
                await db.SaveChangesAsync();
                return Results.Ok(new QrSettingDto(settings.QrOrderingEnabled));
            }).WithName("SetQrSetting").WithSummary("Turn QR table ordering on or off");

            return app;
        }
    }
}
