using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using POSSystem.Data;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Real PostgreSQL, two restaurants billing at the same time through the API.
    // Nothing may cross between them, every bill must match its menu prices, and numbers and tokens must have no gaps or repeats.
    public class TwoRestaurantsPostgresTests : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
    {
        private const int OrdersEach = 20;
        private readonly PostgresFixture _pg;
        private readonly WebApplicationFactory<Program> _factory;

        public TwoRestaurantsPostgresTests(PostgresFixture pg, WebApplicationFactory<Program> factory)
        {
            _pg = pg;
            _factory = factory;
        }

        private record Shop(int Tenant, int Outlet, int TableId, int PaneerId, int ColaId, decimal BillTotal);

        private WebApplicationFactory<Program>? Start(out string connection)
        {
            connection = string.Empty;
            if (_pg.Container == null)
            {
                Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Docker is required in CI: " + _pg.DockerError);
                return null;
            }
            var cs = new NpgsqlConnectionStringBuilder(_pg.Container.GetConnectionString()) { Database = "pos_" + Guid.NewGuid().ToString("N") }.ConnectionString;
            connection = cs;
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                });
            });
        }

        // Restaurant 1: regular GST, Paneer 220 (5%) and Cola 60 (18%): 2 Paneer + 1 Cola = 533.
        // Restaurant 2: no GST, the SAME item names at other prices, Paneer 300 and Cola 50: 2 Paneer + 1 Cola = 650.
        private static async Task<(Shop One, Shop Two)> Seed(WebApplicationFactory<Program> f)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant2 = new Tenant { Name = "Second Cafe" };
            db.Tenants.Add(tenant2);
            await db.SaveChangesAsync();
            var outlet2 = new Outlet { TenantId = tenant2.Id, Name = "Second Cafe Main" };
            db.Outlets.Add(outlet2);
            var outlet1 = await db.Outlets.SingleAsync(o => o.Id == 1);
            var settings = outlet1.GetSettings();
            settings.TaxMode = TaxMode.Regular;
            outlet1.Settings = settings.ToJson();
            outlet1.Gstin = "24ABCDE1234F1Z5";
            await db.SaveChangesAsync();

            async Task<Shop> Menu(int tenant, int outlet, decimal paneer, decimal cola, decimal total)
            {
                db.CurrentTenantId = tenant;
                db.CurrentOutletId = outlet;
                var category = new MenuCategory { Name = "Q Mains", SortOrder = 50 };
                db.MenuCategories.Add(category);
                var table = new RestaurantTable { TableNumber = "Q1", QRCodeString = $"q-{tenant}", SortOrder = 50 };
                db.RestaurantTables.Add(table);
                await db.SaveChangesAsync();
                var p = new MenuItem { Name = "Paneer Tikka", Price = paneer, Category = "Q Mains", CategoryId = category.Id, TaxRate = 5m };
                var c = new MenuItem { Name = "Cola", Price = cola, Category = "Q Mains", CategoryId = category.Id, TaxRate = 18m };
                db.MenuItems.AddRange(p, c);
                await db.SaveChangesAsync();
                return new Shop(tenant, outlet, table.Id, p.Id, c.Id, total);
            }

            var one = await Menu(1, 1, 220m, 60m, 533m);
            var two = await Menu(tenant2.Id, outlet2.Id, 300m, 50m, 650m);
            return (one, two);
        }

        private static HttpClient Till(WebApplicationFactory<Program> f, Shop shop, string role = "cashier")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", shop.Tenant.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Outlet", shop.Outlet.ToString());
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        // One full sale: order (with a fake price the server must ignore), bill, cash payment.
        private static async Task<int> Sale(WebApplicationFactory<Program> f, Shop shop)
        {
            var till = Till(f, shop);
            var order = await Read(await till.PostAsync("/api/orders", Json(new
            {
                table = shop.TableId,
                items = new object[] { new { menuItemId = shop.PaneerId, quantity = 2, unitPrice = 1, price = 1 }, new { menuItemId = shop.ColaId, quantity = 1, unitPrice = 1 } },
                total = 1,
                clientOrderId = Guid.NewGuid()
            })));
            var orderId = order.GetProperty("orderId").GetInt32();
            var bill = await Read(await till.PostAsync($"/api/orders/{orderId}/bill", Json(new { total = 1 })));
            Assert.Equal(shop.BillTotal, bill.GetProperty("total").GetDecimal());
            var billId = bill.GetProperty("id").GetInt32();
            await Read(await till.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "cash", amount = shop.BillTotal, version = 1 })));
            return billId;
        }

        [Fact]
        public async Task TwoRestaurants_BillingAtOnce_NeverMixAndAlwaysMatchTheMenu()
        {
            using var f = Start(out _);
            if (f == null) return;
            var (one, two) = await Seed(f);

            var work = Enumerable.Range(0, OrdersEach).SelectMany(_ => new[] { one, two }).Select(shop => Task.Run(() => Sale(f, shop))).ToList();
            var billIds = await Task.WhenAll(work);
            Assert.Equal(OrdersEach * 2, billIds.Distinct().Count());

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bills = await db.Bills.IgnoreQueryFilters().Include(b => b.Lines).Include(b => b.Payments).Where(b => billIds.Contains(b.Id)).ToListAsync();
            var orders = await db.KitchenOrders.IgnoreQueryFilters().Include(o => o.Items).Where(o => bills.Select(b => b.KitchenOrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id);
            var prices = await db.MenuItems.IgnoreQueryFilters().ToDictionaryAsync(m => m.Id, m => (m.TenantId, m.Price));

            foreach (var shop in new[] { one, two })
            {
                var mine = bills.Where(b => b.TenantId == shop.Tenant).ToList();
                Assert.Equal(OrdersEach, mine.Count);
                Assert.All(mine, b => Assert.Equal(shop.Outlet, b.OutletId));
                Assert.Equal(Enumerable.Range(1, OrdersEach), mine.Select(b => b.Sequence).OrderBy(x => x));          // no gaps, no repeats
                Assert.Equal(OrdersEach, mine.Select(b => b.InvoiceNumber).Distinct().Count());
                Assert.All(mine, b => Assert.Equal(shop.BillTotal, b.Total));
                Assert.All(mine, b => Assert.Equal(BillStatus.Paid, b.Status));
                Assert.All(mine, b => Assert.Equal(shop.BillTotal, b.Payments.Sum(p => p.Amount)));
                var tokens = mine.Select(b => orders[b.KitchenOrderId].TokenNumber).ToList();
                Assert.Equal(OrdersEach, tokens.Distinct().Count());
            }

            foreach (var bill in bills)
            {
                var order = orders[bill.KitchenOrderId];
                Assert.Equal(bill.TenantId, order.TenantId);
                Assert.Equal(bill.OutletId, order.OutletId);
                foreach (var item in order.Items)
                {
                    var (tenant, price) = prices[item.MenuItemId];
                    Assert.Equal(order.TenantId, tenant);          // an order only ever holds its own restaurant's menu items
                    Assert.Equal(price, item.UnitPrice);            // the fake price from the screen was ignored
                }
                Assert.Equal(bill.ItemsTotal, bill.Lines.Sum(l => l.Amount));
                Assert.Equal(order.Items.Sum(i => i.Quantity * i.UnitPrice), bill.ItemsTotal);
            }

            // Each restaurant sees only its own bills, even when it asks for the other's bill by id.
            var otherBill = bills.First(b => b.TenantId == two.Tenant).Id;
            Assert.Equal(HttpStatusCode.NotFound, (await Till(f, one).GetAsync($"/api/bills/{otherBill}")).StatusCode);
            var listOne = await Read(await Till(f, one).GetAsync("/api/bills"));
            Assert.All(listOne.EnumerateArray(), b => Assert.Contains(b.GetProperty("id").GetInt32(), bills.Where(x => x.TenantId == one.Tenant).Select(x => x.Id)));
            var reportTwo = await Read(await Till(f, two, "manager").GetAsync("/api/reports/day"));
            Assert.Equal(OrdersEach, reportTwo.GetProperty("billCount").GetInt32());
            Assert.Equal(OrdersEach * two.BillTotal, reportTwo.GetProperty("total").GetDecimal());

            // A restaurant cannot order the other restaurant's menu item.
            var foreign = await Till(f, one).PostAsync("/api/orders", Json(new
            {
                table = one.TableId,
                items = new object[] { new { menuItemId = two.PaneerId, quantity = 1 } },
                clientOrderId = Guid.NewGuid()
            }));
            Assert.False(foreign.IsSuccessStatusCode);
        }

        [Fact]
        public async Task PriceChangeAfterOrdering_DoesNotChangeTheBill()
        {
            using var f = Start(out _);
            if (f == null) return;
            var (one, _) = await Seed(f);
            var till = Till(f, one);
            var order = await Read(await till.PostAsync("/api/orders", Json(new
            {
                table = one.TableId,
                items = new object[] { new { menuItemId = one.PaneerId, quantity = 2 }, new { menuItemId = one.ColaId, quantity = 1 } },
                clientOrderId = Guid.NewGuid()
            })));

            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var paneer = await db.MenuItems.SingleAsync(m => m.Id == one.PaneerId);
                paneer.Price = 999m;
                await db.SaveChangesAsync();
            }

            var bill = await Read(await till.PostAsync($"/api/orders/{order.GetProperty("orderId").GetInt32()}/bill", Json(new { })));
            Assert.Equal(533m, bill.GetProperty("total").GetDecimal());
            Assert.Equal(220m, bill.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        }
    }
}
