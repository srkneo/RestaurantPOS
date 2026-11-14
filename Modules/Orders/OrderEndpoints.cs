using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POSSystem.Data;
using POSSystem.Dtos;
using POSSystem.Interfaces;
using POSSystem.Services;

namespace POSSystem.Orders
{
    // Order API. Role rules for /api/orders are in AuthSetup.UsePosAuth:
    // POST (place, cancel, change items) = cashier, manager, admin; PUT (status) = kitchen, manager, admin.
    public static class OrderEndpoints
    {
        public static WebApplication MapOrderEndpoints(this WebApplication app)
        {
            app.MapGet("/api/tables", async (AppDbContext db) =>
                Results.Ok(await db.RestaurantTables.Where(t => t.IsActive && t.OutletId == db.CurrentOutletId)
                    .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => new { t.Id, t.TableNumber }).ToListAsync()))
                .WithName("GetTables").WithSummary("List restaurant tables");

            app.MapGet("/api/orders", async (string? date, IOrderService orderService) =>
            {
                DateOnly? day = null;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        return Results.BadRequest(new { message = "Use date=YYYY-MM-DD." });
                    day = parsed;
                }
                var orders = await orderService.GetOrders(day);
                return Results.Ok(orders.Select(o => o.ToDto()));
            }).WithName("GetOrders").WithSummary("List orders of one business day (today by default)");

            app.MapGet("/api/orders/{id:int}", async (int id, IOrderService orderService) =>
            {
                var order = await orderService.GetOrder(id);
                return order == null ? Results.NotFound() : Results.Ok(order.ToDto());
            }).WithName("GetOrder").WithSummary("Get one order");

            app.MapPost("/api/orders", async (OrderRequest orderRequest, IOrderService orderService, ILogger<OrderService> logger) =>
            {
                try
                {
                    var order = await orderService.CreateOrder(orderRequest);
                    logger.LogInformation("Order placed: OrderId={OrderId}, TokenNumber={TokenNumber}, Type={OrderType}", order.Id, order.TokenNumber, order.OrderType);
                    return Results.Ok(new { message = "Order placed", orderId = order.Id, tokenNumber = order.TokenNumber, version = order.Version });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            }).WithName("CreateOrder").WithSummary("Place a new order");

            app.MapPut("/api/orders/{id:int}/status", async (int id, StatusUpdateRequest statusRequest, IOrderService orderService, ILogger<OrderService> logger) =>
            {
                try
                {
                    await orderService.UpdateOrderStatus(id, statusRequest.Status, statusRequest.Version);
                    logger.LogInformation("Order status updated: OrderId={OrderId}, NewStatus={NewStatus}", id, statusRequest.Status);
                    return Results.Ok();
                }
                catch (OrderConflictException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            }).WithName("UpdateOrderStatus").WithSummary("Change an order's status");

            app.MapPost("/api/orders/{id:int}/cancel", async (int id, CancelOrderRequest request, IOrderService orderService, ILogger<OrderService> logger) =>
                await Change(() => orderService.CancelOrder(id, request.Reason, request.Version), order =>
                {
                    logger.LogInformation("Order cancelled: OrderId={OrderId}, Reason={Reason}", order.Id, order.CancelReason);
                    return Results.Ok(order.ToDto());
                }))
                .WithName("CancelOrder").WithSummary("Cancel an order before cooking starts");

            app.MapPost("/api/orders/{id:int}/items", async (int id, UpdateOrderItemsRequest request, IOrderService orderService) =>
                await Change(() => orderService.UpdateOrderItems(id, request.Items, request.Version), order => Results.Ok(order.ToDto())))
                .WithName("UpdateOrderItems").WithSummary("Change the items of an order before cooking starts");

            return app;
        }

        private static async System.Threading.Tasks.Task<IResult> Change(
            Func<System.Threading.Tasks.Task<POSSystem.Models.KitchenOrder>> action,
            Func<POSSystem.Models.KitchenOrder, IResult> onSuccess)
        {
            try
            {
                return onSuccess(await action());
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OrderConflictException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (OrderStateException ex)
            {
                return Results.Conflict(new { message = ex.Message, code = "cooking-started" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }
    }
}
