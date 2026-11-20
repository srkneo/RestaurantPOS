using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // POS and Orders page, server side: everything a till can send wrong, and what other people may not do.
    public class OrderNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public OrderNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int T1, int T2, int Thali, int SoldOut);

        private Setup Build(Action<OutletSettings>? change = null)
        {
            var dbName = Guid.NewGuid().ToString();
            var f = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                });
            });
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outlet = db.Outlets.Single(o => o.Id == 1);
            var settings = outlet.GetSettings();
            settings.TaxMode = TaxMode.None;
            change?.Invoke(settings);
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var t1 = new RestaurantTable { TableNumber = "1", QRCodeString = "t1-1", SortOrder = 1 };
            var t2 = new RestaurantTable { TableNumber = "2", QRCodeString = "t1-2", SortOrder = 2 };
            db.RestaurantTables.AddRange(t1, t2);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            var soldOut = new MenuItem { Name = "Kaju Katli", Price = 99m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m, IsAvailable = false };
            db.MenuItems.AddRange(thali, soldOut);
            db.SaveChanges();
            return new Setup(f, t1.Id, t2.Id, thali.Id, soldOut.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task<(int Id, int Version)> Place(HttpClient client, int? table, int itemId, int quantity = 2, string? name = null)
        {
            var res = await client.PostAsync("/api/orders", Json(new
            {
                orderType = table == null ? "Takeaway" : "DineIn", table, customerName = name ?? (table == null ? "Ravi" : null),
                clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = itemId, quantity } }
            }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            return (body.GetProperty("orderId").GetInt32(), body.GetProperty("version").GetInt32());
        }

        private static async Task<JsonElement> Get(HttpClient client, int id) => await Read(await client.GetAsync($"/api/orders/{id}"));

        [Fact]
        public async Task BadOrders_AreRefused()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            object Line(int id, int quantity = 1, string? note = null) => new { menuItemId = id, quantity, note };
            var bad = new object[]
            {
                new { orderType = "DineIn", table = s.T1, items = Array.Empty<object>() },
                new { orderType = "DineIn", table = s.T1, items = new[] { Line(s.Thali, 0) } },
                new { orderType = "DineIn", table = s.T1, items = new[] { Line(s.Thali, -1) } },
                new { orderType = "DineIn", table = s.T1, items = new[] { Line(s.Thali, 100) } },
                new { orderType = "DineIn", table = s.T1, items = new[] { Line(99999) } },
                new { orderType = "DineIn", table = s.T1, items = new[] { Line(s.SoldOut) } },
                new { orderType = "DineIn", table = s.T1, items = new[] { Line(s.Thali, 1, new string('n', 201)) } },
                new { orderType = "DineIn", items = new[] { Line(s.Thali) } },
                new { orderType = "DineIn", table = 99999, items = new[] { Line(s.Thali) } },
                new { orderType = "Takeaway", items = new[] { Line(s.Thali) } },
                new { orderType = "Takeaway", customerName = "   ", items = new[] { Line(s.Thali) } },
                new { orderType = "Takeaway", customerName = new string('r', 101), items = new[] { Line(s.Thali) } },
                new { orderType = "Takeaway", customerName = "Ravi", customerPhone = "call me", items = new[] { Line(s.Thali) } },
                new { orderType = "Takeaway", customerName = "Ravi", customerPhone = "12-34", items = new[] { Line(s.Thali) } },
            };
            foreach (var body in bad)
            {
                var res = await cashier.PostAsync("/api/orders", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(0, (await Read(await cashier.GetAsync("/api/orders"))).GetArrayLength());
        }

        [Theory]
        [InlineData("98250 12345")]
        [InlineData("98250-12345")]
        [InlineData("+91 98250 12345")]
        [InlineData("+91-98250-12345")]
        public async Task PhoneAsPeopleTypeIt_IsAccepted(string phone)
        {
            var s = Build();
            using var factory = s.Factory;
            var res = await As(s.Factory, "cashier").PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", customerPhone = phone, items = new[] { new { menuItemId = s.Thali, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        [Fact]
        public async Task TurnedOffOrderTypes_AreRefused()
        {
            var s = Build(o => { o.DineInEnabled = false; });
            using var factory = s.Factory;
            var res = await As(s.Factory, "cashier").PostAsync("/api/orders", Json(new
            {
                orderType = "DineIn", table = s.T1, items = new[] { new { menuItemId = s.Thali, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task SameClientOrderId_PlacesTheOrderOnce()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            var clientOrderId = Guid.NewGuid();
            object Body() => new { orderType = "Takeaway", customerName = "Ravi", clientOrderId, items = new[] { new { menuItemId = s.Thali, quantity = 1 } } };
            var first = await Read(await cashier.PostAsync("/api/orders", Json(Body())));
            var again = await Read(await cashier.PostAsync("/api/orders", Json(Body())));
            Assert.Equal(first.GetProperty("orderId").GetInt32(), again.GetProperty("orderId").GetInt32());
            Assert.Equal(first.GetProperty("tokenNumber").GetInt32(), again.GetProperty("tokenNumber").GetInt32());
            Assert.Equal(1, (await Read(await cashier.GetAsync("/api/orders"))).GetArrayLength());
        }

        [Fact]
        public async Task Status_UnknownOrder_WrongVersion_AndBackwardSteps_AreRefused()
        {
            var s = Build();
            using var factory = s.Factory;
            var manager = As(s.Factory, "manager");
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync("/api/orders/99999/status", Json(new { status = "Ready" }))).StatusCode);

            var (id, version) = await Place(manager, s.T1, s.Thali);
            Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsync($"/api/orders/{id}/status", Json(new { status = "Ready", version = version + 5 }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/orders/{id}/status", Json(new { status = "Ready" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync($"/api/orders/{id}/status", Json(new { status = "Preparing" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync($"/api/orders/{id}/status", Json(new { status = "Received" }))).StatusCode);
            Assert.Equal("Ready", (await Get(manager, id)).GetProperty("status").GetString());
        }

        [Fact]
        public async Task Cancel_NeedsAReason_TheRightVersion_AndOnlyOnce()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            var (id, version) = await Place(cashier, null, s.Thali);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "  ", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = new string('x', 201), version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "Guest left", version = version + 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/orders/99999/cancel", Json(new { reason = "Guest left", version = 1 }))).StatusCode);
            var cancelled = await cashier.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "Guest left", version }));
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            var after = await Read(cancelled);
            Assert.Equal("Cancelled", after.GetProperty("status").GetString());
            var twice = await cashier.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "Again", version = after.GetProperty("version").GetInt32() }));
            Assert.True((int)twice.StatusCode is >= 400 and < 500, $"second cancel gave {twice.StatusCode}");
            // A cancelled order cannot be cooked or billed.
            Assert.Equal(HttpStatusCode.BadRequest, (await As(s.Factory, "manager").PutAsync($"/api/orders/{id}/status", Json(new { status = "Preparing" }))).StatusCode);
            var bill = await cashier.PostAsync($"/api/orders/{id}/bill", Json(new { }));
            Assert.True((int)bill.StatusCode is >= 400 and < 500, $"bill of a cancelled order gave {bill.StatusCode}");
        }

        [Fact]
        public async Task EditingItems_AfterCookingStarted_OrWithBadItems_IsRefused()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            var (id, version) = await Place(cashier, s.T1, s.Thali);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{id}/items", Json(new { items = Array.Empty<object>(), version }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{id}/items", Json(new { items = new[] { new { menuItemId = s.SoldOut, quantity = 1 } }, version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync($"/api/orders/{id}/items", Json(new { items = new[] { new { menuItemId = s.Thali, quantity = 1 } }, version = version + 3 }))).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await As(s.Factory, "manager").PutAsync($"/api/orders/{id}/status", Json(new { status = "Preparing" }))).StatusCode);
            var now = (await Get(cashier, id)).GetProperty("version").GetInt32();
            var late = await cashier.PostAsync($"/api/orders/{id}/items", Json(new { items = new[] { new { menuItemId = s.Thali, quantity = 1 } }, version = now }));
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
            Assert.Equal(2, (await Get(cashier, id)).GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        [Fact]
        public async Task MoveMergeSplit_BadRequests_AreRefused()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            var (a, va) = await Place(cashier, s.T1, s.Thali, 2);
            var (b, vb) = await Place(cashier, s.T2, s.Thali, 1);
            var (takeaway, vt) = await Place(cashier, null, s.Thali, 1);

            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{takeaway}/move", Json(new { tableId = s.T2, version = vt }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/move", Json(new { tableId = s.T1, version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/move", Json(new { tableId = 99999, version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/move", Json(new { tableId = s.T2, version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync($"/api/orders/{a}/move", Json(new { tableId = s.T2, version = va + 9 }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/orders/99999/move", Json(new { tableId = s.T2, version = 1 }))).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/merge", Json(new { fromOrderId = a, version = va, fromVersion = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync($"/api/orders/{a}/merge", Json(new { fromOrderId = b, version = va, fromVersion = vb + 4 }))).StatusCode);

            var itemId = (await Get(cashier, a)).GetProperty("items")[0].GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/split", Json(new { lines = Array.Empty<object>(), version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/split", Json(new { lines = new[] { new { orderItemId = itemId, quantity = 2 } }, version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/split", Json(new { lines = new[] { new { orderItemId = itemId, quantity = 3 } }, version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/split", Json(new { lines = new[] { new { orderItemId = itemId, quantity = 1 }, new { orderItemId = itemId, quantity = 1 } }, version = va }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a}/split", Json(new { lines = new[] { new { orderItemId = 99999, quantity = 1 } }, version = va }))).StatusCode);

            // Nothing changed.
            Assert.Equal(2, (await Get(cashier, a)).GetProperty("items")[0].GetProperty("quantity").GetInt32());
            Assert.Equal(3, (await Read(await cashier.GetAsync("/api/orders"))).GetArrayLength());
        }

        [Fact]
        public async Task OrdersList_BadDate_AndUnknownOrder()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("/api/orders?date=20-11-2026")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("/api/orders?date=2026-13-40")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/orders?date=2020-01-01")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/orders/99999")).StatusCode);
        }

        [Fact]
        public async Task OtherRestaurant_CannotSeeOrChangeAnOrder()
        {
            var s = Build();
            using var factory = s.Factory;
            var cashier = As(s.Factory, "cashier");
            var (id, version) = await Place(cashier, s.T1, s.Thali);
            var other = As(s.Factory, "manager", "2");
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{id}")).StatusCode);
            Assert.Equal(0, (await Read(await other.GetAsync("/api/orders"))).GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/orders/{id}/status", Json(new { status = "Ready" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "x", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/orders/{id}/items", Json(new { items = new[] { new { menuItemId = s.Thali, quantity = 9 } }, version }))).StatusCode);
            var bill = await other.PostAsync($"/api/orders/{id}/bill", Json(new { }));
            Assert.True(bill.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"other restaurant billing gave {bill.StatusCode}");
            var mine = await Get(cashier, id);
            Assert.Equal("Received", mine.GetProperty("status").GetString());
            Assert.Equal(2, mine.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        [Fact]
        public async Task Roles_KitchenCannotOrder_WaiterCannotCancelOrBill()
        {
            var s = Build();
            using var factory = s.Factory;
            Assert.Equal(HttpStatusCode.Forbidden, (await As(s.Factory, "kitchen").PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", items = new[] { new { menuItemId = s.Thali, quantity = 1 } }
            }))).StatusCode);
            var waiter = As(s.Factory, "waiter");
            var (id, version) = await Place(waiter, s.T1, s.Thali);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "x", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/orders/{id}/bill", Json(new { }))).StatusCode);
        }
    }
}
