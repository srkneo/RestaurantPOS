using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public static class SettingsEndpoints
    {
        public static WebApplication MapSettingsEndpoints(this WebApplication app)
        {
            app.MapGet("/api/store/settings", async (AppDbContext db) =>
            {
                var outlet = await Load(db);
                return outlet == null ? Results.NotFound() : Results.Ok(StoreRules.ToSettings(outlet));
            })
            .WithName("GetOutletSettingsForScreens")
            .WithSummary("Outlet settings the POS and kitchen screens need");

            var admin = app.MapGroup("/api/admin/settings")
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithTags("Settings");

            admin.MapGet("/", async (AppDbContext db) =>
            {
                var outlet = await Load(db);
                return outlet == null ? Results.NotFound() : Results.Ok(StoreRules.ToSettings(outlet));
            })
            .WithName("GetOutletSettings")
            .WithSummary("Outlet settings");

            admin.MapPut("/", async (OutletSettingsDto dto, AppDbContext db) =>
            {
                var outlet = await Load(db);
                if (outlet == null) return Results.NotFound();
                var errors = StoreRules.Validate(dto);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                StoreRules.Apply(outlet, dto);
                await db.SaveChangesAsync();
                return Results.Ok(StoreRules.ToSettings(outlet));
            })
            .WithName("UpdateOutletSettings")
            .WithSummary("Change outlet settings");

            admin.MapPost("/preset", async (PresetRequest req, AppDbContext db) =>
            {
                var outlet = await Load(db);
                if (outlet == null) return Results.NotFound();
                if (req.Preset == null || !OutletSettings.Presets.Contains(req.Preset))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["preset"] = new[] { "Choose one of: " + string.Join(", ", OutletSettings.Presets) + "." } });
                outlet.Settings = outlet.GetSettings().WithPreset(req.Preset).ToJson();
                await db.SaveChangesAsync();
                return Results.Ok(StoreRules.ToSettings(outlet));
            })
            .WithName("ApplyOutletPreset")
            .WithSummary("Apply a ready-made setup: dine-in, cafe or takeaway-counter");

            return app;
        }

        private static Task<Outlet?> Load(AppDbContext db) =>
            db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
    }
}
