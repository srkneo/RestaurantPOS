using System;
using System.Net;
using System.Net.Http;
using System.Text;
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
    public class AuthAndTenantTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public AuthAndTenantTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private WebApplicationFactory<Program> Build(bool testAuth)
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    if (testAuth)
                    {
                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                        services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                    }
                });
            });
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static void SeedOrder(WebApplicationFactory<Program> f, int tenantId)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CurrentTenantId = tenantId;
            db.KitchenOrders.Add(new KitchenOrder { Id = 1, TableId = 1, TokenNumber = 101, Status = OrderStatus.Received, BusinessDate = BusinessClock.BusinessDate(DateTime.UtcNow, null, BusinessClock.DefaultDayStartHour) });
            db.SaveChanges();
        }

        private static StringContent Json(string body) => new StringContent(body, Encoding.UTF8, "application/json");

        [Fact]
        public async Task Orders_WithoutLogin_Returns401()
        {
            using var f = Build(testAuth: false);
            var response = await f.CreateClient().GetAsync("/api/orders");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Health_WithoutLogin_Returns200()
        {
            using var f = Build(testAuth: false);
            var response = await f.CreateClient().GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Kitchen_CannotPlaceOrder_Returns403()
        {
            using var f = Build(testAuth: true);
            var response = await As(f, "kitchen", "1").PostAsync("/api/orders", Json("{\"table\":1,\"items\":[{\"menuItemId\":1,\"quantity\":1}]}"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Cashier_CannotUpdateStatus_Returns403()
        {
            using var f = Build(testAuth: true);
            var response = await As(f, "cashier", "1").PutAsync("/api/orders/1/status", Json("{\"status\":\"Ready\"}"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task OtherTenant_CannotSeeOrders()
        {
            using var f = Build(testAuth: true);
            SeedOrder(f, 1);

            var own = await (await As(f, "admin", "1").GetAsync("/api/orders")).Content.ReadAsStringAsync();
            var other = await (await As(f, "admin", "2").GetAsync("/api/orders")).Content.ReadAsStringAsync();

            Assert.Contains("\"tokenNumber\":101", own);
            Assert.Equal("[]", other);
        }

        [Fact]
        public async Task OtherTenant_CannotUpdateOrder()
        {
            using var f = Build(testAuth: true);
            SeedOrder(f, 1);

            await As(f, "manager", "2").PutAsync("/api/orders/1/status", Json("{\"status\":\"Ready\"}"));

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CurrentTenantId = 1;
            var order = await db.KitchenOrders.FirstAsync();
            Assert.Equal(OrderStatus.Received, order.Status);
        }
    }
}
