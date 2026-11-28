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
using POSSystem.Loyalty;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Spending points: a guest pays part of a bill with points; the money part earns new points; a void gives the points back.
    public class LoyaltyRedeemTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public LoyaltyRedeemTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
            settings.LoyaltyEnabled = true;
            outlet.Settings = settings.ToJson();
            outlet.BusinessDayStartHour = TestBusinessDay.SafeStartHour(outlet.TimeZone, DateTime.UtcNow);
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "1", QRCodeString = "t1-1", SortOrder = 1 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.Customers.Add(new Customer { Name = "Asha", Phone = "9825012345", ConsentAt = DateTime.UtcNow, Points = 50 });
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

        // Two thalis (398) for the table, billed but not paid.
        private static async Task<JsonElement> Bill(HttpClient cashier, int tableId, int thaliId, string? phone)
        {
            var placed = await cashier.PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), customerPhone = phone, items = new[] { new { menuItemId = thaliId, quantity = 2 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();
            var made = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
            return await Read(made);
        }

        private static Task<HttpResponseMessage> Pay(HttpClient cashier, JsonElement bill, string method, decimal amount) =>
            cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments",
                Json(new { method, amount, version = bill.GetProperty("version").GetInt32() }));

        private static int PointsOf(WebApplicationFactory<Program> f)
        {
            using var scope = f.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers.Single().Points;
        }

        [Fact]
        public async Task Points_PayPartOfABill_AndTheRestEarnsPoints()
        {
            var (f, tableId, thaliId) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "+91 98250 12345");

            // More points than the guest has, or part of a point: refused.
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Points", 60m)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Points", 10.5m)).StatusCode);
            Assert.Equal(50, PointsOf(f));

            var used = await Pay(cashier, bill, "Points", 50m);
            Assert.Equal(HttpStatusCode.OK, used.StatusCode);
            bill = await Read(used);
            Assert.Equal(348m, bill.GetProperty("amountDue").GetDecimal());
            Assert.Equal(0, PointsOf(f));

            var cash = await Pay(cashier, bill, "Cash", 348m);
            Assert.Equal(HttpStatusCode.OK, cash.StatusCode);
            Assert.Equal("Paid", (await Read(cash)).GetProperty("status").GetString());
            Assert.Equal(3, PointsOf(f));   // 348 rupees of money at 100 rupees a point

            var day = await Read(await As(f, "manager").GetAsync("/api/reports/day"));
            var payments = day.GetProperty("payments").EnumerateArray().ToList();
            Assert.Equal(50m, payments.Single(p => p.GetProperty("method").GetString() == "Points").GetProperty("amount").GetDecimal());
        }

        [Fact]
        public async Task Points_NeedALoyaltyGuestOnTheBill()
        {
            var (f, tableId, thaliId) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, null);
            var refused = await Pay(cashier, bill, "Points", 10m);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(50, PointsOf(f));
        }

        [Fact]
        public async Task Reverse_GivesSpentPointsBack_Once()
        {
            var (f, tableId, thaliId) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            Assert.Equal(HttpStatusCode.OK, (await Pay(cashier, bill, "Points", 20m)).StatusCode);
            Assert.Equal(30, PointsOf(f));

            var billId = bill.GetProperty("id").GetInt32();
            for (var i = 0; i < 2; i++)
            {
                using var scope = f.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var saved = db.Bills.Include(b => b.Payments).Single(b => b.Id == billId);
                await LoyaltyRules.ReverseAsync(db, saved);
                await db.SaveChangesAsync();
            }
            Assert.Equal(50, PointsOf(f));
            using var check = f.Services.CreateScope();
            var moves = check.ServiceProvider.GetRequiredService<AppDbContext>().LoyaltyMoves.OrderBy(m => m.Id).ToList();
            Assert.Equal(new[] { LoyaltyKind.Redeem, LoyaltyKind.Reverse }, moves.Select(m => m.Kind).ToArray());
            Assert.Equal(20, moves[1].Points);
        }
    }
}
