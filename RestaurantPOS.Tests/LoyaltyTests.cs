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
    // Loyalty: a guest is added only with consent, earns points on paid bills, and can be forgotten on request.
    public class LoyaltyTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public LoyaltyTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Table1, int Table2, int ThaliId) Build(bool loyaltyOn = true)
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
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var t1 = new RestaurantTable { TableNumber = "1", QRCodeString = "t1-1", SortOrder = 1 };
            var t2 = new RestaurantTable { TableNumber = "2", QRCodeString = "t1-2", SortOrder = 2 };
            db.RestaurantTables.AddRange(t1, t2);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return (f, t1.Id, t2.Id, thali.Id);
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

        // Two thalis (398) for a table, billed and paid in cash.
        private static async Task<JsonElement> PaidBill(HttpClient cashier, int tableId, int thaliId, string phone)
        {
            var placed = await cashier.PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), customerPhone = phone, items = new[] { new { menuItemId = thaliId, quantity = 2 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();
            var made = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, made.StatusCode);
            var bill = await Read(made);
            Assert.Equal(398m, bill.GetProperty("total").GetDecimal());
            var paid = await cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments",
                Json(new { method = "Cash", amount = 398m, version = bill.GetProperty("version").GetInt32() }));
            Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
            bill = await Read(paid);
            Assert.Equal("Paid", bill.GetProperty("status").GetString());
            return bill;
        }

        private static async Task<JsonElement> AddGuest(HttpClient client, string name, string phone)
        {
            var added = await client.PostAsync("/api/customers", Json(new { name, phone, consent = true }));
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            return await Read(added);
        }

        [Fact]
        public async Task Customers_NeedConsent_AndOneEntryPerMobile()
        {
            var (f, _, _, _) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");

            var noConsent = await cashier.PostAsync("/api/customers", Json(new { name = "Asha", phone = "98250 12345" }));
            Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
            Assert.Contains("consent", await noConsent.Content.ReadAsStringAsync());

            var asha = await AddGuest(cashier, "Asha", "98250 12345");
            Assert.Equal("9825012345", asha.GetProperty("phone").GetString());
            Assert.Equal(0, asha.GetProperty("points").GetInt32());

            var again = await cashier.PostAsync("/api/customers", Json(new { name = "Asha P", phone = "+91 98250-12345", consent = true }));
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

            Assert.Single((await Read(await cashier.GetAsync("/api/customers?search=asha"))).EnumerateArray());
            Assert.Single((await Read(await cashier.GetAsync("/api/customers?search=12345"))).EnumerateArray());
            Assert.Empty((await Read(await cashier.GetAsync("/api/customers?search=ravi"))).EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/customers/by-phone?phone=09825012345")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/customers/by-phone?phone=9000000000")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "waiter").GetAsync("/api/customers")).StatusCode);
        }

        [Fact]
        public async Task PaidBill_EarnsPoints_ForAGuestWhoAgreed()
        {
            var (f, t1, t2, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var asha = await AddGuest(cashier, "Asha", "9825012345");

            await PaidBill(cashier, t1, thali, "+91 98250 12345");
            var after = await Read(await cashier.GetAsync("/api/customers/by-phone?phone=9825012345"));
            Assert.Equal(3, after.GetProperty("points").GetInt32());   // 398 rupees at 100 rupees a point
            Assert.Equal(1, after.GetProperty("visits").GetInt32());
            Assert.Equal(398m, after.GetProperty("spent").GetDecimal());

            var moves = (await Read(await cashier.GetAsync($"/api/customers/{asha.GetProperty("id").GetInt32()}/moves"))).EnumerateArray().ToList();
            Assert.Single(moves);
            Assert.Equal("Earn", moves[0].GetProperty("kind").GetString());
            Assert.Equal(3, moves[0].GetProperty("points").GetInt32());

            // A mobile number that never agreed is not stored.
            await PaidBill(cashier, t2, thali, "9000000000");
            Assert.Single((await Read(await cashier.GetAsync("/api/customers"))).EnumerateArray());
        }

        [Fact]
        public async Task NoPoints_WhenLoyaltyIsOff()
        {
            var (f, t1, _, thali) = Build(loyaltyOn: false);
            using var factory = f;
            var cashier = As(f, "cashier");
            await AddGuest(cashier, "Asha", "9825012345");
            await PaidBill(cashier, t1, thali, "9825012345");
            var after = await Read(await cashier.GetAsync("/api/customers/by-phone?phone=9825012345"));
            Assert.Equal(0, after.GetProperty("points").GetInt32());
            Assert.Equal(0, after.GetProperty("visits").GetInt32());
        }

        [Fact]
        public async Task Forget_RemovesGuestAndPointsHistory_ManagersOnly()
        {
            var (f, t1, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var asha = await AddGuest(cashier, "Asha", "9825012345");
            await PaidBill(cashier, t1, thali, "9825012345");
            var id = asha.GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"/api/customers/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await As(f, "manager").DeleteAsync($"/api/customers/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/customers/by-phone?phone=9825012345")).StatusCode);

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, db.LoyaltyMoves.IgnoreQueryFilters().Count());
            Assert.Equal(0, db.Customers.IgnoreQueryFilters().Count());
        }

        [Fact]
        public async Task Settings_AreCheckedAndSavedByManagers()
        {
            var (f, _, _, _) = Build(loyaltyOn: false);
            using var factory = f;
            var manager = As(f, "manager");
            var first = await Read(await manager.GetAsync("/api/customers/settings"));
            Assert.False(first.GetProperty("enabled").GetBoolean());
            Assert.Equal(100m, first.GetProperty("rupeesPerPoint").GetDecimal());
            Assert.Equal(1m, first.GetProperty("pointValue").GetDecimal());

            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsync("/api/customers/settings",
                Json(new { enabled = true, rupeesPerPoint = 0m, pointValue = 1m }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync("/api/customers/settings",
                Json(new { enabled = true, rupeesPerPoint = 50m, pointValue = 0.5m }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync("/api/customers/settings",
                Json(new { enabled = true, rupeesPerPoint = 50m, pointValue = 0.5m }))).StatusCode);

            var saved = await Read(await manager.GetAsync("/api/customers/settings"));
            Assert.True(saved.GetProperty("enabled").GetBoolean());
            Assert.Equal(50m, saved.GetProperty("rupeesPerPoint").GetDecimal());
            Assert.Equal(0.5m, saved.GetProperty("pointValue").GetDecimal());
        }
    }
}
