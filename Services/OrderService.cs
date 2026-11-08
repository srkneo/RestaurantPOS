using POSSystem.Interfaces;
using POSSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;

        public OrderService(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        public async Task<List<KitchenOrder>> GetOrders()
        {
            return await _orderRepository.GetOrders();
        }

        public async Task CreateOrder(KitchenOrder order)
        {
            await _orderRepository.CreateOrder(order);
        }

        public async Task CompleteOrder(int id)
        {
            await _orderRepository.CompleteOrder(id);
        }
    }
}
