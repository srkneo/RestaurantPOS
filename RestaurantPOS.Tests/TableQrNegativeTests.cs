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
    // Tables page, QR sheet and the guest QR menu: what owners and guests can get wrong, and what must stay closed.
    public class TableQrNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TableQrNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int Table, int Thali, int OtherShopItem);

        private Setup Build(bool qrOn = true)
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
            settings.QrOrderingEnabled = qrOn;
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "5", QRCodeString = "t1-5", SortOrder = 5 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();

            // An item of another restaurant: a guest of this one must not be able to order it.
            db.CurrentTenantId = 2;
            var foreign = new MenuItem { Name = "Other Shop Pizza", Price = 1m, Category = "Pizza", TaxRate = 5m };
            db.MenuItems.Add(foreign);
            db.SaveChanges();
            db.CurrentTenantId = 1;
            return new Setup(f, table.Id, thali.Id, foreign.Id);
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

        private static async Task<string> TokenOf(WebApplicationFactory<Program> f, int tableId)
        {
            var list = await Read(await As(f, "manager").GetAsync("/api/admin/tables/qr"));
            return list.EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == tableId).GetProperty("token").GetString()!;
        }

        private static Task<HttpResponseMessage> GuestOrder(HttpClient guest, string token, object items) =>
            guest.PostAsync($"/api/qr/{token}/order", Json(new { items, clientOrderId = Guid.NewGuid() }));

        [Fact]
        public async Task BadTables_AreRefused_AndTablesWithOrdersStay()
        {
            var s = Build();
            using var factory = s.Factory;
            var manager = As(s.Factory, "manager");
            foreach (var body in new object[] { new { tableNumber = "" }, new { tableNumber = "   " }, new { tableNumber = new string('9', 21) }, new { tableNumber = "5" } })
            {
                var res = await manager.PostAsync("/api/admin/tables", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync("/api/admin/tables/99999", Json(new { tableNumber = "9" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync("/api/admin/tables/99999")).StatusCode);

            var placed = await As(s.Factory, "cashier").PostAsync("/api/orders", Json(new
            {
                orderType = "DineIn", table = s.Table, items = new[] { new { menuItemId = s.Thali, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync($"/api/admin/tables/{s.Table}")).StatusCode);

            var spare = await manager.PostAsync("/api/admin/tables", Json(new { tableNumber = "12", isActive = true, sortOrder = 12 }));
            Assert.True(spare.IsSuccessStatusCode, $"add table gave {spare.StatusCode}");
            var spareId = (await Read(spare)).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync($"/api/admin/tables/{spareId}", Json(new { tableNumber = "5" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/admin/tables/{spareId}")).StatusCode);
        }

        [Fact]
        public async Task Tables_OtherRestaurant_AndRoles()
        {
            var s = Build();
            using var factory = s.Factory;
            var other = As(s.Factory, "admin", "2");
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/admin/tables/{s.Table}", Json(new { tableNumber = "Hacked" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/admin/tables/{s.Table}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/admin/tables/{s.Table}/qr", Json(new { }))).StatusCode);
            Assert.DoesNotContain("t1-5", await (await other.GetAsync("/api/admin/tables/qr")).Content.ReadAsStringAsync());
            foreach (var role in new[] { "cashier", "waiter", "kitchen" })
            {
                var c = As(s.Factory, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/tables", Json(new { tableNumber = "77" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/tables/qr")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync("/api/admin/tables/qr-setting", Json(new { enabled = false }))).StatusCode);
            }
        }

        [Fact]
        public async Task QrImage_NeedsAWebAddress()
        {
            var s = Build();
            using var factory = s.Factory;
            var manager = As(s.Factory, "manager");
            foreach (var origin in new[] { "", "pos.example.com", "javascript:alert(1)", "ftp://pos.example.com", "file:///etc/passwd" })
                Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"/api/admin/tables/{s.Table}/qr.svg?origin={Uri.EscapeDataString(origin)}")).StatusCode);
            var ok = await manager.GetAsync($"/api/admin/tables/{s.Table}/qr.svg?origin={Uri.EscapeDataString("https://pos.example.com/some/page?x=1")}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("image/svg+xml", ok.Content.Headers.ContentType?.MediaType);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/admin/tables/99999/qr.svg?origin=https%3A%2F%2Fpos.example.com")).StatusCode);
        }

        [Fact]
        public async Task Qr_OldToken_InactiveTable_AndDisabledRestaurant_AreNotFound()
        {
            var s = Build();
            using var factory = s.Factory;
            var guest = s.Factory.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/api/qr/not-a-real-token")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/qr/{new string('a', 41)}")).StatusCode);

            var token = await TokenOf(s.Factory, s.Table);
            Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/qr/{token}")).StatusCode);
            var renewed = await Read(await As(s.Factory, "manager").PostAsync($"/api/admin/tables/{s.Table}/qr", Json(new { })));
            var fresh = renewed.GetProperty("token").GetString()!;
            Assert.NotEqual(token, fresh);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/qr/{token}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await GuestOrder(guest, token, new[] { new { menuItemId = s.Thali, quantity = 1 } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/qr/{fresh}")).StatusCode);

            using (var scope = s.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.RestaurantTables.Single(t => t.Id == s.Table).IsActive = false;
                db.SaveChanges();
            }
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/qr/{fresh}")).StatusCode);

            using (var scope = s.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.RestaurantTables.Single(t => t.Id == s.Table).IsActive = true;
                db.Tenants.Single(t => t.Id == 1).IsActive = false;
                db.SaveChanges();
                // The platform page clears the one-minute cache when it disables a restaurant; do the same here.
                scope.ServiceProvider.GetRequiredService<POSSystem.Platform.TenantStatus>().Forget(1);
            }
            var menuOff = (await guest.GetAsync($"/api/qr/{fresh}")).StatusCode;
            Assert.True(menuOff is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, $"menu of a disabled restaurant gave {menuOff}");
            // A real guest (not signed in) gets 404 too.
            var anonymous = s.Factory.CreateClient();
            anonymous.DefaultRequestHeaders.Add("X-Test-Tenant", "2");
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/qr/{fresh}")).StatusCode);
            // The test client is signed in to that restaurant, so it may be stopped even earlier (401); a real guest gets 404.
            var blocked = (await GuestOrder(guest, fresh, new[] { new { menuItemId = s.Thali, quantity = 1 } })).StatusCode;
            Assert.True(blocked is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, $"order on a disabled restaurant gave {blocked}");
        }

        [Fact]
        public async Task GuestOrders_BadItems_TooMany_OtherShopsItems_AndQrOff_AreRefused()
        {
            var s = Build();
            using var factory = s.Factory;
            var guest = s.Factory.CreateClient();
            var token = await TokenOf(s.Factory, s.Table);
            foreach (var items in new object[]
            {
                Array.Empty<object>(),
                new[] { new { menuItemId = s.Thali, quantity = 31 } },
                new[] { new { menuItemId = s.Thali, quantity = 20 }, new { menuItemId = s.Thali, quantity = 11 } },
                new[] { new { menuItemId = s.Thali, quantity = 0 } },
                new[] { new { menuItemId = s.Thali, quantity = -3 } },
                new[] { new { menuItemId = 99999, quantity = 1 } },
                new[] { new { menuItemId = s.OtherShopItem, quantity = 1 } },
            })
            {
                var res = await GuestOrder(guest, token, items);
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(items)} gave {res.StatusCode}");
            }
            Assert.DoesNotContain("Other Shop Pizza", await (await guest.GetAsync($"/api/qr/{token}")).Content.ReadAsStringAsync());
            Assert.Equal(0, (await Read(await As(s.Factory, "cashier").GetAsync("/api/orders"))).GetArrayLength());

            Assert.Equal(HttpStatusCode.OK, (await As(s.Factory, "manager").PutAsync("/api/admin/tables/qr-setting", Json(new { enabled = false }))).StatusCode);
            var off = await GuestOrder(guest, token, new[] { new { menuItemId = s.Thali, quantity = 1 } });
            Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
            Assert.Contains("qr-off", await off.Content.ReadAsStringAsync());
            var menu = await Read(await guest.GetAsync($"/api/qr/{token}"));
            Assert.Contains("Gujarati Thali", menu.ToString());
        }

        [Fact]
        public async Task GuestCall_OnlyWaiterOrBill()
        {
            var s = Build();
            using var factory = s.Factory;
            var guest = s.Factory.CreateClient();
            var token = await TokenOf(s.Factory, s.Table);
            foreach (var kind in new[] { "", "manager", "police", "<script>" })
                Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsync($"/api/qr/{token}/call", Json(new { kind }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await guest.PostAsync($"/api/qr/{token}/call", Json(new { kind = " Bill " }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.PostAsync("/api/qr/unknown-token/call", Json(new { kind = "waiter" }))).StatusCode);
        }
    }
}
