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

        // Kitchen station of the item when it was ordered (copied, so renaming a station never changes old KOTs).
        public int? StationId { get; set; }
        public string? StationName { get; set; }

        // Running tab: 1 for the first order, 2 for the next items added to the same table, and so on.
        // The kitchen screen and the KOT of a new round show only that round.
        public int Round { get; set; } = 1;

        // Combo: the items it includes, e.g. "1 x Burger, 1 x Fries, 1 x Cola", for the kitchen (not on the bill).
        public string? Includes { get; set; }
    }
}
