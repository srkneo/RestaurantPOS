using System;
using System.Collections.Generic;
using System.Linq;
using POSSystem.Models;

namespace POSSystem.Dtos
{
    public record TableDto(int Id, string TableNumber);
    public record OrderItemDto(int Id, int MenuItemId, string ItemName, decimal UnitPrice, int Quantity, string? Note,
        int? VariantId = null, List<int>? AddOnIds = null, string? Options = null, int? StationId = null, string? StationName = null, int Round = 1);
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
        string? CancelReason,
        string? TakenBy = null);

    public static class OrderMapping
    {
        public static OrderDto ToDto(this KitchenOrder o) => new OrderDto(
            o.Id,
            o.TokenNumber,
            o.TableId,
            o.Table == null ? null : new TableDto(o.Table.Id, o.Table.TableNumber),
            o.Status.ToString(),
            o.CreatedAt,
            o.Items.Select(i => new OrderItemDto(i.Id, i.MenuItemId, i.ItemName, i.UnitPrice, i.Quantity, i.Note,
                i.VariantId, POSSystem.Menu.MenuOptions.AddOnIdList(i.AddOnIds), i.Options, i.StationId, i.StationName, i.Round)).ToList(),
            o.OrderType.ToString(),
            o.CustomerName,
            o.CustomerPhone,
            o.BusinessDate.ToString("yyyy-MM-dd"),
            o.Version,
            o.CancelReason,
            o.TakenBy);
    }
}
