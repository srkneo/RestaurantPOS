using System.Collections.Generic;
using POSSystem.Dtos;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class OrderMappingTests
    {
        [Fact]
        public void ToDto_MapsStatusAndItems()
        {
            var order = new KitchenOrder
            {
                Id = 1, TokenNumber = 101, TableId = 2, Status = OrderStatus.Ready,
                Items = new List<OrderItem> { new OrderItem { Id = 1, MenuItemId = 5, ItemName = "Burger", UnitPrice = 10m, Quantity = 2 } }
            };

            var dto = order.ToDto();

            Assert.Equal("Ready", dto.Status);
            Assert.Equal(101, dto.TokenNumber);
            Assert.Single(dto.Items);
            Assert.Equal("Burger", dto.Items[0].ItemName);
            Assert.Equal(2, dto.Items[0].Quantity);
        }
    }
}