using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    public class RestaurantTable : TenantEntity
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(20)]
        public string TableNumber { get; set; } = string.Empty;

        public string QRCodeString { get; set; } = string.Empty;

        // Secret token in the table's QR code for guest ordering (made on first use, renewable). Never the plain table number.
        public string? OrderToken { get; set; }

        // Tables not in use are hidden from the POS but kept, so old orders still show their table.
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
    }
}
