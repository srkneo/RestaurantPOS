namespace RestaurantPOS.Models
{
    public class RestaurantTable
    {
        public int Id { get; set; }
        public int TableNumber { get; set; }
        public string QRCodeString { get; set; } = string.Empty;
    }
}
