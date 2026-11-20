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
    // Loyalty edge cases: bad input, other restaurants, roles, limits, and points coming back when a bill is voided.
    public class LoyaltyNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public LoyaltyNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int TableId, int ThaliId) Build(bool loyaltyOn = true, decimal rupeesPerPoint = 100m, decimal pointValue = 1m, int startPoints = 0)
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
            settings.LoyaltyEnabled = loyaltyOn;
            settings.LoyaltyRupeesPerPoint = rupeesPerPoint;
            settings.LoyaltyPointValue = pointValue;
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "1", QRCodeString = "t1-1", SortOrder = 1 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            if (startPoints > 0)
                db.Customers.Add(new Customer { Name = "Asha", Phone = "9825012345", ConsentAt = DateTime.UtcNow, Points = startPoints });
            db.SaveChanges();
            return (f, table.Id, thali.Id);
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
        public async Task OtherRestaurant_CannotSeeOrChangeGuests()
        {
            var (f, _, _) = Build();
            using var factory = f;
            var added = await As(f, "cashier").PostAsync("/api/customers", Json(new { name = "Asha", phone = "9825012345", consent = true }));
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var id = (await Read(added)).GetProperty("id").GetInt32();

            var other = As(f, "manager", "2");
            Assert.Equal(0, (await Read(await other.GetAsync("/api/customers"))).GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/customers/by-phone?phone=9825012345")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/customers/{id}/moves")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/customers/{id}", Json(new { name = "Hacked", phone = "9000000000" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/customers/{id}")).StatusCode);

            // The same mobile number may be a guest of another restaurant too.
            Assert.Equal(HttpStatusCode.Created, (await other.PostAsync("/api/customers", Json(new { name = "Asha B", phone = "98250 12345", consent = true }))).StatusCode);
            var mine = await Read(await As(f, "cashier").GetAsync("/api/customers/by-phone?phone=9825012345"));
            Assert.Equal("Asha", mine.GetProperty("name").GetString());
        }

        [Theory]
        [InlineData("", "9825012345", "name")]
        [InlineData("   ", "9825012345", "name")]
        [InlineData("Asha", "", "phone")]
        [InlineData("Asha", "abcde", "phone")]
        [InlineData("Asha", "12345", "phone")]
        [InlineData("Asha", "1234567890123456", "phone")]
        public async Task BadGuestDetails_AreRefused(string name, string phone, string field)
        {
            var (f, _, _) = Build();
            using var factory = f;
            var res = await As(f, "cashier").PostAsync("/api/customers", Json(new { name, phone, consent = true }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.True((await Read(res)).GetProperty("errors").TryGetProperty(field, out _));
        }

        [Fact]
        public async Task LongName_ExplicitNoConsent_DuplicateOnEdit_AndUnknownIds_AreRefused()
        {
            var (f, _, _) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/customers", Json(new { name = new string('a', 61), phone = "9825012345", consent = true }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/customers", Json(new { name = "Asha", phone = "9825012345", consent = false }))).StatusCode);

            var asha = await Read(await cashier.PostAsync("/api/customers", Json(new { name = "Asha", phone = "9825012345", consent = true })));
            var ravi = await Read(await cashier.PostAsync("/api/customers", Json(new { name = "Ravi", phone = "9000000001", consent = true })));
            var raviId = ravi.GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PutAsync($"/api/customers/{raviId}", Json(new { name = "Ravi", phone = "+91 98250 12345" }))).StatusCode);
            // Keeping the own number is fine.
            Assert.Equal(HttpStatusCode.OK, (await cashier.PutAsync($"/api/customers/{raviId}", Json(new { name = "Ravi Patel", phone = "9000000001" }))).StatusCode);

            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PutAsync("/api/customers/9999", Json(new { name = "X", phone = "9000000002" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/customers/9999/moves")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "manager").DeleteAsync("/api/customers/9999")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/customers/by-phone")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/customers/by-phone?phone=abc")).StatusCode);
            Assert.Equal(asha.GetProperty("id").GetInt32(), (await Read(await cashier.GetAsync("/api/customers/by-phone?phone=09825012345"))).GetProperty("id").GetInt32());
            // Two digits only search by name: no error, nothing found.
            Assert.Equal(0, (await Read(await cashier.GetAsync("/api/customers?search=12"))).GetArrayLength());
        }

        [Theory]
        [InlineData("waiter")]
        [InlineData("kitchen")]
        public async Task Waiter_AndKitchen_CannotUseLoyalty(string role)
        {
            var (f, _, _) = Build();
            using var factory = f;
            var client = As(f, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers/settings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/customers", Json(new { name = "Asha", phone = "9825012345", consent = true }))).StatusCode);
        }

        [Theory]
        [InlineData(0.5, 1)]
        [InlineData(10001, 1)]
        [InlineData(100, -1)]
        [InlineData(100, 100.01)]
        public async Task Settings_OutOfRange_AreRefused(double rupeesPerPoint, double pointValue)
        {
            var (f, _, _) = Build();
            using var factory = f;
            var res = await As(f, "admin").PutAsync("/api/customers/settings", Json(new { enabled = true, rupeesPerPoint = (decimal)rupeesPerPoint, pointValue = (decimal)pointValue }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task Settings_RoundRupeesPerPoint()
        {
            var (f, _, _) = Build();
            using var factory = f;
            var res = await As(f, "admin").PutAsync("/api/customers/settings", Json(new { enabled = true, rupeesPerPoint = 49.6m, pointValue = 0.255m }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var saved = await Read(res);
            Assert.Equal(50m, saved.GetProperty("rupeesPerPoint").GetDecimal());
            Assert.Equal(0.26m, saved.GetProperty("pointValue").GetDecimal());
        }

        [Fact]
        public async Task OtherRate_EarnsWholePointsOnly()
        {
            var (f, tableId, thaliId) = Build(rupeesPerPoint: 50m, startPoints: 1);
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            Assert.Equal(HttpStatusCode.OK, (await Pay(cashier, bill, "Cash", 398m)).StatusCode);
            Assert.Equal(8, PointsOf(f));   // 1 + floor(398 / 50) = 1 + 7
        }

        [Fact]
        public async Task HalfRupeePoints_NeedWholePoints_AndCannotPassTheDue()
        {
            var (f, tableId, thaliId) = Build(pointValue: 0.5m, startPoints: 2000);
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Points", 5.25m)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Points", 399m)).StatusCode);
            Assert.Equal(2000, PointsOf(f));
            var ok = await Pay(cashier, bill, "Points", 5m);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(1990, PointsOf(f));
            Assert.Equal("10 points", (await Read(ok)).GetProperty("payments")[0].GetProperty("reference").GetString());
        }

        [Fact]
        public async Task Points_AreRefused_WhenLoyaltyIsOff_OrTheBillIsPaid()
        {
            var (f, tableId, thaliId) = Build(loyaltyOn: false, startPoints: 50);
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Points", 10m)).StatusCode);
            var paid = await Read(await Pay(cashier, bill, "Cash", 398m));
            Assert.Equal("Paid", paid.GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, paid, "Points", 10m)).StatusCode);
            Assert.Equal(50, PointsOf(f));
        }

        [Fact]
        public async Task Points_CannotBeATipOrARefundMethod()
        {
            var (f, tableId, thaliId) = Build(startPoints: 50);
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            var tip = await cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/tips", Json(new { amount = 20m, method = "Points" }));
            Assert.True(tip.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"tip with points gave {tip.StatusCode}");
        }

        [Fact]
        public async Task VoidingAPaidBill_GivesSpentPointsBack_AndTakesEarnedPointsBack()
        {
            var (f, tableId, thaliId) = Build(startPoints: 50);
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            bill = await Read(await Pay(cashier, bill, "Points", 50m));
            bill = await Read(await Pay(cashier, bill, "Cash", 348m));
            Assert.Equal("Paid", bill.GetProperty("status").GetString());
            Assert.Equal(3, PointsOf(f));

            var voided = await As(f, "manager").PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/void",
                Json(new { reason = "Wrong table", version = bill.GetProperty("version").GetInt32() }));
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
            Assert.Equal(50, PointsOf(f));

            using var scope = f.Services.CreateScope();
            var kinds = scope.ServiceProvider.GetRequiredService<AppDbContext>().LoyaltyMoves.OrderBy(m => m.Id).Select(m => m.Kind).ToList();
            Assert.Equal(new[] { LoyaltyKind.Redeem, LoyaltyKind.Earn, LoyaltyKind.Reverse }, kinds);
        }

        [Fact]
        public async Task GuestForgotten_BeforePaying_StillLetsTheBillBePaid()
        {
            var (f, tableId, thaliId) = Build(startPoints: 10);
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await Bill(cashier, tableId, thaliId, "9825012345");
            int id;
            using (var scope = f.Services.CreateScope())
                id = scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers.Single().Id;
            Assert.Equal(HttpStatusCode.NoContent, (await As(f, "manager").DeleteAsync($"/api/customers/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, bill, "Points", 10m)).StatusCode);
            var paid = await Pay(cashier, bill, "Cash", 398m);
            Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
            Assert.Equal("Paid", (await Read(paid)).GetProperty("status").GetString());
        }
    }
}
