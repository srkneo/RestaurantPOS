using System;
using System.Collections.Generic;
using System.Linq;
using POSSystem.Models;

namespace POSSystem.Dtos
{
    public record TableDto(int Id, string TableNumber);
    public record OrderItemDto(int Id, int MenuItemId, string ItemName, decimal UnitPrice, int Quantity);
    public record OrderDto(int Id, int TokenNumber, int TableId, TableDto? Table, string Status, DateTime CreatedAt, List<OrderItemDto> Items);

    public static class OrderMapping
    {
        public static OrderDto ToDto(this KitchenOrder o) => new OrderDto(
            o.Id,
            o.TokenNumber,
            o.TableId,
            o.Table == null ? null : new TableDto(o.Table.Id, o.Table.TableNumber),
            o.Status.ToString(),
            o.CreatedAt,
            o.Items.Select(i => new OrderItemDto(i.Id, i.MenuItemId, i.ItemName, i.UnitPrice, i.Quantity)).ToList());
    }
}