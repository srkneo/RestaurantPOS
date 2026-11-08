using POSSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Interfaces
{
    public interface IOrderRepository
    {
        Task<List<KitchenOrder>> GetOrders();
        Task CreateOrder(KitchenOrder order);
        Task CompleteOrder(int id);
        Task UpdateOrderStatus(int id, string status);
    }
}
