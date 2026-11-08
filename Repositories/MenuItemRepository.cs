using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;
using System.Collections.Generic;
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

        public async Task<List<MenuItem>> GetMenuItems()
        {
            return await _context.MenuItems.ToListAsync();
        }
    }
}
