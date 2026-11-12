using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POSSystem.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _context;

        public OrderRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<KitchenOrder>> GetOrders()
        {
            return await _context.KitchenOrders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .ToListAsync();
        }

        public async Task CreateOrder(KitchenOrder order)
        {
            _context.KitchenOrders.Add(order);
            await _context.SaveChangesAsync();
        }

        public async Task CompleteOrder(int id)
        {
            var order = await _context.KitchenOrders.FindAsync(id);
            if (order != null)
            {
                order.Status = OrderStatus.Completed;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateOrderStatus(int id, OrderStatus status)
        {
            var order = await _context.KitchenOrders.FindAsync(id);
            if (order != null)
            {
                order.Status = status;
                await _context.SaveChangesAsync();
            }
        }
    }
}
