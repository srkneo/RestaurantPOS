using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class ApiExplorerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApiExplorerTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private HttpClient CreateClient(string environment)
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("Admin:Email", "admin@test.local");
                builder.UseSetting("Admin:Password", "Test-Passw0rd!");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            }).CreateClient();
        }

        [Fact]
        public async Task OpenApiDocument_IsPublicInDevelopment_AndListsOrders()
        {
            var client = CreateClient("Development");

            var response = await client.GetAsync("/openapi/v1.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("/api/orders", json);
        }

        [Fact]
        public async Task ApiExplorerPage_IsPublicInDevelopment()
        {
            var client = CreateClient("Development");

            var a = await client.GetAsync("/scalar/v1");
            var b = await client.GetAsync("/scalar");

            Assert.True(a.StatusCode == HttpStatusCode.OK || b.StatusCode == HttpStatusCode.OK,
                $"/scalar/v1 gave {(int)a.StatusCode}, /scalar gave {(int)b.StatusCode}");
        }

        [Fact]
        public async Task OpenApiDocument_IsNotServedInProduction()
        {
            var client = CreateClient("Production");

            var response = await client.GetAsync("/openapi/v1.json");

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
