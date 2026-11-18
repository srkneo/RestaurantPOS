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
    // Running tab: more rounds on a table's open order, table status on the POS, moving an order to another table.
    public class RunningTabTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RunningTabTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int[] Tables, int NaanId, int TeaId);

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
            var outlet = db.Outlets.Single(o => o.Id == 1);
            var settings = outlet.GetSettings();
            settings.TaxMode = TaxMode.None;
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Mains", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var tables = new[] { "1", "2", "3" }.Select((n, i) => new RestaurantTable { TableNumber = n, QRCodeString = "t1-" + n, SortOrder = i + 1 }).ToList();
            db.RestaurantTables.AddRange(tables);
            db.SaveChanges();
            var naan = new MenuItem { Name = "Butter Naan", Price = 40m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.AddRange(naan, tea);
            db.SaveChanges();
            return new Setup(f, tables.Select(t => t.Id).ToArray(), naan.Id, tea.Id);
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

        [Fact]
        public async Task NewRound_AfterCookingStarted_IsAdded_AndGoesBackToTheKitchen()
        {
            var s = Build();
            using var f = s.Factory;
            var (orderId, version) = await Place(s, s.Tables[0], s.NaanId, 2);
            var kitchen = As(f, "kitchen");
            Assert.Equal(HttpStatusCode.OK, (await kitchen.PutAsync($"/api/orders/{orderId}/status", Json(new { status = "Preparing", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await kitchen.PutAsync($"/api/orders/{orderId}/status", Json(new { status = "Ready", version = version + 1 }))).StatusCode);

            var added = await As(f, "cashier").PostAsync($"/api/orders/{orderId}/add-items",
                Json(new { items = new[] { new { menuItemId = s.TeaId, quantity = 3 } }, version = version + 2 }));
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var order = await Read(added);
            Assert.Equal("Received", order.GetProperty("status").GetString());
            var lines = order.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal(1, lines.Single(l => l.GetProperty("itemName").GetString() == "Butter Naan").GetProperty("round").GetInt32());
            Assert.Equal(2, lines.Single(l => l.GetProperty("itemName").GetString() == "Tea").GetProperty("round").GetInt32());

            // An old screen with the previous version is told the order changed.
            var stale = await As(f, "cashier").PostAsync($"/api/orders/{orderId}/add-items",
                Json(new { items = new[] { new { menuItemId = s.TeaId, quantity = 1 } }, version = version + 2 }));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        [Fact]
        public async Task Tables_ShowFreeOccupiedAndBilled_AndABilledTabTakesNoMoreItems()
        {
            var s = Build();
            using var f = s.Factory;
            var (orderId, version) = await Place(s, s.Tables[0], s.NaanId, 1);

            var board = await Board(f);
            Assert.Equal("Occupied", Row(board, s.Tables[0]).GetProperty("status").GetString());
            Assert.Equal(orderId, Row(board, s.Tables[0]).GetProperty("orderId").GetInt32());
            Assert.Equal(version, Row(board, s.Tables[0]).GetProperty("orderVersion").GetInt32());
            Assert.Equal("Free", Row(board, s.Tables[1]).GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, Row(board, s.Tables[1]).GetProperty("orderId").ValueKind);

            Assert.Equal(HttpStatusCode.Created, (await As(f, "cashier").PostAsync($"/api/orders/{orderId}/bill", Json(new { }))).StatusCode);
            Assert.Equal("Billed", Row(await Board(f), s.Tables[0]).GetProperty("status").GetString());

            var more = await As(f, "cashier").PostAsync($"/api/orders/{orderId}/add-items",
                Json(new { items = new[] { new { menuItemId = s.TeaId, quantity = 1 } }, version }));
            Assert.Equal(HttpStatusCode.Conflict, more.StatusCode);
            Assert.Equal("billed", (await Read(more)).GetProperty("code").GetString());
        }

        [Fact]
        public async Task Move_GoesToAFreeTable_NotToABusyOne()
        {
            var s = Build();
            using var f = s.Factory;
            var (orderId, version) = await Place(s, s.Tables[0], s.NaanId, 1);
            await Place(s, s.Tables[1], s.TeaId, 1);
            var cashier = As(f, "cashier");

            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{orderId}/move", Json(new { tableId = s.Tables[1], version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync($"/api/orders/{orderId}/move", Json(new { tableId = s.Tables[2], version = version + 5 }))).StatusCode);

            var moved = await cashier.PostAsync($"/api/orders/{orderId}/move", Json(new { tableId = s.Tables[2], version }));
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            Assert.Equal("3", (await Read(moved)).GetProperty("table").GetProperty("tableNumber").GetString());

            var board = await Board(f);
            Assert.Equal("Free", Row(board, s.Tables[0]).GetProperty("status").GetString());
            Assert.Equal("Occupied", Row(board, s.Tables[2]).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync($"/api/orders/{orderId}/move", Json(new { tableId = s.Tables[0], version = version + 1 }))).StatusCode);
        }

        private static async Task<(int Id, int Version)> Place(Setup s, int tableId, int itemId, int quantity)
        {
            var placed = await As(s.Factory, "cashier").PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = itemId, quantity } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var body = await Read(placed);
            return (body.GetProperty("orderId").GetInt32(), body.GetProperty("version").GetInt32());
        }

        // The test database also has the sample tables of a new restaurant, so rows are found by id.
        private static JsonElement Row(JsonElement[] board, int tableId) => board.Single(t => t.GetProperty("id").GetInt32() == tableId);

        private static async Task<JsonElement[]> Board(WebApplicationFactory<Program> f) =>
            (await Read(await As(f, "cashier").GetAsync("/api/tables"))).EnumerateArray().ToArray();
    }
}
