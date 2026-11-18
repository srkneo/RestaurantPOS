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
    // Captain mode: a waiter takes orders and adds rounds at the table, but never bills, cancels, merges or splits.
    public class WaiterTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public WaiterTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int OtherTableId, int TeaId);

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
            var category = new MenuCategory { Name = "Drinks", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "W1", QRCodeString = "t1-w1", SortOrder = 60 };
            var other = new RestaurantTable { TableNumber = "W2", QRCodeString = "t1-w2", SortOrder = 61 };
            db.RestaurantTables.AddRange(table, other);
            db.SaveChanges();
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Drinks", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(tea);
            db.SaveChanges();
            return new Setup(f, table.Id, other.Id, tea.Id);
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
        public async Task Waiter_TakesOrdersAndAddsRounds()
        {
            var s = Build();
            using var f = s.Factory;
            var waiter = As(f, "waiter");
            Assert.Equal(HttpStatusCode.OK, (await waiter.GetAsync("/api/tables")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await waiter.GetAsync("/api/menuitems")).StatusCode);

            var placed = await waiter.PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = s.TeaId, quantity = 2 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var body = await Read(placed);
            var id = body.GetProperty("orderId").GetInt32();
            var version = body.GetProperty("version").GetInt32();

            var round = await waiter.PostAsync($"/api/orders/{id}/add-items", Json(new { items = new[] { new { menuItemId = s.TeaId, quantity = 1 } }, version }));
            Assert.Equal(HttpStatusCode.OK, round.StatusCode);
            var moved = await waiter.PostAsync($"/api/orders/{id}/move", Json(new { tableId = s.OtherTableId, version = version + 1 }));
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        }

        [Fact]
        public async Task Waiter_CannotBillCancelMergeOrSplit()
        {
            var s = Build();
            using var f = s.Factory;
            var placed = await Read(await As(f, "cashier").PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = s.TeaId, quantity = 2 } }
            })));
            var id = placed.GetProperty("orderId").GetInt32();
            var version = placed.GetProperty("version").GetInt32();
            var waiter = As(f, "waiter");

            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/orders/{id}/bill", Json(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/orders/{id}/cancel", Json(new { reason = "test", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/orders/{id}/merge", Json(new { fromOrderId = id, version, fromVersion = version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/orders/{id}/split", Json(new { lines = Array.Empty<object>(), version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PutAsync($"/api/orders/{id}/status", Json(new { status = "Preparing", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync("/api/staff")).StatusCode);
        }

        [Fact]
        public async Task Owner_CanAddAWaiter()
        {
            var s = Build();
            using var f = s.Factory;
            var response = await As(f, "admin").PostAsync("/api/staff",
                Json(new { email = "ravi.waiter@test.local", displayName = "Ravi", role = "waiter", password = "Waiter#2026x", isDevice = false }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }
}
