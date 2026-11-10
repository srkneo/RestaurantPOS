using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NLog;
using POSSystem.Data;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Models;
using POSSystem.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class OrderServiceTests
    {
        [Fact]
        public async Task CreateOrder_GeneratesTokenNumberAndSendsSignalRMessage()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDatabase")
                .Options;

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockLogger = new Mock<ILogger<OrderService>>();

            using (var context = new AppDbContext(options))
            {
                context.MenuItems.AddRange(
                    new MenuItem { Id = 1, Name = "Classic Burger", Price = 9.99m, Category = "Mains" },
                    new MenuItem { Id = 2, Name = "Fries", Price = 3.99m, Category = "Sides" }
                );
                context.SaveChanges();
            }

            using (var context = new AppDbContext(options))
            {
                var orderService = new OrderService(mockOrderRepository.Object, context, mockHubContext.Object);

                var orderRequest = new OrderRequest
                {
                    Table = 1,
                    Items = new List<int> { 1, 2 }
                };

                // Act
                var order = await orderService.CreateOrder(orderRequest);

                // Assert
                Assert.Equal(101, order.TokenNumber);
                mockHubContext.Verify(hub => hub.Clients.All.SendAsync("ReceiveNewOrder", It.IsAny<KitchenOrder>()), Times.Once);
            }
        }

        [Fact]
        public async Task UpdateOrderStatus_SendsOrderStatusChanged()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDatabase")
                .Options;

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockLogger = new Mock<ILogger<OrderService>>();

            using (var context = new AppDbContext(options))
            {
                context.KitchenOrders.Add(new KitchenOrder { Id = 1, TokenNumber = 101, Status = "Received" });
                context.SaveChanges();
            }

            using (var context = new AppDbContext(options))
            {
                var orderService = new OrderService(mockOrderRepository.Object, context, mockHubContext.Object);

                // Act
                await orderService.UpdateOrderStatus(1, "Ready");

                // Assert
                mockHubContext.Verify(hub => hub.Clients.All.SendAsync("OrderStatusChanged", It.IsAny<object[]>()), Times.Once);
            }
        }
    }
}
