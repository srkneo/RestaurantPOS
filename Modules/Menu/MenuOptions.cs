using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    // A size or portion of a menu item with its own price, e.g. Half / Full or Regular / Large.
    // When an item has sizes, the cashier must pick one and its price replaces the item's price.
    public class ItemVariant : TenantEntity
    {
        [Key]
        public int Id { get; set; }
        public int MenuItemId { get; set; }

        [MaxLength(40)]
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
        public int SortOrder { get; set; }
        public bool IsAvailable { get; set; } = true;
    }

    // An extra that can be added to a menu item for a price, e.g. Extra cheese (+30). Its price is added to the line.
    public class ItemAddOn : TenantEntity
    {
        [Key]
        public int Id { get; set; }
        public int MenuItemId { get; set; }

        [MaxLength(40)]
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
        public int SortOrder { get; set; }
        public bool IsAvailable { get; set; } = true;
    }
}
