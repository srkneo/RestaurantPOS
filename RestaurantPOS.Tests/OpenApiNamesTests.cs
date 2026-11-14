using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class OpenApiNamesTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public OpenApiNamesTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task OpenApiDocument_HasNamedEndpoints()
        {
            var dbName = Guid.NewGuid().ToString();
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Admin:Email", "admin@test.local");
                builder.UseSetting("Admin:Password", "Test-Passw0rd!");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            }).CreateClient();

            var response = await client.GetAsync("/openapi/v1.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            foreach (var name in new[] { "GetMenuItems", "GetTables", "GetOrders", "CreateOrder", "UpdateOrderStatus" })
            {
                Assert.Contains("\"operationId\": \"" + name + "\"", json.Replace("\":\"", "\": \""));
            }
            Assert.Contains("Place a new order", json);
        }
    }
}
