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
    // Kitchen stations: the owner makes them and puts items on them; every order line remembers its station.
    public class StationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public StationTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int CategoryId, int NaanId, int TeaId);

        private Setup Build()
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
            var category = new MenuCategory { Name = "Mains", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "3", QRCodeString = "t1-3", SortOrder = 3 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var naan = new MenuItem { Name = "Butter Naan", Price = 40m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.AddRange(naan, tea);
            db.SaveChanges();
            return new Setup(f, table.Id, category.Id, naan.Id, tea.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task<int> AddStation(WebApplicationFactory<Program> f, string name, bool active = true)
        {
            var res = await As(f, "admin").PostAsync("/api/admin/menu/stations", Json(new { name, sortOrder = 1, isActive = active }));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            return (await Read(res)).GetProperty("id").GetInt32();
        }

        private static Task<HttpResponseMessage> PutItem(Setup s, int itemId, string name, decimal price, int? stationId) =>
            As(s.Factory, "admin").PutAsync($"/api/admin/menu/items/{itemId}", Json(new
            {
                name, price, categoryId = s.CategoryId, description = (string?)null, isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1, stationId
            }));

        [Fact]
        public async Task OrderLines_RememberTheStationOfTheirItem()
        {
            var s = Build();
            using var f = s.Factory;
            var tandoor = await AddStation(f, "Tandoor");
            Assert.Equal(HttpStatusCode.OK, (await PutItem(s, s.NaanId, "Butter Naan", 40m, tandoor)).StatusCode);

            var items = await Read(await As(f, "admin").GetAsync("/api/admin/menu/items"));
            Assert.Equal(tandoor, items.EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Butter Naan").GetProperty("stationId").GetInt32());

            var placed = await As(f, "cashier").PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(),
                items = new object[] { new { menuItemId = s.NaanId, quantity = 2 }, new { menuItemId = s.TeaId, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();

            var order = await Read(await As(f, "kitchen").GetAsync($"/api/orders/{orderId}"));
            var lines = order.GetProperty("items").EnumerateArray().ToList();
            var naan = lines.Single(l => l.GetProperty("itemName").GetString() == "Butter Naan");
            Assert.Equal("Tandoor", naan.GetProperty("stationName").GetString());
            Assert.Equal(tandoor, naan.GetProperty("stationId").GetInt32());
            var tea = lines.Single(l => l.GetProperty("itemName").GetString() == "Tea");
            Assert.Equal(JsonValueKind.Null, tea.GetProperty("stationId").ValueKind);
        }

        [Fact]
        public async Task KitchenScreens_SeeOnlyStationsThatAreOn()
        {
            var s = Build();
            using var f = s.Factory;
            await AddStation(f, "Tandoor");
            await AddStation(f, "Old counter", active: false);
            var list = await Read(await As(f, "kitchen").GetAsync("/api/stations"));
            Assert.Equal(new[] { "Tandoor" }, list.EnumerateArray().Select(x => x.GetProperty("name").GetString()));
            Assert.Equal(2, (await Read(await As(f, "admin").GetAsync("/api/admin/menu/stations"))).GetArrayLength());
        }

        [Fact]
        public async Task Stations_AreCheckedAndOnlyForOwnerOrManager()
        {
            var s = Build();
            using var f = s.Factory;
            await AddStation(f, "Tandoor");
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "admin").PostAsync("/api/admin/menu/stations", Json(new { name = "Tandoor", sortOrder = 2 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "admin").PostAsync("/api/admin/menu/stations", Json(new { name = " ", sortOrder = 2 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PostAsync("/api/admin/menu/stations", Json(new { name = "Bar", sortOrder = 2 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutItem(s, s.TeaId, "Tea", 20m, 9999)).StatusCode);
            // Editing an item without a station keeps it on the main kitchen.
            Assert.Equal(HttpStatusCode.OK, (await PutItem(s, s.TeaId, "Tea", 25m, null)).StatusCode);
        }
    }
}
