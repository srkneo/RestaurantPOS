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
    // Small change: a guest 3 rupees short pays 395 of 398 in cash; the cashier waives 3 and the bill is paid.
    public class WaiveTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public WaiveTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int TableId, int ThaliId) Build()
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
            outlet.BusinessDayStartHour = TestBusinessDay.SafeStartHour(outlet.TimeZone, DateTime.UtcNow);
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "4", QRCodeString = "t1-4", SortOrder = 4 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return (f, table.Id, thali.Id);
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

        private static async Task<JsonElement> Bill(HttpClient cashier, int tableId, int thaliId)
        {
            var placed = await cashier.PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = thaliId, quantity = 2 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();
            var bill = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            return await Read(bill);
        }

        private static Task<HttpResponseMessage> Pay(HttpClient cashier, JsonElement bill, string method, decimal amount) =>
            cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments",
                Json(new { method, amount, version = bill.GetProperty("version").GetInt32() }));

        [Fact]
        public async Task SmallChange_IsWaivedAfterAPayment_UpToTheLimit()
        {
            var (f, tableId, thaliId) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId);
            Assert.Equal(398m, bill.GetProperty("total").GetDecimal());

            // Nothing paid yet: cannot waive.
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Waived", 3m)).StatusCode);

            var cash = await Pay(cashier, bill, "Cash", 395m);
            Assert.Equal(HttpStatusCode.OK, cash.StatusCode);
            bill = await Read(cash);
            Assert.Equal(3m, bill.GetProperty("amountDue").GetDecimal());

            // Only the whole amount due can be waived.
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Waived", 2m)).StatusCode);
            var waived = await Pay(cashier, bill, "Waived", 3m);
            Assert.Equal(HttpStatusCode.OK, waived.StatusCode);
            bill = await Read(waived);
            Assert.Equal("Paid", bill.GetProperty("status").GetString());
            Assert.Equal(398m, bill.GetProperty("total").GetDecimal());

            var day = await Read(await As(f, "manager").GetAsync("/api/reports/day"));
            var payments = day.GetProperty("payments").EnumerateArray().ToList();
            Assert.Equal(395m, payments.Single(p => p.GetProperty("method").GetString() == "Cash").GetProperty("amount").GetDecimal());
            Assert.Equal(3m, payments.Single(p => p.GetProperty("method").GetString() == "Waived").GetProperty("amount").GetDecimal());
        }

        [Fact]
        public async Task MoreThanTheLimit_IsRefused_AndReportsHideWaivedWhenNone()
        {
            var (f, tableId, thaliId) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId);
            bill = await Read(await Pay(cashier, bill, "Upi", 390m));
            var refused = await Pay(cashier, bill, "Waived", 8m);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("Only up to 5 rupees", await refused.Content.ReadAsStringAsync());

            var day = await Read(await As(f, "manager").GetAsync("/api/reports/day"));
            Assert.DoesNotContain(day.GetProperty("payments").EnumerateArray(), p => p.GetProperty("method").GetString() == "Waived");
        }
    }
}
