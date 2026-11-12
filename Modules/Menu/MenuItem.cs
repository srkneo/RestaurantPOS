using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    public class MenuItem : TenantEntity
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
    }
}
