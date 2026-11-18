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
    // The owner can cap the discount a cashier gives on their own; above it a manager PIN is needed. No cap = as before.
    public class DiscountLimitTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DiscountLimitTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int ThaliId);

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
            var category = new MenuCategory { Name = "Meals", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "L1", QRCodeString = "t1-l1", SortOrder = 110 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Thali", Price = 200m, Category = "Meals", CategoryId = category.Id, TaxRate = 5m };
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

        private static async Task<int> Order(Setup s)
        {
            var placed = await As(s.Factory, "cashier").PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = s.ThaliId, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            return (await Read(placed)).GetProperty("orderId").GetInt32();
        }

        private static async Task ManagerWithPin(WebApplicationFactory<Program> f, string pin)
        {
            var staff = await As(f, "admin").PostAsync("/api/staff",
                Json(new { email = "meena.manager@test.local", displayName = "Meena", role = "manager", password = "Staff#2026x", isDevice = false }));
            Assert.Equal(HttpStatusCode.Created, staff.StatusCode);
            var id = (await Read(staff)).GetProperty("id").GetString();
            Assert.True((await As(f, "admin").PutAsync($"/api/staff/{id}/pin", Json(new { pin }))).IsSuccessStatusCode);
        }

        [Fact]
        public async Task AboveTheLimit_ACashierNeedsAManagerPin()
        {
            var s = Build();
            using var f = s.Factory;
            await ManagerWithPin(f, "4321");
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync("/api/admin/discount-limit", Json(new { cashierMaxDiscountPercent = 10m }))).StatusCode);
            var cashier = As(f, "cashier");

            var small = await cashier.PostAsync($"/api/orders/{await Order(s)}/bill", Json(new { discountPercent = 10m }));
            Assert.Equal(HttpStatusCode.Created, small.StatusCode);

            var orderId = await Order(s);
            var noPin = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountPercent = 20m }));
            Assert.Equal(HttpStatusCode.Forbidden, noPin.StatusCode);
            Assert.Equal("pin-required", (await Read(noPin)).GetProperty("code").GetString());
            var wrong = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountPercent = 20m, approverPin = "1111" }));
            Assert.Equal("wrong-pin", (await Read(wrong)).GetProperty("code").GetString());
            var approved = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountPercent = 20m, approverPin = "4321" }));
            Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
            Assert.Equal(40m, (await Read(approved)).GetProperty("discount").GetDecimal());

            // Rupees count too: 50 off 200 is 25%.
            var rupees = await cashier.PostAsync($"/api/orders/{await Order(s)}/bill", Json(new { discountAmount = 50m }));
            Assert.Equal(HttpStatusCode.Forbidden, rupees.StatusCode);
            // A manager gives any discount without a PIN.
            Assert.Equal(HttpStatusCode.Created, (await As(f, "manager").PostAsync($"/api/orders/{await Order(s)}/bill", Json(new { discountPercent = 30m }))).StatusCode);
        }

        [Fact]
        public async Task NoLimit_KeepsTheOldWay_AndTheLimitIsChecked()
        {
            var s = Build();
            using var f = s.Factory;
            Assert.Equal(JsonValueKind.Null, (await Read(await As(f, "admin").GetAsync("/api/admin/discount-limit"))).GetProperty("cashierMaxDiscountPercent").ValueKind);
            Assert.Equal(HttpStatusCode.Created, (await As(f, "cashier").PostAsync($"/api/orders/{await Order(s)}/bill", Json(new { discountPercent = 50m }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "admin").PutAsync("/api/admin/discount-limit", Json(new { cashierMaxDiscountPercent = 120m }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/discount-limit", Json(new { cashierMaxDiscountPercent = 5m }))).StatusCode);
        }
    }
}
