using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Hubs; // Added missing using directive
using POSSystem.Interfaces;
using POSSystem.Models;
using POSSystem.Repositories;
using POSSystem.Services;
using NLog;
using NLog.Web;
using System.IO;
using System.Text.Json.Serialization;

// Setup NLog for Dependency injection
var logger = NLog.LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();
logger.Info("Starting the Restaurant POS server...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // NLog: Setup NLog for Dependency injection
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Add services to the container.
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
            policy.WithOrigins("http://localhost:5173")
                   .AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials()); // AllowCredentials added for SignalR
    });

    builder.Services.AddOpenApi();
    builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IOrderService, OrderService>();
    builder.Services.AddScoped<IMenuItemRepository, MenuItemRepository>();
    builder.Services.AddScoped<IMenuItemService, MenuItemService>();

    var app = builder.Build();

    // Ensure the logs directory exists
    var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
    if (!Directory.Exists(logDirectory))
    {
        Directory.CreateDirectory(logDirectory);
    }

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational()) { db.Database.Migrate(); } else { db.Database.EnsureCreated(); }

        // Seed Table 1 to 10 so the KitchenOrder has a valid foreign key
        if (!db.RestaurantTables.Any())
        {
            for (var i = 1; i <= 10; i++)
            {
                db.RestaurantTables.Add(new RestaurantTable { TableNumber = i.ToString(), QRCodeString = $"table-{i}-qr" });
            }
            db.SaveChanges();
        }
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseCors("AllowFrontend");

    app.UseHttpsRedirection();

    var summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    app.MapGet("/api/menuitems", async (IMenuItemService menuItemService) =>
    {
        var menuItems = await menuItemService.GetMenuItems();
        return Results.Ok(menuItems);
    });

    app.MapGet("/api/orders", async (IOrderService orderService) =>
    {
        var orders = await orderService.GetOrders();
        return Results.Ok(orders);
    });

    app.MapPost("/api/orders", async (OrderRequest orderRequest, IOrderService orderService, ILogger<OrderService> logger) =>
    {
        try
        {
            var order = await orderService.CreateOrder(orderRequest);
            logger.LogInformation("Order placed: OrderId={OrderId}, TokenNumber={TokenNumber}, TableId={TableId}", order.Id, order.TokenNumber, order.TableId);
            return Results.Ok(new { message = "Order placed", orderId = order.Id, tokenNumber = order.TokenNumber });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    });

    app.MapPut("/api/orders/{id}/status", async (int id, StatusUpdateRequest statusRequest, IOrderService orderService, ILogger<OrderService> logger) =>
    {
        try
        {
            await orderService.UpdateOrderStatus(id, statusRequest.Status);
            logger.LogInformation("Order status updated: OrderId={OrderId}, NewStatus={NewStatus}", id, statusRequest.Status);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        return Results.Ok();
    });

    app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)]
            ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast");

    app.MapHub<OrderHub>("/orderHub"); // Map SignalR Hub

    app.Run();
}
catch (Exception exception)
{
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
    NLog.LogManager.Shutdown();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public class OrderRequest
{
    public int Table { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
    public Guid? ClientOrderId { get; set; }
}

public class OrderItemRequest
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class StatusUpdateRequest
{
    public OrderStatus Status { get; set; }
}

public partial class Program { }
