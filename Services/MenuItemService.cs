using POSSystem.Interfaces;
using POSSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Services
{
    public class MenuItemService : IMenuItemService
    {
        private readonly IMenuItemRepository _menuItemRepository;

        public MenuItemService(IMenuItemRepository menuItemRepository)
        {
            _menuItemRepository = menuItemRepository;
        }

        public async Task<List<MenuItem>> GetMenuItems()
        {
            return await _menuItemRepository.GetMenuItems();
        }
    }
}
