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
    // Bakery / simple counter: dine-in needs no table, takeaway needs no name; other outlet types still need them.
    public class SimpleCounterTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SimpleCounterTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> F, int ItemId) Build()
        {
            var dbName = Guid.NewGuid().ToString();
            var f = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
            }));
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new MenuItem { Name = "Black Forest Pastry", Price = 80m, Category = "Cakes", TaxRate = 5m };
            db.MenuItems.Add(item);
            db.SaveChanges();
            return (f, item.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static Task<HttpResponseMessage> Order(HttpClient client, string type, int itemId) =>
            client.PostAsync("/api/orders", Json(new { orderType = type, items = new[] { new { menuItemId = itemId, quantity = 2 } }, clientOrderId = Guid.NewGuid() }));

        [Fact]
        public async Task A_bakery_takes_dine_in_and_takeaway_orders_without_table_or_name_and_bills_them()
        {
            var (f, itemId) = Build();
            using var _ = f;
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/admin/settings/preset", Json(new { preset = "bakery" }))).StatusCode);
            var settings = JsonDocument.Parse(await (await As(f, "cashier").GetAsync("/api/store/settings")).Content.ReadAsStringAsync()).RootElement;
            Assert.True(settings.GetProperty("simpleCounter").GetBoolean());

            var cashier = As(f, "cashier");
            var dineIn = await Order(cashier, "DineIn", itemId);
            Assert.Equal(HttpStatusCode.OK, dineIn.StatusCode);
            var takeaway = await Order(cashier, "Takeaway", itemId);
            Assert.Equal(HttpStatusCode.OK, takeaway.StatusCode);

            var orderId = JsonDocument.Parse(await dineIn.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetInt32();
            var bill = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            Assert.Contains("Dine-in", await bill.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task A_restaurant_with_tables_still_needs_a_table_for_dine_in()
        {
            var (f, itemId) = Build();
            using var _ = f;
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/admin/settings/preset", Json(new { preset = "dine-in" }))).StatusCode);
            var dineIn = await Order(As(f, "cashier"), "DineIn", itemId);
            Assert.Equal(HttpStatusCode.BadRequest, dineIn.StatusCode);
        }
    }
}
