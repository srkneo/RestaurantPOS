using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int TenantId { get; set; } = 1;
        public int KitchenOrderId { get; set; }
        public int MenuItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }

        // Kitchen instruction for this line, e.g. "Jain" or "less spicy".
        [MaxLength(200)]
        public string? Note { get; set; }

        // The size chosen (ItemName then reads "Pizza (Large)") and the extras, as ids for editing the order and as
        // names for the kitchen and the bill. UnitPrice already includes them.
        public int? VariantId { get; set; }
        public string? AddOnIds { get; set; }
        public string? Options { get; set; }
    }
}
