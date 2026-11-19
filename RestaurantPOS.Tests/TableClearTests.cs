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
    // After payment a dine-in table shows Paid until staff tap Customer left; a new order there is a new token.
    public class TableClearTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TableClearTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private (WebApplicationFactory<Program> Factory, int TableId, int TeaId) Build()
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
            var category = new MenuCategory { Name = "Drinks", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "C1", QRCodeString = "t1-c1", SortOrder = 100 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var tea = new MenuItem { Name = "Masala Tea", Price = 20m, Category = "Drinks", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(tea);
            db.SaveChanges();
            return (f, table.Id, tea.Id);
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
        public async Task PaidTable_StaysPaid_UntilCustomerLeft()
        {
            var (f, tableId, teaId) = Build();
            using var _ = f;
            var cashier = As(f, "cashier");
            var waiter = As(f, "waiter");
            async Task<JsonElement> Board() => (await Read(await cashier.GetAsync("/api/tables"))).EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == tableId);
            async Task<int> Place() => (await Read(await cashier.PostAsync("/api/orders", Json(new
            {
                orderType = "DineIn", table = tableId, clientOrderId = Guid.NewGuid().ToString(),
                items = new[] { new { menuItemId = teaId, quantity = 2 } }
            })))).GetProperty("orderId").GetInt32();
            async Task Pay(int orderId)
            {
                var bill = await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { })));
                var paid = await cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments",
                    Json(new { method = "cash", amount = bill.GetProperty("total").GetDecimal(), version = bill.GetProperty("version").GetInt32() }));
                Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
            }

            var first = await Place();
            // Still open: Customer left is refused.
            Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync($"/api/tables/{tableId}/clear", null)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/orders/{first}/status", Json(new { status = "Ready" }))).StatusCode);
            await Pay(first);
            var board = await Board();
            Assert.Equal("Paid", board.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, board.GetProperty("orderId").ValueKind);

            // They order again: a new token on the same table, Occupied until it is paid.
            var second = await Place();
            Assert.NotEqual(first, second);
            Assert.Equal("Occupied", (await Board()).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await waiter.PostAsync($"/api/tables/{tableId}/clear", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/orders/{second}/status", Json(new { status = "Ready" }))).StatusCode);
            await Pay(second);
            Assert.Equal("Paid", (await Board()).GetProperty("status").GetString());

            Assert.Equal(HttpStatusCode.OK, (await waiter.PostAsync($"/api/tables/{tableId}/clear", null)).StatusCode);
            Assert.Equal("Free", (await Board()).GetProperty("status").GetString());
        }
    }
}
