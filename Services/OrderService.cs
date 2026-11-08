using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POSSystem.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly AppDbContext _context;

        public OrderService(IOrderRepository orderRepository, AppDbContext context)
        {
            _orderRepository = orderRepository;
            _context = context;
        }

        public async Task<List<KitchenOrder>> GetOrders()
        {
            return await _orderRepository.GetOrders();
        }

        public async Task<KitchenOrder> CreateOrder(OrderRequest orderRequest)
        {
            var items = await _context.MenuItems.Where(m => orderRequest.Items.Contains(m.Id)).ToListAsync();
            var order = new KitchenOrder
            {
                TableId = orderRequest.Table,
                Status = "Received",
                MenuItems = items
            };

            var existingOrdersToday = await _context.KitchenOrders
                .Where(o => o.CreatedAt.Date == DateTime.UtcNow.Date)
                .ToListAsync();

            order.TokenNumber = existingOrdersToday.Any() ? existingOrdersToday.Max(o => o.TokenNumber) + 1 : 101;

            await _orderRepository.CreateOrder(order);
            return order;
        }

        public async Task CompleteOrder(int id)
        {
            await _orderRepository.CompleteOrder(id);
        }

        public async Task UpdateOrderStatus(int id, string status)
        {
            await _orderRepository.UpdateOrderStatus(id, status);
        }
    }
}
