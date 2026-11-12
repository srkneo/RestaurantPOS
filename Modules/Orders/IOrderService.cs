using POSSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Interfaces
{
    public interface IOrderService
    {
        Task<List<KitchenOrder>> GetOrders();
        Task<KitchenOrder> CreateOrder(OrderRequest orderRequest);
        Task CompleteOrder(int id);
        Task UpdateOrderStatus(int id, OrderStatus status);
    }
}
