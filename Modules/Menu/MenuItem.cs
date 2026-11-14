using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    public class MenuItem : TenantEntity
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        // Category name, kept in step with CategoryId for screens that only need the name.
        public string Category { get; set; } = string.Empty;
        public int? CategoryId { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsVeg { get; set; } = true;

        // Off = sold out for now; the item stays on the owner's menu page.
        public bool IsAvailable { get; set; } = true;

        // GST rate in percent (restaurants usually 5).
        public decimal TaxRate { get; set; } = 5m;

        public int SortOrder { get; set; }

        // Deleted items leave the menu but old orders and bills keep them.
        public bool IsDeleted { get; set; }
    }

    public class MenuCategory : TenantEntity
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
