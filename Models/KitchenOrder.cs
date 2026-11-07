using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models
{
    public class KitchenOrder
    {
        [Key]
        public int Id { get; set; }

        public int TableId { get; set; }

        [ForeignKey("TableId")]
        public RestaurantTable? Table { get; set; }

        public List<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

        public string Status { get; set; } = "Pending";
    }
}
