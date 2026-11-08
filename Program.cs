using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;
using POSSystem.Repositories;
using POSSystem.Services;
using NLog;
using NLog.Web;
using System.IO;

var logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite("Data Source=restaurant.db"));

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
            policy.WithOrigins("http://localhost:5173")
                   .AllowAnyMethod()
                   .AllowAnyHeader());
    }); 

    builder.Services.AddOpenApi();

    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IOrderService, OrderService>();

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
        db.Database.EnsureCreated();

        // Seed Table 1 so the KitchenOrder has a valid foreign key
        if (!db.RestaurantTables.Any())
        {
            db.RestaurantTables.Add(new RestaurantTable { TableNumber = "1", QRCodeString = "table-1-qr" });
            db.SaveChanges();
        }

        if (!db.MenuItems.Any())
        {
            db.MenuItems.AddRange(
                new MenuItem { Name = "Classic Burger", Price = 9.99m, Category = "Mains" },
                new MenuItem { Name = "Fries", Price = 3.99m, Category = "Sides" },
                new MenuItem { Name = "Coke", Price = 1.99m, Category = "Beverages" },
                new MenuItem { Name = "Caesar Salad", Price = 7.99m, Category = "Mains" }
            );
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

    app.MapGet("/api/menuitems", async (AppDbContext db) =>
        await db.MenuItems.ToListAsync());

    app.MapGet("/api/orders", async (IOrderService orderService) =>
        await orderService.GetOrders());

    app.MapPost("/api/orders", async (OrderRequest orderRequest, IOrderService orderService, AppDbContext db) =>
    {
        var items = await db.MenuItems.Where(m => orderRequest.Items.Contains(m.Id)).ToListAsync();
        var order = new KitchenOrder 
        { 
            TableId = orderRequest.Table, 
            Status = "Pending", 
            MenuItems = items 
        };
        await orderService.CreateOrder(order);
        return Results.Ok(new { message = "Order placed successfully" });
    });

    app.MapPut("/api/orders/{id}/complete", async (int id, IOrderService orderService) =>
    {
        await orderService.CompleteOrder(id);
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
    LogManager.Shutdown();
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public class OrderRequest
{
    public int Table { get; set; }
    public List<int> Items { get; set; }
}
