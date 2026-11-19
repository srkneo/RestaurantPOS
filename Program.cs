using Microsoft.EntityFrameworkCore;
using POSSystem.Alerts;
using POSSystem.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using POSSystem.Hubs;
using POSSystem.Interfaces;
using POSSystem.Menu;
using POSSystem.Models;
using POSSystem.Orders;
using POSSystem.Platform;
using POSSystem.Billing;
using POSSystem.Inventory;
using POSSystem.Reservations;
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
    builder.Services.AddPosKeyStorage();
    builder.Services.AddMenuScan();
    builder.Services.AddPosRateLimits(builder.Configuration);
    builder.Services.AddPosLicence(builder.Configuration);
    builder.Services.AddTelegramErrorAlerts(builder.Configuration);
    builder.Services.AddSubscriptionReminders();
    builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready" });
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IOrderService, OrderService>();
    builder.Services.AddScoped<BillService>();
    builder.Services.AddScoped<DayCloseService>();
    builder.Services.AddScoped<TipService>();
    builder.Services.AddScoped<ExpenseService>();
    builder.Services.AddScoped<StockService>();
    builder.Services.AddScoped<ReservationService>();
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ApprovalService>();
    builder.Services.AddScoped<CreditNoteService>();
    builder.Services.AddScoped<LicenceIssuer>();
    builder.Services.AddScoped<SupportService>();
    builder.Services.AddScoped<IMenuItemRepository, MenuItemRepository>();
    builder.Services.AddScoped<IMenuItemService, MenuItemService>();

    var app = builder.Build();
    // /health: the app is running. /health/ready: it can also reach the database (used by the uptime check).
    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
    app.MapPosAuth();
    app.MapStaffEndpoints();
    app.MapMenuEndpoints();
    app.MapMenuImportEndpoints();
    app.MapOrderEndpoints();
    app.MapTableEndpoints();
    app.MapLogoAndAuditEndpoints();
    app.MapStoreEndpoints();
    app.MapSettingsEndpoints();
    app.MapPlatformEndpoints();
    app.MapSubscriptionEndpoints();
    app.MapBillEndpoints();
    app.MapDayCloseEndpoints();
    app.MapTipEndpoints();
    app.MapReportEndpoints();
    app.MapExpenseEndpoints();
    app.MapStockEndpoints();
    app.MapRecipeEndpoints();
    app.MapReservationEndpoints();
    app.MapQrOrderingEndpoints();
    app.MapDemoDataEndpoints();
    app.MapReviewLinkEndpoints();
    app.MapGuestFeedbackEndpoints();
    app.MapDiscountLimitEndpoints();
    app.MapApprovalRulesEndpoints();
    app.MapApprovalEndpoints();
    app.MapLicenceEndpoints();
    app.MapLicenceServerEndpoints();
    app.MapAboutEndpoints();

    // Requests slower than 1 second go to the log with their path, so load tests show where time is spent.
    app.Use(async (context, next) =>
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await next();
        var ms = (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (ms > 1000)
            app.Logger.LogWarning("Slow request {Method} {Path} {Status} {Ms} ms", context.Request.Method, context.Request.Path, context.Response.StatusCode, ms);
    });

    app.UseExceptionHandler();
    app.UsePosAuth();
    app.UseRateLimiter();
    app.UseMiddleware<LicenceGate>();

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
