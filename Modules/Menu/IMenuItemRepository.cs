using POSSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Interfaces
{
    public interface IMenuItemRepository
    {
        Task<List<MenuItem>> GetMenuItems();
    }
}
