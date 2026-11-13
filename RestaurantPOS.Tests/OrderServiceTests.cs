using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using POSSystem.Data;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Models;
using POSSystem.Repositories;
using POSSystem.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace RestaurantPOS.Tests
{
    public class OrderServiceTests
    {
        [Fact]
        public async Task CreateOrder_GeneratesTokenNumberAndSendsSignalRMessage()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
            mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
            mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
            mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
            mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
            mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

            using (var context = new AppDbContext(options))
            {
                context.MenuItems.AddRange(
                    new MenuItem { Id = 1, Name = "Test Burger", Price = 10, Category = "Main" }
                );
                context.SaveChanges();
            }

            using (var context = new AppDbContext(options))
            {
                var orderService = new OrderService(mockOrderRepository.Object, context, mockHubContext.Object);

                var orderRequest = new OrderRequest
                {
                    Table = 1,
                    Items = new List<OrderItemRequest>
                    {
                        new OrderItemRequest { MenuItemId = 1, Quantity = 1 }
                    }
                };

                // Act
                var order = await orderService.CreateOrder(orderRequest);

                // Assert
                Assert.Equal(101, order.TokenNumber);
                mockClientProxy.Verify(c => c.SendCoreAsync("ReceiveNewOrder", It.IsAny<object[]>(), default), Times.Once);
            }
        }

        [Fact]
        public async Task UpdateOrderStatus_SendsOrderStatusChanged()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
            mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

            using (var context = new AppDbContext(options))
            {
                context.KitchenOrders.Add(new KitchenOrder { Id = 1, TokenNumber = 101, Status = OrderStatus.Received });
                context.SaveChanges();
            }

            using (var context = new AppDbContext(options))
            {
                var orderService = new OrderService(mockOrderRepository.Object, context, mockHubContext.Object);

                // Act
                await orderService.UpdateOrderStatus(1, OrderStatus.Ready);

                // Assert
                mockClientProxy.Verify(c => c.SendCoreAsync("OrderStatusChanged", It.IsAny<object[]>(), default), Times.Once);
            }
        }

        [Fact]
        public async Task UpdateOrderStatus_InvalidTransition_Throws()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
            mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

            using (var context = new AppDbContext(options))
            {
                context.KitchenOrders.Add(new KitchenOrder { Id = 1, TokenNumber = 101, Status = OrderStatus.Completed });
                context.SaveChanges();
            }

            using (var context = new AppDbContext(options))
            {
                var orderService = new OrderService(mockOrderRepository.Object, context, mockHubContext.Object);

                // Act & Assert
                await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.UpdateOrderStatus(1, OrderStatus.Preparing));
            }
        }

        [Fact]
        public async Task CreateOrder_SameClientOrderId_ReturnsSameOrder()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
            mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

            using (var context = new AppDbContext(options))
            {
                context.MenuItems.AddRange(
                    new MenuItem { Id = 1, Name = "Burger", Price = 10, Category = "Main" }
                );
                context.SaveChanges();
            }

            using (var context = new AppDbContext(options))
            {
                var orderRepository = new OrderRepository(context);
                var orderService = new OrderService(orderRepository, context, mockHubContext.Object);

                var orderRequest = new OrderRequest
                {
                    Table = 1,
                    ClientOrderId = Guid.NewGuid(),
                    Items = new List<OrderItemRequest>
                    {
                        new OrderItemRequest { MenuItemId = 1, Quantity = 1 }
                    }
                };

                // Act
                var firstOrder = await orderService.CreateOrder(orderRequest);
                var secondOrder = await orderService.CreateOrder(orderRequest);

                // Assert
                Assert.Equal(firstOrder.Id, secondOrder.Id);
                Assert.Equal(1, context.KitchenOrders.Count());
            }
        }
    }
}
