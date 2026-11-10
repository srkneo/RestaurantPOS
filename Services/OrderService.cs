using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Hubs;
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
        private readonly IHubContext<OrderHub> _hubContext; // Injected HubContext

        public OrderService(IOrderRepository orderRepository, AppDbContext context, IHubContext<OrderHub> hubContext)
        {
            _orderRepository = orderRepository;
            _context = context;
            _hubContext = hubContext; // Assign HubContext
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

            await _hubContext.Clients.All.SendAsync("ReceiveNewOrder", order); // Send new order to all clients

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
