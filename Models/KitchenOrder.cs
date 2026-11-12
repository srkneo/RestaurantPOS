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

        public List<OrderItem> Items { get; set; } = new List<OrderItem>();

        public OrderStatus Status { get; set; } = OrderStatus.Received;

        public int TokenNumber { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
