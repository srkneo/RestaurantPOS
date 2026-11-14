using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Menu;
using POSSystem.Models;
using POSSystem.Orders;
using POSSystem.Repositories;
using POSSystem.Services;
using POSSystem.Staff;
using NLog;
using NLog.Web;
using System.IO;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

// Setup NLog for Dependency injection
var logger = NLog.LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();
logger.Info("Starting the Restaurant POS server...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
            policy.WithOrigins("http://localhost:5173")
                   .AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials()); // needed for SignalR
    });

    builder.Services.AddOpenApi();
    builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddProblemDetails();
    builder.Services.AddPosAuth();
    builder.Services.AddHealthChecks();
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IOrderService, OrderService>();
    builder.Services.AddScoped<IMenuItemRepository, MenuItemRepository>();
    builder.Services.AddScoped<IMenuItemService, MenuItemService>();

    var app = builder.Build();
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapPosAuth();
    app.MapStaffEndpoints();
    app.MapMenuEndpoints();
    app.MapOrderEndpoints();

    app.UseExceptionHandler();
    app.UsePosAuth();

    var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
    if (!Directory.Exists(logDirectory))
    {
        Directory.CreateDirectory(logDirectory);
    }

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational()) { db.Database.Migrate(); } else { db.Database.EnsureCreated(); }
        SeedData.EnsureMenu(db);
        await IdentitySeed.EnsureAsync(scope.ServiceProvider, app.Configuration);

        // Tables 1 to 10 for a new restaurant
        if (!db.RestaurantTables.Any())
        {
            for (var i = 1; i <= 10; i++)
            {
                db.RestaurantTables.Add(new RestaurantTable { TableNumber = i.ToString(), QRCodeString = $"table-{i}-qr", SortOrder = i });
            }
            db.SaveChanges();
        }
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference().AllowAnonymous();
    }

    app.UseCors("AllowFrontend");
    app.UseHttpsRedirection();

    app.MapHub<OrderHub>("/orderHub");

    app.Run();
}
catch (Exception exception)
{
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    // Flush and stop internal timers/threads before exit (avoids a crash on Linux)
    NLog.LogManager.Shutdown();
}

public partial class Program { }
