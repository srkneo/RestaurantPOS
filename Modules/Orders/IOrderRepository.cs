using POSSystem.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Interfaces
{
    public interface IOrderRepository
    {
        Task<List<KitchenOrder>> GetOrders(DateOnly businessDate);
        Task<KitchenOrder?> GetOrder(int id);
        Task CreateOrder(KitchenOrder order);
        Task CompleteOrder(int id);
        Task UpdateOrderStatus(int id, OrderStatus status);
    }
}
