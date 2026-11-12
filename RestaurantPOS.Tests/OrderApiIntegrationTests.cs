using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using POSSystem.Data;
using POSSystem.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class OrderApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public OrderApiIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task PostOrder_Returns200AndGeneratedTokenNumber()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseInMemoryDatabase(dbName));
                });
            });

            var client = factory.CreateClient();

            // Seed one menu item so CreateOrder can validate the request
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.MenuItems.Add(new MenuItem { Id = 1, Name = "Test Burger", Price = 10, Category = "Main" });
                db.SaveChanges();
            }

            var orderRequest = new
            {
                Table = 1,
                Items = new List<object>
                {
                    new { MenuItemId = 1, Quantity = 2 }
                }
            };

            var jsonContent = JsonConvert.SerializeObject(orderRequest);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            // Act
            var response = await client.PostAsync("/api/orders", content);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var responseOrder = JsonConvert.DeserializeObject<dynamic>(responseContent)!;

            Assert.Equal(200, (int)response.StatusCode);
            Assert.Equal(101, (int)responseOrder.tokenNumber);
        }
    }
}