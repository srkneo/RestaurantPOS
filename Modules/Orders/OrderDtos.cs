using System;
using System.Collections.Generic;
using System.Linq;
using POSSystem.Models;

namespace POSSystem.Dtos
{
    public record TableDto(int Id, string TableNumber);
    public record OrderItemDto(int Id, int MenuItemId, string ItemName, decimal UnitPrice, int Quantity, string? Note);
    public record OrderDto(
        int Id,
        int TokenNumber,
        int? TableId,
        TableDto? Table,
        string Status,
        DateTime CreatedAt,
        List<OrderItemDto> Items,
        string OrderType,
        string? CustomerName,
        string? CustomerPhone,
        string BusinessDate,
        int Version,
        string? CancelReason);

    public static class OrderMapping
    {
        public static OrderDto ToDto(this KitchenOrder o) => new OrderDto(
            o.Id,
            o.TokenNumber,
            o.TableId,
            o.Table == null ? null : new TableDto(o.Table.Id, o.Table.TableNumber),
            o.Status.ToString(),
            o.CreatedAt,
            o.Items.Select(i => new OrderItemDto(i.Id, i.MenuItemId, i.ItemName, i.UnitPrice, i.Quantity, i.Note)).ToList(),
            o.OrderType.ToString(),
            o.CustomerName,
            o.CustomerPhone,
            o.BusinessDate.ToString("yyyy-MM-dd"),
            o.Version,
            o.CancelReason);
    }
}
