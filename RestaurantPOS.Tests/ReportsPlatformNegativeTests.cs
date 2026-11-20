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
    // Reports and exports: bad dates, too long ranges, roles and other restaurants. Platform pages: only the platform owner.
    public class ReportsPlatformNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ReportsPlatformNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Thali) Build()
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
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return (f, thali.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static readonly string[] Reports = { "/api/reports/sales", "/api/reports/sales.csv", "/api/reports/gst.csv", "/api/reports/tally.xml" };

        [Fact]
        public async Task Reports_BadDates_AndLongRanges_AreRefused()
        {
            var (f, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            foreach (var report in Reports)
            {
                foreach (var query in new[] { "?from=2026-13-01", "?from=20-11-2026", "?to=tomorrow", "?from=2026-11-20&to=2026-11-01", "?from=2026-01-01&to=2026-12-31" })
                    Assert.True((await manager.GetAsync(report + query)).StatusCode == HttpStatusCode.BadRequest, $"{report}{query} was not refused");
                Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(report + "?from=2026-11-01&to=2026-11-30")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(report)).StatusCode);
            }
        }

        [Fact]
        public async Task Reports_AreForManagers_AndShowOnlyThisRestaurant()
        {
            var (f, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            using var placedDoc = JsonDocument.Parse(await (await cashier.PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = thali, quantity = 1 } }
            }))).Content.ReadAsStringAsync());
            var bill = await cashier.PostAsync($"/api/orders/{placedDoc.RootElement.GetProperty("orderId").GetInt32()}/bill", Json(new { }));
            using var billDoc = JsonDocument.Parse(await bill.Content.ReadAsStringAsync());
            var invoice = billDoc.RootElement.GetProperty("invoiceNumber").GetString()!;
            // The sales CSV is a summary: one bill counted for this restaurant.
            Assert.Contains("Total,Bills,1,", await (await As(f, "manager").GetAsync("/api/reports/sales.csv")).Content.ReadAsStringAsync());

            foreach (var role in new[] { "cashier", "waiter", "kitchen" })
                foreach (var report in Reports.Append("/api/reports/day"))
                    Assert.True((await As(f, role).GetAsync(report)).StatusCode == HttpStatusCode.Forbidden, $"{role} could open {report}");

            var other = As(f, "admin", "2");
            foreach (var report in Reports.Append("/api/reports/day"))
                Assert.DoesNotContain(invoice, await (await other.GetAsync(report)).Content.ReadAsStringAsync());
            Assert.DoesNotContain("Total,Bills,1,", await (await other.GetAsync("/api/reports/sales.csv")).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ShopOwner_CannotUseThePlatform()
        {
            var (f, _) = Build();
            using var factory = f;
            foreach (var role in new[] { "admin", "manager", "cashier" })
            {
                var c = As(f, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/platform/tenants")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/platform/tenants/1/disable", Json(new { }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/platform/tenants/1/enable", Json(new { }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/platform/tenants/1/payments", Json(new { plan = "yearly", amount = 0 }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync("/api/platform/tenants/1/subscription", Json(new { plan = "yearly" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/platform/licences")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/platform/licences", Json(new { }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/platform/support")).StatusCode);
            }
            Assert.Equal(HttpStatusCode.OK, (await As(f, "platform").GetAsync("/api/platform/tenants")).StatusCode);

            using var scope = f.Services.CreateScope();
            Assert.True(scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.Single(t => t.Id == 1).IsActive);
        }

        [Fact]
        public async Task LicenceAndSubscription_AreForTheOwner()
        {
            var (f, _) = Build();
            using var factory = f;
            foreach (var role in new[] { "manager", "cashier", "waiter", "kitchen" })
            {
                var c = As(f, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/licence/activate", Json(new { key = "x" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/licence/backups/run", Json(new { }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/licence/updates/run", Json(new { }))).StatusCode);
            }
            foreach (var role in new[] { "cashier", "waiter", "kitchen" })
                Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).GetAsync("/api/subscription")).StatusCode);
        }
    }
}
