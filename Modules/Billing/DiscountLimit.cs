using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;

namespace POSSystem.Billing
{
    public record DiscountLimitDto(decimal? CashierMaxDiscountPercent);

    // Owner: the most discount (percent of the bill) a cashier may give without a manager PIN. Empty = no limit.
    public static class DiscountLimitEndpoints
    {
        public static WebApplication MapDiscountLimitEndpoints(this WebApplication app)
        {
            var admin = app.MapGroup("/api/admin/discount-limit").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Settings");

            admin.MapGet("/", async (AppDbContext db) =>
            {
                var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
                return outlet == null ? Results.NotFound() : Results.Ok(new DiscountLimitDto(outlet.GetSettings().CashierMaxDiscountPercent));
            }).WithName("GetDiscountLimit").WithSummary("Most discount a cashier may give without a manager PIN");

            admin.MapPut("/", async (DiscountLimitDto request, AppDbContext db) =>
            {
                if (request.CashierMaxDiscountPercent is < 0 or > 100)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["cashiermaxdiscountpercent"] = new[] { "Enter 0 to 100, or leave it empty for no limit." } });
                var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
                if (outlet == null) return Results.NotFound();
                var settings = outlet.GetSettings();
                settings.CashierMaxDiscountPercent = request.CashierMaxDiscountPercent;
                outlet.Settings = settings.ToJson();
                await db.SaveChangesAsync();
                return Results.Ok(new DiscountLimitDto(settings.CashierMaxDiscountPercent));
            }).WithName("SetDiscountLimit").WithSummary("Set the most discount a cashier may give without a manager PIN");

            return app;
        }
    }
}
