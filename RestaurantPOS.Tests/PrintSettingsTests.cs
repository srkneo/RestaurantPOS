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
    public class PrintSettingsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public PrintSettingsTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
            }));
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
        public async Task PrintSettings_DefaultTo80mmEnglish_AndFollowBillingSettings()
        {
            using var f = Build();
            var cashier = As(f, "cashier");

            var defaults = await Read(await cashier.GetAsync("/api/print-settings"));
            Assert.Equal(80, defaults.GetProperty("paperWidth").GetInt32());
            Assert.Equal("en", defaults.GetProperty("printLanguage").GetString());
            Assert.False(defaults.GetProperty("upiReady").GetBoolean());

            var admin = As(f, "admin");
            var bad = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", paperWidth = 72, printLanguage = "fr" }));
            var hindi = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", paperWidth = 80, printLanguage = "hi" }));
            Assert.Equal(HttpStatusCode.OK, hindi.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var badBody = await bad.Content.ReadAsStringAsync();
            Assert.Contains("paperwidth", badBody);
            Assert.Contains("printlanguage", badBody);

            var saved = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", upiId = "shop@okaxis", paperWidth = 58, printLanguage = "gu" }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            var now = await Read(await As(f, "kitchen").GetAsync("/api/print-settings"));
            Assert.Equal(58, now.GetProperty("paperWidth").GetInt32());
            Assert.Equal("gu", now.GetProperty("printLanguage").GetString());
            Assert.True(now.GetProperty("upiReady").GetBoolean());
        }

        [Fact]
        public async Task Printing_IsCounted_ForDuplicates()
        {
            using var f = Build();
            int billId;
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = new KitchenOrder { TokenNumber = 1, BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow), OrderType = OrderType.Takeaway };
                db.KitchenOrders.Add(order);
                db.SaveChanges();
                var bill = new Bill { KitchenOrderId = order.Id, InvoiceNumber = "INV/26-27/000001", FinancialYear = 2026, Sequence = 1, Total = 100m };
                db.Bills.Add(bill);
                db.SaveChanges();
                billId = bill.Id;
            }
            var cashier = As(f, "cashier");

            Assert.Equal(1, (await Read(await cashier.PostAsync($"/api/bills/{billId}/printed", null))).GetProperty("printCount").GetInt32());
            Assert.Equal(2, (await Read(await cashier.PostAsync($"/api/bills/{billId}/printed", null))).GetProperty("printCount").GetInt32());
            Assert.Equal(2, (await Read(await cashier.GetAsync($"/api/bills/{billId}"))).GetProperty("printCount").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/bills/999/printed", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync($"/api/bills/{billId}/printed", null)).StatusCode);
        }
    }
}
