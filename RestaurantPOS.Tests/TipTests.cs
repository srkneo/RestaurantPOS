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
    // Tips go to the waiter, stay out of the bill and the GST, and cash tips are expected in the till.
    public class TipTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TipTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int ThaliId);

        // No-GST restaurant with a table and a Thali at 100.
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
            var category = new MenuCategory { Name = "Meals", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "T9", QRCodeString = "t1-t9", SortOrder = 70 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Thali", Price = 100m, Category = "Meals", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return new Setup(f, table.Id, thali.Id);
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

        private static async Task<JsonElement> BillOf(Setup s)
        {
            var cashier = As(s.Factory, "cashier");
            var placed = await Read(await cashier.PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = s.ThaliId, quantity = 1 } }
            })));
            var bill = await cashier.PostAsync($"/api/orders/{placed.GetProperty("orderId").GetInt32()}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            return await Read(bill);
        }

        [Fact]
        public async Task Tip_StaysOutOfTheBill_AndIsCountedPerWaiter()
        {
            var s = Build();
            using var f = s.Factory;
            var bill = await BillOf(s);
            var billId = bill.GetProperty("id").GetInt32();
            var cashier = As(f, "cashier");

            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Upi", amount = 50m, waiterName = "Ravi" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Card", amount = 20m, waiterName = "Ravi" }))).StatusCode);
            var shared = await Read(await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Upi", amount = 10m })));
            Assert.Equal("Shared", shared.GetProperty("waiterName").GetString());

            Assert.Equal(100m, (await Read(await cashier.GetAsync($"/api/bills/{billId}"))).GetProperty("total").GetDecimal());
            var report = await Read(await cashier.GetAsync("/api/tips"));
            Assert.Equal(80m, report.GetProperty("total").GetDecimal());
            var ravi = report.GetProperty("waiters").EnumerateArray().Single(w => w.GetProperty("waiterName").GetString() == "Ravi");
            Assert.Equal(70m, ravi.GetProperty("amount").GetDecimal());
            Assert.Equal(2, ravi.GetProperty("count").GetInt32());
        }

        [Fact]
        public async Task CashTip_IsExpectedInTheOpenShift()
        {
            var s = Build();
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 1000m }))).StatusCode);
            var billId = (await BillOf(s)).GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Cash", amount = 30m, waiterName = "Ravi" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Upi", amount = 20m, waiterName = "Ravi" }))).StatusCode);

            var shift = (await Read(await cashier.GetAsync("/api/shifts/current"))).GetProperty("shift");
            Assert.Equal(30m, shift.GetProperty("cashIn").GetDecimal());
            Assert.Equal(1030m, shift.GetProperty("expectedCash").GetDecimal());
        }

        [Fact]
        public async Task Tips_AreChecked_AndOnlyTheTillRecordsThem()
        {
            var s = Build();
            using var f = s.Factory;
            var billId = (await BillOf(s)).GetProperty("id").GetInt32();
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Cash", amount = 0m }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Cash", amount = 200000m }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Cheque", amount = 10m }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/bills/99999/tips", Json(new { method = "Cash", amount = 10m }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "waiter").PostAsync($"/api/bills/{billId}/tips", Json(new { method = "Cash", amount = 10m }))).StatusCode);
        }
    }
}
