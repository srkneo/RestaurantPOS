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
    // Recipes: what one portion uses; completing an order takes it from stock once, with the token in the history.
    public class RecipeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RecipeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int ChaiId, int ColaId);

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
            var category = new MenuCategory { Name = "Tea", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "3", QRCodeString = "t1-3", SortOrder = 3 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var chai = new MenuItem { Name = "Masala Chai", Price = 20m, Category = "Tea", CategoryId = category.Id };
            var cola = new MenuItem { Name = "Cola", Price = 40m, Category = "Tea", CategoryId = category.Id };
            db.MenuItems.AddRange(chai, cola);
            db.SaveChanges();
            return new Setup(f, table.Id, chai.Id, cola.Id);
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

        private static async Task<int> AddStock(HttpClient manager, string name, string unit, decimal quantity) =>
            (await Read(await manager.PostAsync("/api/stock", Json(new { name, unit, quantity })))).GetProperty("id").GetInt32();

        private static async Task<decimal> QuantityOf(HttpClient manager, int stockId) =>
            (await Read(await manager.GetAsync("/api/stock"))).EnumerateArray().First(s => s.GetProperty("id").GetInt32() == stockId).GetProperty("quantity").GetDecimal();

        [Fact]
        public async Task Recipe_IsSavedAndChecked_ByManagersOnly()
        {
            var s = Build();
            using var f = s.Factory;
            var manager = As(f, "manager");
            var milk = await AddStock(manager, "Milk", "l", 10m);
            var sugar = await AddStock(manager, "Sugar", "kg", 2m);

            var saved = await manager.PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = new[] { new { stockItemId = milk, quantity = 0.15m }, new { stockItemId = sugar, quantity = 0.01m } } }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var recipe = await Read(await manager.GetAsync($"/api/menu/{s.ChaiId}/recipe"));
            Assert.Equal(2, recipe.GetArrayLength());
            Assert.Equal("Milk", recipe[0].GetProperty("name").GetString());
            Assert.Equal(0.15m, recipe[0].GetProperty("quantity").GetDecimal());
            Assert.Equal("l", recipe[0].GetProperty("unit").GetString());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = Array.Empty<object>() }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = new[] { new { stockItemId = 999, quantity = 1m } } }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = new[] { new { stockItemId = milk, quantity = 0m } } }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = new[] { new { stockItemId = milk, quantity = 1m }, new { stockItemId = milk, quantity = 2m } } }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/menu/9999/recipe")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = Array.Empty<object>() }))).StatusCode);
            Assert.Equal(0, (await Read(await manager.GetAsync($"/api/menu/{s.ChaiId}/recipe"))).GetArrayLength());
        }

        [Fact]
        public async Task CompletingAnOrder_TakesItsRecipeFromStock_Once()
        {
            var s = Build();
            using var f = s.Factory;
            var manager = As(f, "manager");
            var milk = await AddStock(manager, "Milk", "l", 10m);
            var sugar = await AddStock(manager, "Sugar", "kg", 2m);
            await manager.PutAsync($"/api/menu/{s.ChaiId}/recipe", Json(new { lines = new[] { new { stockItemId = milk, quantity = 0.15m }, new { stockItemId = sugar, quantity = 0.01m } } }));

            var cashier = As(f, "cashier");
            var placed = await cashier.PostAsync("/api/orders", Json(new
            {
                table = s.TableId,
                items = new object[] { new { menuItemId = s.ChaiId, quantity = 2 }, new { menuItemId = s.ColaId, quantity = 1 } },
                clientOrderId = Guid.NewGuid()
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();

            // Placing and cooking do not touch stock; completing does.
            Assert.Equal(10m, await QuantityOf(manager, milk));
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/orders/{orderId}/status", Json(new { status = "Ready" }))).StatusCode);
            Assert.Equal(10m, await QuantityOf(manager, milk));
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/orders/{orderId}/status", Json(new { status = "Completed" }))).StatusCode);
            Assert.Equal(9.7m, await QuantityOf(manager, milk));
            Assert.Equal(1.98m, await QuantityOf(manager, sugar));

            var moves = await Read(await manager.GetAsync($"/api/stock/{milk}/moves"));
            Assert.Equal("Used", moves[0].GetProperty("kind").GetString());
            Assert.Equal(-0.3m, moves[0].GetProperty("change").GetDecimal());
            Assert.StartsWith("Token ", moves[0].GetProperty("note").GetString());

            // A completed order cannot complete again, so stock is taken only once.
            await As(f, "admin").PutAsync($"/api/orders/{orderId}/status", Json(new { status = "Completed" }));
            Assert.Equal(9.7m, await QuantityOf(manager, milk));

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.StockMoves.IgnoreQueryFilters().CountAsync(m => m.Kind == POSSystem.Inventory.StockMoveKind.Used));
        }
    }
}
