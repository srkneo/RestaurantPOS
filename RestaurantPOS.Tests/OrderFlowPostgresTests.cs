using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Real Postgres: tokens under load, and the one-time fixes for data from before Batch B.
    public class OrderFlowPostgresTests : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly PostgresFixture _pg;
        private readonly WebApplicationFactory<Program> _factory;

        public OrderFlowPostgresTests(PostgresFixture pg, WebApplicationFactory<Program> factory)
        {
            _pg = pg;
            _factory = factory;
        }

        private bool DockerMissing()
        {
            if (_pg.Container != null) return false;
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Docker is required in CI: " + _pg.DockerError);
            return true;
        }

        private WebApplicationFactory<Program> CreateFactory()
        {
            var cs = new NpgsqlConnectionStringBuilder(_pg.Container!.GetConnectionString()) { Database = "flow_" + Guid.NewGuid().ToString("N") }.ConnectionString;
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Admin:Email", "admin@test.local");
                builder.UseSetting("Admin:Password", "Test-Passw0rd!");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));
                });
            });
        }

        [Fact]
        public async Task TenOrdersAtOnce_GetTenDifferentTokens()
        {
            if (DockerMissing()) return;
            using var factory = CreateFactory();
            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Test-Passw0rd!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            using var menu = JsonDocument.Parse(await client.GetStringAsync("/api/menuitems"));
            var menuItemId = menu.RootElement[0].GetProperty("id").GetInt32();

            var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
                client.PostAsJsonAsync("/api/orders", new { orderType = "Takeaway", customerName = "Load test", items = new[] { new { menuItemId, quantity = 1 } } })));

            var tokens = await Task.WhenAll(responses.Select(async r =>
            {
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("tokenNumber").GetInt32();
            }));
            Assert.Equal(Enumerable.Range(1, 10), tokens.OrderBy(t => t));
        }

        [Fact]
        public async Task OldOrdersGetABusinessDate_AndOldItemsGetACategory()
        {
            if (DockerMissing()) return;
            using var factory = CreateFactory();
            factory.CreateClient(); // starts the app: migrations and seed data

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""KitchenOrders"" (""TableId"", ""Status"", ""TokenNumber"", ""CreatedAt"", ""BusinessDate"")
                VALUES ((SELECT MIN(""Id"") FROM ""RestaurantTables""), 'Completed', 7, TIMESTAMPTZ '2026-11-14 22:00:00+00', DATE '0001-01-01');
                INSERT INTO ""MenuItems"" (""Name"", ""Price"", ""Category"") VALUES ('Old Samosa', 15, 'Old Snacks');");

            Assert.Equal(1, SeedData.FixBusinessDates(db));
            SeedData.LinkCategories(db);

            var order = await db.KitchenOrders.IgnoreQueryFilters().SingleAsync(o => o.TokenNumber == 7);
            Assert.Equal(new DateOnly(2026, 11, 14), order.BusinessDate); // 3:30 am India time counts for the day before
            var item = await db.MenuItems.IgnoreQueryFilters().SingleAsync(m => m.Name == "Old Samosa");
            Assert.NotNull(item.CategoryId);
            Assert.Equal("Old Snacks", (await db.MenuCategories.IgnoreQueryFilters().SingleAsync(c => c.Id == item.CategoryId)).Name);
        }
    }
}
