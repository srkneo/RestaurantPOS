using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using POSSystem.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantPOS.Tests
{
    // One Postgres container for all tests in this class; each test gets its own database.
    public class PostgresFixture : IAsyncLifetime
    {
        public PostgreSqlContainer? Container { get; private set; }
        public string? DockerError { get; private set; }

        public async Task InitializeAsync()
        {
            try
            {
                Container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
                await Container.StartAsync();
            }
            catch (Exception ex)
            {
                DockerError = ex.Message;
                Container = null;
            }
        }

        public async Task DisposeAsync()
        {
            if (Container != null) await Container.DisposeAsync();
        }
    }

    public class PostgresFlowTests : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly PostgresFixture _pg;
        private readonly WebApplicationFactory<Program> _factory;

        public PostgresFlowTests(PostgresFixture pg, WebApplicationFactory<Program> factory)
        {
            _pg = pg;
            _factory = factory;
        }

        // Without Docker the tests pass locally but fail in CI, where Docker is always required.
        private bool DockerMissing()
        {
            if (_pg.Container != null) return false;
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Docker is required in CI: " + _pg.DockerError);
            return true;
        }

        private WebApplicationFactory<Program> CreateFactory()
        {
            var csb = new NpgsqlConnectionStringBuilder(_pg.Container!.GetConnectionString())
            {
                Database = "pos_" + Guid.NewGuid().ToString("N")
            };
            var cs = csb.ConnectionString;
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
        public async Task Migrations_ApplyToEmptyDatabase_AndModelHasNoMissingMigration()
        {
            if (DockerMissing()) return;
            using var factory = CreateFactory();
            var client = factory.CreateClient();

            var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges(), "The model has changes that are not in a migration. Run: dotnet ef migrations add <Name>");
            Assert.True(await db.MenuItems.IgnoreQueryFilters().AnyAsync());
        }

        [Fact]
        public async Task Admin_CanSignIn_AndPlaceOrder_OnRealPostgres()
        {
            if (DockerMissing()) return;
            using var factory = CreateFactory();
            var client = factory.CreateClient();
            var config = factory.Services.GetRequiredService<IConfiguration>();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = config["Admin:Email"], password = config["Admin:Password"] });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            using var menu = JsonDocument.Parse(await client.GetStringAsync("/api/menuitems"));
            var menuItemId = menu.RootElement[0].GetProperty("id").GetInt32();
            using var tables = JsonDocument.Parse(await client.GetStringAsync("/api/tables"));
            var tableId = tables.RootElement[0].GetProperty("id").GetInt32();

            var place = await client.PostAsJsonAsync("/api/orders", new
            {
                table = tableId,
                items = new[] { new { menuItemId, quantity = 2 } },
                clientOrderId = Guid.NewGuid()
            });
            Assert.Equal(HttpStatusCode.OK, place.StatusCode);

            using var orders = JsonDocument.Parse(await client.GetStringAsync("/api/orders"));
            Assert.Equal(1, orders.RootElement.GetArrayLength());
        }
    }
}
