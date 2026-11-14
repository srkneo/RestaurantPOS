using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using POSSystem.Models;

namespace POSSystem.Data
{
    public static class SeedData
    {
        // Runs at every start: sample menu for an empty database, then one-time data fixes.
        public static void EnsureMenu(AppDbContext db)
        {
            if (!db.Database.IsRelational()) return;

            if (!db.MenuItems.IgnoreQueryFilters().Any())
            {
                var mains = new MenuCategory { Name = "Mains", SortOrder = 1 };
                var sides = new MenuCategory { Name = "Sides", SortOrder = 2 };
                var drinks = new MenuCategory { Name = "Drinks", SortOrder = 3 };
                db.MenuCategories.AddRange(mains, sides, drinks);
                db.SaveChanges();

                db.MenuItems.AddRange(
                    new MenuItem { Name = "Classic Burger", Price = 6, Category = "Mains", CategoryId = mains.Id, IsVeg = false, SortOrder = 1 },
                    new MenuItem { Name = "Caesar Salad", Price = 5, Category = "Mains", CategoryId = mains.Id, SortOrder = 2 },
                    new MenuItem { Name = "Fries", Price = 3, Category = "Sides", CategoryId = sides.Id, SortOrder = 1 },
                    new MenuItem { Name = "Coke", Price = 2, Category = "Drinks", CategoryId = drinks.Id, SortOrder = 1 });
                db.SaveChanges();
            }

            LinkCategories(db);
            FixBusinessDates(db);
        }

        // Items from before categories existed: make a category for each old category name and link it.
        public static void LinkCategories(AppDbContext db)
        {
            var unlinked = db.MenuItems.IgnoreQueryFilters().Where(m => m.CategoryId == null).ToList();
            if (unlinked.Count == 0) return;

            var tenantBefore = db.CurrentTenantId;
            var outletBefore = db.CurrentOutletId;
            foreach (var group in unlinked.GroupBy(m => new { m.TenantId, m.OutletId, Name = string.IsNullOrWhiteSpace(m.Category) ? "Other" : m.Category.Trim() }))
            {
                db.CurrentTenantId = group.Key.TenantId;
                db.CurrentOutletId = group.Key.OutletId;
                var category = db.MenuCategories.IgnoreQueryFilters()
                    .FirstOrDefault(c => c.TenantId == group.Key.TenantId && c.OutletId == group.Key.OutletId && c.Name == group.Key.Name);
                if (category == null)
                {
                    var next = db.MenuCategories.IgnoreQueryFilters()
                        .Where(c => c.TenantId == group.Key.TenantId && c.OutletId == group.Key.OutletId)
                        .Select(c => (int?)c.SortOrder).Max() ?? 0;
                    category = new MenuCategory { Name = group.Key.Name, SortOrder = next + 1 };
                    db.MenuCategories.Add(category);
                    db.SaveChanges();
                }
                foreach (var item in group)
                {
                    item.CategoryId = category.Id;
                    item.Category = category.Name;
                }
                db.SaveChanges();
            }
            db.CurrentTenantId = tenantBefore;
            db.CurrentOutletId = outletBefore;
        }

        // Orders from before business dates existed: work the date out from when they were placed,
        // in the outlet's time zone with its day start hour.
        public static int FixBusinessDates(AppDbContext db)
        {
            return db.Database.ExecuteSqlRaw(@"
                UPDATE ""KitchenOrders"" AS k
                SET ""BusinessDate"" = ((k.""CreatedAt"" AT TIME ZONE o.""TimeZone"") - make_interval(hours => o.""BusinessDayStartHour""))::date
                FROM ""Outlets"" AS o
                WHERE o.""Id"" = k.""OutletId"" AND k.""BusinessDate"" = DATE '0001-01-01';");
        }
    }
}
