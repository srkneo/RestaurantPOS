using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Dtos;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace POSSystem.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly AppDbContext _context;
        private readonly IHubContext<OrderHub> _hubContext;

        // Cancelling goes through CancelOrder (it needs a reason), not through a status change.
        private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
        {
            [OrderStatus.Received] = new[] { OrderStatus.Preparing, OrderStatus.Ready },
            [OrderStatus.Preparing] = new[] { OrderStatus.Ready },
            [OrderStatus.Ready] = new[] { OrderStatus.Completed },
            [OrderStatus.Completed] = Array.Empty<OrderStatus>(),
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>()
        };

        private static readonly Regex PhonePattern = new(@"^\+?[0-9 ]{6,20}$");

        public OrderService(IOrderRepository orderRepository, AppDbContext context, IHubContext<OrderHub> hubContext)
        {
            _orderRepository = orderRepository;
            _context = context;
            _hubContext = hubContext;
        }

        private async Task<Outlet?> CurrentOutlet() =>
            await _context.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == _context.CurrentOutletId && o.TenantId == _context.CurrentTenantId);

        private static DateOnly TodayFor(Outlet? outlet) =>
            BusinessClock.BusinessDate(DateTime.UtcNow, outlet?.TimeZone, outlet?.BusinessDayStartHour ?? BusinessClock.DefaultDayStartHour);

        // Orders of one business day for the signed-in outlet; today when no date is given.
        public async Task<List<KitchenOrder>> GetOrders(DateOnly? businessDate = null)
        {
            var date = businessDate ?? TodayFor(await CurrentOutlet());
            return await _orderRepository.GetOrders(date);
        }

        public async Task<KitchenOrder?> GetOrder(int id) => await _orderRepository.GetOrder(id);

        public async Task<KitchenOrder> CreateOrder(OrderRequest orderRequest)
        {
            if (orderRequest.ClientOrderId.HasValue)
            {
                var existing = await _context.KitchenOrders.Include(o => o.Items).Include(o => o.Table)
                    .FirstOrDefaultAsync(o => o.ClientOrderId == orderRequest.ClientOrderId.Value);
                if (existing != null) return existing;
            }

            var outlet = await CurrentOutlet();
            var settings = outlet?.GetSettings() ?? new OutletSettings();
            var orderType = orderRequest.OrderType ?? settings.DefaultOrderType;
            if (orderType == OrderType.DineIn && !settings.DineInEnabled) throw new ArgumentException("Dine-in orders are turned off for this outlet.");
            if (orderType == OrderType.Takeaway && !settings.TakeawayEnabled) throw new ArgumentException("Takeaway orders are turned off for this outlet.");

            int? tableId = null;
            string? customerName = string.IsNullOrWhiteSpace(orderRequest.CustomerName) ? null : orderRequest.CustomerName.Trim();
            string? customerPhone = string.IsNullOrWhiteSpace(orderRequest.CustomerPhone) ? null : orderRequest.CustomerPhone.Trim();
            if (customerName is { Length: > 100 }) throw new ArgumentException("Customer name can be at most 100 characters.");
            if (customerPhone != null && !PhonePattern.IsMatch(customerPhone)) throw new ArgumentException("Enter a valid phone number.");

            if (orderType == OrderType.DineIn)
            {
                if (orderRequest.Table is not > 0) throw new ArgumentException("Choose a table for a dine-in order.");
                var tableExists = await _context.RestaurantTables.AnyAsync(t => t.Id == orderRequest.Table.Value && t.IsActive);
                if (!tableExists) throw new ArgumentException("That table does not exist or is not in use.");
                tableId = orderRequest.Table.Value;
            }
            else if (settings.TakeawayNeedsName && customerName == null)
            {
                throw new ArgumentException("Enter the customer's name for a takeaway order.");
            }

            var orderItems = await BuildItems(orderRequest.Items, settings);
            var businessDate = TodayFor(outlet);

            var order = new KitchenOrder
            {
                OrderType = orderType,
                TableId = tableId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                Status = OrderStatus.Received,
                Items = orderItems,
                BusinessDate = businessDate,
                ClientOrderId = orderRequest.ClientOrderId ?? Guid.NewGuid(),
                Version = 1
            };
            order.TokenNumber = await NextToken(businessDate, settings.FirstToken);

            await _orderRepository.CreateOrder(order);
            if (order.TableId.HasValue && order.Table == null) order.Table = await _context.RestaurantTables.FindAsync(order.TableId.Value);

            await _hubContext.Clients.Group($"tenant-{order.TenantId}").SendAsync("ReceiveNewOrder", order.ToDto());
            return order;
        }

        // Checks the lines against the live menu and copies name and price, so later menu changes never alter this order.
        private async Task<List<OrderItem>> BuildItems(List<OrderItemRequest>? requested, OutletSettings settings)
        {
            if (requested == null || requested.Count == 0) throw new ArgumentException("Order must contain at least one item.");
            foreach (var line in requested)
            {
                if (line.Quantity < 1 || line.Quantity > settings.MaxItemQuantity)
                    throw new ArgumentException($"Quantity must be between 1 and {settings.MaxItemQuantity}.");
                if (line.Note is { Length: > 200 }) throw new ArgumentException("A note can be at most 200 characters.");
            }

            var ids = requested.Select(i => i.MenuItemId).Distinct().ToList();
            var menu = await _context.MenuItems.Where(m => ids.Contains(m.Id) && !m.IsDeleted).ToDictionaryAsync(m => m.Id);
            foreach (var id in ids)
            {
                if (!menu.TryGetValue(id, out var item)) throw new ArgumentException("An item in the order is not on the menu.");
                if (!item.IsAvailable) throw new ArgumentException($"{item.Name} is sold out.");
            }

            return requested.Select(i => new OrderItem
            {
                MenuItemId = i.MenuItemId,
                ItemName = menu[i.MenuItemId].Name,
                UnitPrice = menu[i.MenuItemId].Price,
                Quantity = i.Quantity,
                Note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim()
            }).ToList();
        }

        // Next token for this outlet and business day. On Postgres one statement adds 1, so two tills
        // ordering at the same moment always get different tokens.
        private async Task<int> NextToken(DateOnly businessDate, int firstToken)
        {
            var tenantId = _context.CurrentTenantId;
            var outletId = _context.CurrentOutletId;
            if (_context.Database.IsRelational())
            {
                var tokens = await _context.Database.SqlQuery<int>($@"
                    INSERT INTO ""OrderCounters"" (""TenantId"", ""OutletId"", ""BusinessDate"", ""LastToken"")
                    VALUES ({tenantId}, {outletId}, {businessDate}, {firstToken})
                    ON CONFLICT (""TenantId"", ""OutletId"", ""BusinessDate"")
                    DO UPDATE SET ""LastToken"" = ""OrderCounters"".""LastToken"" + 1
                    RETURNING ""LastToken"" AS ""Value""").ToListAsync();
                return tokens.Single();
            }

            var max = await _context.KitchenOrders
                .Where(o => o.OutletId == outletId && o.BusinessDate == businessDate)
                .Select(o => (int?)o.TokenNumber)
                .MaxAsync();
            return max.HasValue ? max.Value + 1 : firstToken;
        }

        public async Task CompleteOrder(int id)
        {
            await _orderRepository.CompleteOrder(id);
        }

        public Task UpdateOrderStatus(int id, OrderStatus status) => UpdateOrderStatus(id, status, null);

        public async Task UpdateOrderStatus(int id, OrderStatus status, int? version)
        {
            var order = await _context.KitchenOrders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return;
            if (version.HasValue && version.Value != order.Version)
                throw new OrderConflictException("This order was changed on another screen.");
            if (!AllowedTransitions[order.Status].Contains(status))
                throw new InvalidOperationException($"Cannot change status from {order.Status} to {status}");

            var now = DateTime.UtcNow;
            order.Status = status;
            if (status == OrderStatus.Preparing) order.PreparingAt = now;
            if (status == OrderStatus.Ready) order.ReadyAt = now;
            if (status == OrderStatus.Completed) order.CompletedAt = now;
            order.Version++;
            await SaveChecked();

            await _hubContext.Clients.Group($"tenant-{order.TenantId}").SendAsync("OrderStatusChanged",
                new { orderId = order.Id, tokenNumber = order.TokenNumber, status = status.ToString(), version = order.Version });
        }

        // Allowed only before the kitchen starts cooking. The order is kept, never deleted.
        public async Task<KitchenOrder> CancelOrder(int id, string? reason, int version)
        {
            var order = await LoadForChange(id, version);
            var text = reason?.Trim();
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Give a reason for cancelling.");
            if (text.Length > 200) throw new ArgumentException("The reason can be at most 200 characters.");

            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = DateTime.UtcNow;
            order.CancelReason = text;
            order.Version++;
            await SaveChecked();

            await _hubContext.Clients.Group($"tenant-{order.TenantId}").SendAsync("OrderStatusChanged",
                new { orderId = order.Id, tokenNumber = order.TokenNumber, status = order.Status.ToString(), version = order.Version });
            return order;
        }

        // Replaces the lines of an order before the kitchen starts cooking.
        public async Task<KitchenOrder> UpdateOrderItems(int id, List<OrderItemRequest> items, int version)
        {
            var order = await LoadForChange(id, version);
            var outlet = await CurrentOutlet();
            var newItems = await BuildItems(items, outlet?.GetSettings() ?? new OutletSettings());

            _context.OrderItems.RemoveRange(order.Items);
            order.Items = newItems;
            order.Version++;
            await SaveChecked();

            await _hubContext.Clients.Group($"tenant-{order.TenantId}").SendAsync("OrderUpdated", order.ToDto());
            return order;
        }

        private async Task<KitchenOrder> LoadForChange(int id, int version)
        {
            var order = await _context.KitchenOrders.Include(o => o.Items).Include(o => o.Table).FirstOrDefaultAsync(o => o.Id == id)
                ?? throw new KeyNotFoundException("Order not found.");
            if (order.Version != version) throw new OrderConflictException("This order was changed on another screen.");
            if (order.Status != OrderStatus.Received)
                throw new OrderStateException("Cooking has started, so this order can no longer be changed.");
            return order;
        }

        private async Task SaveChecked()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new OrderConflictException("This order was changed on another screen.");
            }
        }
    }
}
