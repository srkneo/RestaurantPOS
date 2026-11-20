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
    // Menu and staff pages, server side: bad input, removed items, other restaurants and roles.
    public class MenuStaffNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MenuStaffNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Category, int Thali) Build()
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
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return (f, category.Id, thali.Id);
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

        private static object Item(int? categoryId, string name = "Paneer Tikka", decimal price = 250m, decimal taxRate = 5m, string? description = null, int? stationId = null) =>
            new { name, price, categoryId, description, isVeg = true, isAvailable = true, taxRate, sortOrder = 1, stationId };

        [Fact]
        public async Task BadMenuItems_AreRefused()
        {
            var (f, category, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            foreach (var body in new[]
            {
                Item(category, name: ""), Item(category, name: "   "), Item(category, name: new string('n', 101)),
                Item(category, price: -1m), Item(category, price: 100000.01m), Item(category, taxRate: 7m), Item(category, taxRate: -5m),
                Item(category, description: new string('d', 501)), Item(null), Item(99999), Item(category, stationId: 99999),
            })
            {
                var res = await manager.PostAsync("/api/admin/menu/items", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync("/api/admin/menu/items/99999", Json(Item(category)))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync("/api/admin/menu/items/99999/availability", Json(new { isAvailable = false }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync("/api/admin/menu/items/99999")).StatusCode);
        }

        [Fact]
        public async Task BadCategories_AreRefused()
        {
            var (f, category, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            foreach (var body in new object[]
            {
                new { name = "", sortOrder = 1 }, new { name = new string('c', 101), sortOrder = 1 }, new { name = "Thali", sortOrder = 2 },
                new { name = "Breakfast", sortOrder = 1, availableFrom = "25:00", availableTo = "11:00" },
                new { name = "Breakfast", sortOrder = 1, availableFrom = "7am", availableTo = "11:00" },
                new { name = "Breakfast", sortOrder = 1, availableFrom = "07:00" },
            })
            {
                var res = await manager.PostAsync("/api/admin/menu/categories", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync("/api/admin/menu/categories/99999", Json(new { name = "X", sortOrder = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/admin/menu/categories/{category}", Json(new { name = "Thali", sortOrder = 3 }))).StatusCode);
        }

        [Fact]
        public async Task RemovedItem_CannotBeOrdered_AndLeavesThePosMenu()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/admin/menu/items/{thali}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"/api/admin/menu/items/{thali}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync($"/api/admin/menu/items/{thali}/availability", Json(new { isAvailable = true }))).StatusCode);
            Assert.DoesNotContain("Gujarati Thali", await (await As(f, "cashier").GetAsync("/api/menuitems")).Content.ReadAsStringAsync());
            var order = await As(f, "cashier").PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", items = new[] { new { menuItemId = thali, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
        }

        [Fact]
        public async Task SoldOutItem_StaysOnTheMenuButCannotBeOrdered()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            Assert.Equal(HttpStatusCode.OK, (await As(f, "manager").PutAsync($"/api/admin/menu/items/{thali}/availability", Json(new { isAvailable = false }))).StatusCode);
            var order = await As(f, "cashier").PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", items = new[] { new { menuItemId = thali, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
            Assert.Contains("sold out", await order.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Menu_OtherRestaurant_AndRoles()
        {
            var (f, category, thali) = Build();
            using var factory = f;
            var other = As(f, "admin", "2");
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/admin/menu/items/{thali}", Json(Item(category, name: "Hacked")))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/admin/menu/items/{thali}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/admin/menu/items/{thali}/availability", Json(new { isAvailable = false }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/admin/menu/categories/{category}", Json(new { name = "Hacked", sortOrder = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsync("/api/admin/menu/items", Json(Item(category, name: "Sneaky")))).StatusCode);
            Assert.DoesNotContain("Gujarati Thali", await (await other.GetAsync("/api/admin/menu/items")).Content.ReadAsStringAsync());

            foreach (var role in new[] { "cashier", "waiter", "kitchen" })
            {
                var c = As(f, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/menu/items", Json(Item(category)))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync($"/api/admin/menu/items/{thali}")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/menu/categories", Json(new { name = "X", sortOrder = 1 }))).StatusCode);
            }
            var mine = await Read(await As(f, "manager").GetAsync("/api/admin/menu/items"));
            Assert.Equal("Gujarati Thali", mine.EnumerateArray().Single().GetProperty("name").GetString());
        }

        private static object Staff(string email, string role, bool isDevice = false, string password = "Test#1234", string displayName = "Ravi") =>
            new { email, displayName, role, password, isDevice };

        [Fact]
        public async Task Staff_CannotBeGivenThePlatformRole()
        {
            var (f, _, _) = Build();
            using var factory = f;
            var admin = As(f, "admin");
            foreach (var body in new[]
            {
                Staff("p1@test.local", "platform"), Staff("p2@test.local", "Admin"), Staff("p3@test.local", "owner"), Staff("p4@test.local", ""),
                Staff("p5@test.local", "cashier", isDevice: true), Staff("not-an-email", "cashier"), Staff("p6@test.local", "cashier", password: ""),
                Staff("p7@test.local", "cashier", password: "abc"), Staff("p8@test.local", "cashier", displayName: new string('r', 101)),
                Staff("p9@test.local", "cashier", displayName: " "),
            })
            {
                var res = await admin.PostAsync("/api/staff", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/staff", Json(Staff("dup@test.local", "cashier")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/staff", Json(Staff("DUP@test.local", "manager")))).StatusCode);
            Assert.DoesNotContain("platform", await (await admin.GetAsync("/api/staff")).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Staff_ManagersCannotManageStaff_AndPinsAreChecked()
        {
            var (f, _, _) = Build();
            using var factory = f;
            var admin = As(f, "admin");
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").GetAsync("/api/staff")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PostAsync("/api/staff", Json(Staff("m1@test.local", "admin")))).StatusCode);

            var cashierId = (await Read(await admin.PostAsync("/api/staff", Json(Staff("till@test.local", "cashier"))))).GetProperty("id").GetString();
            var managerId = (await Read(await admin.PostAsync("/api/staff", Json(Staff("boss@test.local", "manager"))))).GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/staff/{cashierId}/pin", Json(new { pin = "4321" }))).StatusCode);
            foreach (var pin in new[] { "12", "1234567", "abcd", "12 34" })
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/staff/{managerId}/pin", Json(new { pin }))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/staff/{managerId}/pin", Json(new { pin = "4321" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsync("/api/staff/no-such-user/pin", Json(new { pin = "4321" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "admin", "2").PutAsync($"/api/staff/{managerId}/pin", Json(new { pin = "9999" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsync("/api/staff/no-such-user", Json(new { displayName = "X", role = "cashier", isActive = true }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/staff/{cashierId}", Json(new { displayName = "Ravi", role = "platform", isActive = true }))).StatusCode);
        }
    }
}
