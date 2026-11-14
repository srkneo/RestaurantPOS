using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POSSystem.Models
{
    public enum OrderType
    {
        DineIn,
        Takeaway
    }

    public class KitchenOrder : TenantEntity
    {
        [Key]
        public int Id { get; set; }

        public OrderType OrderType { get; set; } = OrderType.DineIn;

        // Dine-in orders have a table; takeaway orders have a customer name instead.
        public int? TableId { get; set; }

        [ForeignKey("TableId")]
        public RestaurantTable? Table { get; set; }

        [MaxLength(100)]
        public string? CustomerName { get; set; }

        [MaxLength(20)]
        public string? CustomerPhone { get; set; }

        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
        public OrderStatus Status { get; set; } = OrderStatus.Received;

        // Token restarts every business day per outlet (see BusinessClock and OrderCounter).
        public int TokenNumber { get; set; }
        public DateOnly BusinessDate { get; set; }

        public Guid ClientOrderId { get; set; } = Guid.NewGuid();

        // Goes up on every change; a screen sending an older version gets "changed on another screen".
        public int Version { get; set; } = 1;

        public DateTime? PreparingAt { get; set; }
        public DateTime? ReadyAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelledAt { get; set; }

        [MaxLength(200)]
        public string? CancelReason { get; set; }
    }

    // Last token handed out per outlet per business day. Increased in one database statement, so two tills never get the same token.
    public class OrderCounter
    {
        public int TenantId { get; set; }
        public int OutletId { get; set; }
        public DateOnly BusinessDate { get; set; }
        public int LastToken { get; set; }
    }
}
