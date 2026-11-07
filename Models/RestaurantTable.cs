using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    public class RestaurantTable
    {
        [Key]
        public int Id { get; set; }
        public string TableNumber { get; set; } = string.Empty;
        public string QRCodeString { get; set; } = string.Empty;
    }
}
