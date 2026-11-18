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
    // Merging two tables into one bill, and splitting some items off to a bill of their own.
    public class MergeSplitTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MergeSplitTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int[] Tables, int NaanId, int TeaId);

        // No-GST restaurant with three tables, Butter Naan 40 and Tea 20.
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
            var tables = new[] { "M1", "M2", "M3" }.Select((n, i) => new RestaurantTable { TableNumber = n, QRCodeString = "t1-" + n, SortOrder = 50 + i }).ToList();
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
        public async Task Merge_BringsTheOtherTablesItems_AndClosesThatOrder()
        {
            var s = Build();
            using var f = s.Factory;
            var a = await Place(s, s.Tables[0], (s.NaanId, 2));
            var b = await Place(s, s.Tables[1], (s.TeaId, 1));
            var cashier = As(f, "cashier");

            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync($"/api/orders/{a.Id}/merge",
                Json(new { fromOrderId = b.Id, version = a.Version, fromVersion = b.Version + 3 }))).StatusCode);

            var merged = await cashier.PostAsync($"/api/orders/{a.Id}/merge", Json(new { fromOrderId = b.Id, version = a.Version, fromVersion = b.Version }));
            Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
            var lines = (await Read(merged)).GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal(2, lines.Single(l => l.GetProperty("itemName").GetString() == "Tea").GetProperty("round").GetInt32());

            var other = await Read(await cashier.GetAsync($"/api/orders/{b.Id}"));
            Assert.Equal("Cancelled", other.GetProperty("status").GetString());
            Assert.Contains("Merged into token", other.GetProperty("cancelReason").GetString());
            Assert.Empty(other.GetProperty("items").EnumerateArray());

            var bill = await Read(await cashier.PostAsync($"/api/orders/{a.Id}/bill", Json(new { })));
            Assert.Equal(100m, bill.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task Split_MovesPartOfALine_ToANewOrderWithItsOwnBill()
        {
            var s = Build();
            using var f = s.Factory;
            var a = await Place(s, s.Tables[2], (s.NaanId, 3), (s.TeaId, 1));
            var cashier = As(f, "cashier");
            var order = await Read(await cashier.GetAsync($"/api/orders/{a.Id}"));
            var naanLine = order.GetProperty("items").EnumerateArray().Single(l => l.GetProperty("itemName").GetString() == "Butter Naan").GetProperty("id").GetInt32();
            var teaLine = order.GetProperty("items").EnumerateArray().Single(l => l.GetProperty("itemName").GetString() == "Tea").GetProperty("id").GetInt32();

            var split = await cashier.PostAsync($"/api/orders/{a.Id}/split",
                Json(new { lines = new[] { new { orderItemId = naanLine, quantity = 1 } }, version = a.Version }));
            Assert.Equal(HttpStatusCode.OK, split.StatusCode);
            var newOrder = await Read(split);
            Assert.NotEqual(a.Token, newOrder.GetProperty("tokenNumber").GetInt32());
            Assert.Equal(1, newOrder.GetProperty("items")[0].GetProperty("quantity").GetInt32());

            var left = await Read(await cashier.GetAsync($"/api/orders/{a.Id}"));
            Assert.Equal(2, left.GetProperty("items").EnumerateArray().Single(l => l.GetProperty("itemName").GetString() == "Butter Naan").GetProperty("quantity").GetInt32());

            Assert.Equal(100m, (await Read(await cashier.PostAsync($"/api/orders/{a.Id}/bill", Json(new { })))).GetProperty("total").GetDecimal());
            Assert.Equal(40m, (await Read(await cashier.PostAsync($"/api/orders/{newOrder.GetProperty("id").GetInt32()}/bill", Json(new { })))).GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task Split_KeepsSomethingOnTheOrder_AndChecksQuantities()
        {
            var s = Build();
            using var f = s.Factory;
            var a = await Place(s, s.Tables[0], (s.NaanId, 2));
            var cashier = As(f, "cashier");
            var line = (await Read(await cashier.GetAsync($"/api/orders/{a.Id}"))).GetProperty("items")[0].GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a.Id}/split",
                Json(new { lines = new[] { new { orderItemId = line, quantity = 2 } }, version = a.Version }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a.Id}/split",
                Json(new { lines = new[] { new { orderItemId = line, quantity = 5 } }, version = a.Version }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{a.Id}/split",
                Json(new { lines = new[] { new { orderItemId = 99999, quantity = 1 } }, version = a.Version }))).StatusCode);
        }

        private static async Task<(int Id, int Version, int Token)> Place(Setup s, int tableId, params (int Item, int Quantity)[] lines)
        {
            var placed = await As(s.Factory, "cashier").PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), items = lines.Select(l => new { menuItemId = l.Item, quantity = l.Quantity }).ToArray()
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var body = await Read(placed);
            return (body.GetProperty("orderId").GetInt32(), body.GetProperty("version").GetInt32(), body.GetProperty("tokenNumber").GetInt32());
        }
    }
}
