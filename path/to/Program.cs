using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Data;
using RestaurantPOS.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddCors();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseHttpsRedirection();

// POST endpoint to submit an order
app.MapPost("/api/orders", async (KitchenOrder order, AppDbContext context) =>
{
    // Set default status if not provided
    if (string.IsNullOrEmpty(order.Status))
    {
        order.Status = "Pending";
    }
    
    context.KitchenOrders.Add(order);
    await context.SaveChangesAsync();
    
    return Results.Created($"/api/orders/{order.Id}", order);
});

// GET endpoint to get all menu items
app.MapGet("/api/menuitems", async (AppDbContext context) =>
{
    return await context.MenuItems.ToListAsync();
});

// GET endpoint to get all tables
app.MapGet("/api/tables", async (AppDbContext context) =>
{
    return await context.RestaurantTables.ToListAsync();
});

app.Run();
