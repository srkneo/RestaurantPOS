using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;

namespace POSSystem.Platform
{
    public static class StoreEndpoints
    {
        public static WebApplication MapStoreEndpoints(this WebApplication app)
        {
            app.MapGet("/api/store", async (AppDbContext db) =>
            {
                var (tenant, outlet) = await Load(db);
                if (tenant == null || outlet == null) return Results.NotFound();
                var hasLogo = await db.OutletLogos.AnyAsync(l => l.OutletId == outlet.Id);
                return Results.Ok(StoreRules.ToSummary(tenant, outlet, hasLogo));
            }).WithName("GetStore").WithSummary("Restaurant and outlet name for the screens");

            var admin = app.MapGroup("/api/admin/store").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Store");

            admin.MapGet("/", async (AppDbContext db) =>
            {
                var (tenant, outlet) = await Load(db);
                if (tenant == null || outlet == null) return Results.NotFound();
                var hasLogo = await db.OutletLogos.AnyAsync(l => l.OutletId == outlet.Id);
                return Results.Ok(StoreRules.ToProfile(tenant, outlet, hasLogo));
            }).WithName("GetStoreProfile").WithSummary("Store profile shown on bills");

            admin.MapPut("/", async (StoreProfileRequest req, AppDbContext db) =>
            {
                var (tenant, outlet) = await Load(db);
                if (tenant == null || outlet == null) return Results.NotFound();
                var errors = StoreRules.Validate(req);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                StoreRules.Apply(tenant, outlet, req);
                await db.SaveChangesAsync();
                var hasLogo = await db.OutletLogos.AnyAsync(l => l.OutletId == outlet.Id);
                return Results.Ok(StoreRules.ToProfile(tenant, outlet, hasLogo));
            }).WithName("UpdateStoreProfile").WithSummary("Change the store profile");

            return app;
        }

        private static async Task<(Tenant? Tenant, Outlet? Outlet)> Load(AppDbContext db)
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == db.CurrentTenantId);
            var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
            return (tenant, outlet);
        }
    }
}
