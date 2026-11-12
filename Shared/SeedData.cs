using System.Linq;
using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    public static class SeedData
    {
        public static void EnsureMenu(AppDbContext db)
        {
            if (!db.Database.IsRelational()) return;
            if (db.MenuItems.Any()) return;

            db.MenuItems.AddRange(
                new MenuItem { Name = "Classic Burger", Price = 6, Category = "Mains" },
                new MenuItem { Name = "Caesar Salad", Price = 5, Category = "Mains" },
                new MenuItem { Name = "Fries", Price = 3, Category = "Sides" },
                new MenuItem { Name = "Coke", Price = 2, Category = "Drinks" });
            db.SaveChanges();
        }
    }
}