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
using Xunit;

namespace RestaurantPOS.Tests
{
    // Demo data: only the platform owner, only for an empty restaurant, and the reports then show real numbers.
    public class DemoDataTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DemoDataTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
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
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant, string outlet = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            client.DefaultRequestHeaders.Add("X-Test-Outlet", outlet);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        [Fact]
        public async Task DemoData_FillsAnEmptyRestaurant_Once()
        {
            using var f = Build();
            var platform = As(f, "platform", "0");
            var created = await platform.PostAsync("/api/platform/tenants", Json(new
            {
                restaurantName = "Demo Thali", outletName = "Main", ownerName = "Demo Owner", ownerEmail = "demo.owner@test.local", ownerPassword = "Owner#2026x"
            }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var tenantId = (await Read(created)).GetProperty("id").GetInt32();

            var filled = await platform.PostAsync($"/api/platform/tenants/{tenantId}/demo", Json(new { }));
            Assert.Equal(HttpStatusCode.OK, filled.StatusCode);
            Assert.Equal(9, (await Read(filled)).GetProperty("items").GetInt32());

            int outletId;
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                outletId = db.Outlets.IgnoreQueryFilters().Where(o => o.TenantId == tenantId).Select(o => o.Id).First();
            }
            var manager = As(f, "manager", tenantId.ToString(), outletId.ToString());
            Assert.Contains("Gujarati Thali", await manager.GetStringAsync("/api/menuitems"));
            var report = await Read(await manager.GetAsync("/api/reports/sales"));
            Assert.Equal(3, report.GetProperty("billCount").GetInt32());
            Assert.True(report.GetProperty("payments").EnumerateArray().Single(p => p.GetProperty("method").GetString() == "Cash").GetProperty("amount").GetDecimal() > 0);

            Assert.Equal(HttpStatusCode.Conflict, (await platform.PostAsync($"/api/platform/tenants/{tenantId}/demo", Json(new { }))).StatusCode);
            // The first restaurant already has its own data.
            Assert.Equal(HttpStatusCode.NotFound, (await platform.PostAsync("/api/platform/tenants/99999/demo", Json(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "admin", tenantId.ToString(), outletId.ToString())
                .PostAsync($"/api/platform/tenants/{tenantId}/demo", Json(new { }))).StatusCode);
        }
    }
}
