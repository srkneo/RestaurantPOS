using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    // A place in the kitchen that cooks some of the items, e.g. Tandoor, Chinese or Drinks counter. Each station can
    // have its own kitchen screen and its own KOT. Items without a station go to the main kitchen.
    public class KitchenStation : TenantEntity
    {
        // v1.7 menu sync: id of the cloud row this was copied from (empty = made on this PC).
        public int? CloudId { get; set; }

        [Key]
        public int Id { get; set; }

        [MaxLength(40)]
        public string Name { get; set; } = string.Empty;

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
