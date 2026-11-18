using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Billing;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // Platform owner: fill an empty restaurant with a sample Gujarati menu and three orders of today (two paid, one billed),
    // so a demo shows the POS, kitchen, bills and reports with real numbers. Never touches a restaurant that has a menu or orders.
    public static class DemoData
    {
        private static readonly (string Category, string Name, decimal Price, bool Veg)[] Menu =
        {
            ("Thali", "Gujarati Thali", 220m, true),
            ("Thali", "Kathiyawadi Thali", 260m, true),
            ("Snacks", "Khaman Dhokla", 60m, true),
            ("Snacks", "Samosa", 25m, true),
            ("Snacks", "Pav Bhaji", 120m, true),
            ("Drinks", "Masala Tea", 20m, true),
            ("Drinks", "Chaas", 30m, true),
            ("Drinks", "Sweet Lassi", 50m, true),
            ("Sweets", "Gulab Jamun", 40m, true),
        };

        public static WebApplication MapDemoDataEndpoints(this WebApplication app)
        {
            app.MapPost("/api/platform/tenants/{id:int}/demo", async (int id, AppDbContext db, IOrderService orders, BillService bills) =>
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
                var outlet = await db.Outlets.Where(o => o.TenantId == id).OrderBy(o => o.Id).FirstOrDefaultAsync();
                if (tenant == null || outlet == null) return Results.NotFound();
                if (await db.KitchenOrders.IgnoreQueryFilters().AnyAsync(o => o.TenantId == id)
                    || await db.MenuItems.IgnoreQueryFilters().AnyAsync(m => m.TenantId == id))
                {
                    return Results.Conflict(new { message = "This restaurant already has a menu or orders. Demo data is only for an empty restaurant." });
                }

                var tenantBefore = db.CurrentTenantId;
                var outletBefore = db.CurrentOutletId;
                db.CurrentTenantId = id;
                db.CurrentOutletId = outlet.Id;
                try
                {
                    var categories = Menu.Select(m => m.Category).Distinct()
                        .Select((name, i) => new MenuCategory { Name = name, SortOrder = i + 1 }).ToList();
                    db.MenuCategories.AddRange(categories);
                    await db.SaveChangesAsync();
                    var items = Menu.Select((m, i) =>
                    {
                        var category = categories.First(c => c.Name == m.Category);
                        return new MenuItem
                        {
                            Name = m.Name, Price = m.Price, Category = category.Name, CategoryId = category.Id, IsVeg = m.Veg,
                            IsAvailable = true, TaxRate = 5m, SortOrder = i + 1
                        };
                    }).ToList();
                    db.MenuItems.AddRange(items);

                    var tables = await db.RestaurantTables.Where(t => t.OutletId == outlet.Id && t.IsActive).OrderBy(t => t.SortOrder).Take(2).ToListAsync();
                    for (var n = tables.Count + 1; n <= 2; n++)
                    {
                        var table = new RestaurantTable { TableNumber = $"D{n}", QRCodeString = $"t{id}-demo-{n}", SortOrder = 100 + n };
                        db.RestaurantTables.Add(table);
                        tables.Add(table);
                    }
                    await db.SaveChangesAsync();

                    int Item(string name) => items.First(i => i.Name == name).Id;
                    OrderItemRequest Line(string name, int quantity) => new() { MenuItemId = Item(name), Quantity = quantity };

                    var first = await orders.CreateOrder(new OrderRequest
                    {
                        OrderType = OrderType.DineIn, Table = tables[0].Id, ClientOrderId = Guid.NewGuid(), TakenBy = "Demo",
                        Items = new List<OrderItemRequest> { Line("Gujarati Thali", 2), Line("Chaas", 2) }
                    });
                    var second = await orders.CreateOrder(new OrderRequest
                    {
                        OrderType = OrderType.DineIn, Table = tables[1].Id, ClientOrderId = Guid.NewGuid(), TakenBy = "Demo",
                        Items = new List<OrderItemRequest> { Line("Khaman Dhokla", 1), Line("Masala Tea", 2) }
                    });
                    var third = await orders.CreateOrder(new OrderRequest
                    {
                        OrderType = OrderType.Takeaway, CustomerName = "Demo guest", ClientOrderId = Guid.NewGuid(), TakenBy = "Demo",
                        Items = new List<OrderItemRequest> { Line("Samosa", 4), Line("Gulab Jamun", 2) }
                    });

                    var cashBill = await bills.CreateBill(first.Id, new CreateBillRequest(null, null));
                    await bills.AddPayment(cashBill.Id, new AddPaymentRequest("Cash", cashBill.Total, null, null, cashBill.Version));
                    var upiBill = await bills.CreateBill(second.Id, new CreateBillRequest(null, null));
                    await bills.AddPayment(upiBill.Id, new AddPaymentRequest("Upi", upiBill.Total, null, null, upiBill.Version));
                    await bills.CreateBill(third.Id, new CreateBillRequest(null, null));

                    return Results.Ok(new { categories = categories.Count, items = items.Count, orders = 3 });
                }
                finally
                {
                    db.CurrentTenantId = tenantBefore;
                    db.CurrentOutletId = outletBefore;
                }
            })
                .RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole))
                .WithTags("Platform")
                .WithName("FillDemoData").WithSummary("Fill an empty restaurant with a sample menu and three orders of today, for demos");

            return app;
        }
    }
}
