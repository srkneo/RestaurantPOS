using POSSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POSSystem.Interfaces
{
    public interface IMenuItemService
    {
        Task<List<MenuItem>> GetMenuItems();
    }
}
