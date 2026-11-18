using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Menu
{
    public record StationDto(int Id, string Name, int SortOrder, bool IsActive);
    public record StationRequest(string? Name, int SortOrder, bool IsActive = true);

    public static class StationEndpoints
    {
        public static WebApplication MapStationEndpoints(this WebApplication app)
        {
            // Kitchen screens and tills: the stations of this outlet, to pick one on a kitchen screen.
            app.MapGet("/api/stations", async (AppDbContext db) =>
                Results.Ok(await List(db, activeOnly: true)))
                .RequireAuthorization().WithTags("Menu")
                .WithName("GetStations").WithSummary("Kitchen stations of this outlet");

            var admin = app.MapGroup("/api/admin/menu/stations").RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Menu admin");

            admin.MapGet("/", async (AppDbContext db) => Results.Ok(await List(db, activeOnly: false)))
                .WithName("GetStationsAdmin").WithSummary("All kitchen stations, including switched off ones");

            admin.MapPost("/", async (StationRequest req, AppDbContext db) =>
            {
                var errors = await Validate(db, req, null);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                var station = new KitchenStation { Name = req.Name!.Trim(), SortOrder = req.SortOrder, IsActive = req.IsActive };
                db.KitchenStations.Add(station);
                await db.SaveChangesAsync();
                return Results.Created($"/api/admin/menu/stations/{station.Id}", ToDto(station));
            }).WithName("CreateStation").WithSummary("Add a kitchen station");

            admin.MapPut("/{id:int}", async (int id, StationRequest req, AppDbContext db) =>
            {
                var station = await db.KitchenStations.FirstOrDefaultAsync(s => s.Id == id && s.OutletId == db.CurrentOutletId);
                if (station == null) return Results.NotFound();
                var errors = await Validate(db, req, id);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                station.Name = req.Name!.Trim();
                station.SortOrder = req.SortOrder;
                station.IsActive = req.IsActive;
                await db.SaveChangesAsync();
                return Results.Ok(ToDto(station));
            }).WithName("UpdateStation").WithSummary("Rename, reorder or switch off a kitchen station");

            return app;
        }

        private static StationDto ToDto(KitchenStation s) => new(s.Id, s.Name, s.SortOrder, s.IsActive);

        private static async Task<List<StationDto>> List(AppDbContext db, bool activeOnly) =>
            await db.KitchenStations.Where(s => s.OutletId == db.CurrentOutletId && (!activeOnly || s.IsActive))
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                .Select(s => new StationDto(s.Id, s.Name, s.SortOrder, s.IsActive)).ToListAsync();

        private static async Task<Dictionary<string, string[]>> Validate(AppDbContext db, StationRequest req, int? id)
        {
            var errors = new Dictionary<string, string[]>();
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 40)
                errors["name"] = new[] { "Enter a name of up to 40 characters." };
            else if (await db.KitchenStations.AnyAsync(s => s.OutletId == db.CurrentOutletId && s.Name == name && s.Id != id))
                errors["name"] = new[] { "A station with this name already exists." };
            return errors;
        }
    }
}
