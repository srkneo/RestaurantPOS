using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Orders
{
    public record TableDto(int Id, string TableNumber, bool IsActive, int SortOrder);
    public record TableRequest(string? TableNumber, bool IsActive = true, int SortOrder = 0);

    public static class TableEndpoints
    {
        public static WebApplication MapTableEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/admin/tables").RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Tables");

            group.MapGet("/", async (AppDbContext db) =>
                Results.Ok(await db.RestaurantTables.Where(t => t.OutletId == db.CurrentOutletId).OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                    .Select(t => new TableDto(t.Id, t.TableNumber, t.IsActive, t.SortOrder)).ToListAsync()))
                .WithName("GetAdminTables").WithSummary("List all tables, including tables not in use");

            group.MapPost("/", async (TableRequest req, AppDbContext db) =>
            {
                var errors = await Validate(db, req, null);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                var table = new RestaurantTable { TableNumber = req.TableNumber!.Trim(), QRCodeString = $"table-{req.TableNumber!.Trim()}-qr", IsActive = req.IsActive, SortOrder = req.SortOrder };
                db.RestaurantTables.Add(table);
                await db.SaveChangesAsync();
                return Results.Created($"/api/admin/tables/{table.Id}", ToDto(table));
            }).WithName("CreateTable").WithSummary("Add a table");

            group.MapPut("/{id:int}", async (int id, TableRequest req, AppDbContext db) =>
            {
                var table = await db.RestaurantTables.FirstOrDefaultAsync(t => t.Id == id);
                if (table == null) return Results.NotFound();
                var errors = await Validate(db, req, id);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                table.TableNumber = req.TableNumber!.Trim();
                table.IsActive = req.IsActive;
                table.SortOrder = req.SortOrder;
                await db.SaveChangesAsync();
                return Results.Ok(ToDto(table));
            }).WithName("UpdateTable").WithSummary("Rename a table or mark it not in use");

            return app;
        }

        private static TableDto ToDto(RestaurantTable t) => new(t.Id, t.TableNumber, t.IsActive, t.SortOrder);

        private static async Task<Dictionary<string, string[]>> Validate(AppDbContext db, TableRequest req, int? id)
        {
            var errors = new Dictionary<string, string[]>();
            var number = req.TableNumber?.Trim();
            if (string.IsNullOrEmpty(number) || number.Length > 20)
                errors["tablenumber"] = new[] { "Enter a table number of up to 20 characters." };
            else if (await db.RestaurantTables.AnyAsync(t => t.OutletId == db.CurrentOutletId && t.TableNumber == number && t.Id != id))
                errors["tablenumber"] = new[] { "A table with this number already exists." };
            return errors;
        }
    }
}
