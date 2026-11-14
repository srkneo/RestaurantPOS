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
    }
}
