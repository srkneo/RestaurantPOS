using System;
using System.Collections.Generic;
using POSSystem.Models;

// Request bodies for the order API (kept in the global namespace, as before).

public class OrderRequest
{
    public OrderType? OrderType { get; set; }
    public int? Table { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
    public Guid? ClientOrderId { get; set; }
}

public class OrderItemRequest
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Note { get; set; }
}

public class StatusUpdateRequest
{
    public OrderStatus Status { get; set; }
    public int? Version { get; set; }
}

public class CancelOrderRequest
{
    public string? Reason { get; set; }
    public int Version { get; set; }
}

public class UpdateOrderItemsRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public int Version { get; set; }
}
