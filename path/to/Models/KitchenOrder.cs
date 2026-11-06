namespace RestaurantPOS.Models
{
    public class KitchenOrder
    {
        public int Id { get; set; }
        public int TableId { get; set; }
        public List<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
        public string Status { get; set; } = "Pending";
    }
}
