using POSSystem.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Interfaces
{
    public interface IOrderService
    {
        Task<List<KitchenOrder>> GetOrders(DateOnly? businessDate = null);
        Task<KitchenOrder?> GetOrder(int id);
        Task<KitchenOrder> CreateOrder(OrderRequest orderRequest);
        Task CompleteOrder(int id);
        Task UpdateOrderStatus(int id, OrderStatus status);
        Task UpdateOrderStatus(int id, OrderStatus status, int? version);
        Task<KitchenOrder> CancelOrder(int id, string? reason, int version);
        Task<KitchenOrder> UpdateOrderItems(int id, List<OrderItemRequest> items, int version);
    }

    // The order was changed on another screen since this screen loaded it.
    public class OrderConflictException : Exception
    {
        public OrderConflictException(string message) : base(message) { }
    }

    // The order exists but is not in a state that allows this change (e.g. cooking started).
    public class OrderStateException : InvalidOperationException
    {
        public OrderStateException(string message) : base(message) { }
    }
}
