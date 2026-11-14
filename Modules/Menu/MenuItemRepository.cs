using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POSSystem.Repositories
{
    public class MenuItemRepository : IMenuItemRepository
    {
        private readonly AppDbContext _context;

        public MenuItemRepository(AppDbContext context)
        {
            _context = context;
        }

        // What the POS can sell right now: not deleted, not sold out, in category then position order.
        public async Task<List<MenuItem>> GetMenuItems()
        {
            var categoryOrder = await _context.MenuCategories.ToDictionaryAsync(c => c.Id, c => c.SortOrder);
            var items = await _context.MenuItems
                .Where(m => m.OutletId == _context.CurrentOutletId && !m.IsDeleted && m.IsAvailable)
                .ToListAsync();
            return items
                .OrderBy(m => m.CategoryId.HasValue && categoryOrder.TryGetValue(m.CategoryId.Value, out var s) ? s : int.MaxValue)
                .ThenBy(m => m.SortOrder)
                .ThenBy(m => m.Name)
                .ToList();
        }
    }
}
