using Microsoft.AspNetCore.SignalR;
using POSSystem.Dtos;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Models;
using System;
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

        private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
        {
            [OrderStatus.Received] = new[] { OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Cancelled },
            [OrderStatus.Preparing] = new[] { OrderStatus.Ready, OrderStatus.Cancelled },
            [OrderStatus.Ready] = new[] { OrderStatus.Completed },
            [OrderStatus.Completed] = Array.Empty<OrderStatus>(),
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>()
        };

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
            if (orderRequest.ClientOrderId.HasValue)
            {
                var existing = await _context.KitchenOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.ClientOrderId == orderRequest.ClientOrderId.Value);
                if (existing != null) return existing;
            }

            var ids = orderRequest.Items.Select(i => i.MenuItemId).Distinct().ToList();
            var menu = await _context.MenuItems.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
            var orderItems = orderRequest.Items.Where(i => i.Quantity > 0 && menu.ContainsKey(i.MenuItemId)).Select(i => new OrderItem { MenuItemId = i.MenuItemId, ItemName = menu[i.MenuItemId].Name, UnitPrice = Convert.ToDecimal(menu[i.MenuItemId].Price), Quantity = i.Quantity }).ToList();
            if (!orderItems.Any()) throw new ArgumentException("Order must contain at least one valid item");

            var order = new KitchenOrder
            {
                TableId = orderRequest.Table,
                Status = OrderStatus.Received,
                Items = orderItems,
                ClientOrderId = orderRequest.ClientOrderId ?? Guid.NewGuid()
            };

            var existingOrdersToday = await _context.KitchenOrders
                .Where(o => o.CreatedAt.Date == DateTime.UtcNow.Date)
                .ToListAsync();

            order.TokenNumber = existingOrdersToday.Any() ? existingOrdersToday.Max(o => o.TokenNumber) + 1 : 101;

            await _orderRepository.CreateOrder(order);

            await _hubContext.Clients.Group($"tenant-{order.TenantId}").SendAsync("ReceiveNewOrder", order.ToDto()); // Send new order to all clients

            return order;
        }

        public async Task CompleteOrder(int id)
        {
            await _orderRepository.CompleteOrder(id);
        }

        public async Task UpdateOrderStatus(int id, OrderStatus status)
        {
            var order = await _context.KitchenOrders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return;
            if (!AllowedTransitions[order.Status].Contains(status))
                throw new InvalidOperationException($"Cannot change status from {order.Status} to {status}");
            await _orderRepository.UpdateOrderStatus(id, status);
            await _hubContext.Clients.Group($"tenant-{order.TenantId}").SendAsync("OrderStatusChanged", new { orderId = order.Id, tokenNumber = order.TokenNumber, status = status.ToString() });
        }
    }
}
