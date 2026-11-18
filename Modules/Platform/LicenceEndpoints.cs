using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace POSSystem.Platform
{
    public static class LicenceEndpoints
    {
        public static WebApplication MapLicenceEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/licence").WithTags("Licence");

            group.MapGet("/status", async (LicenceService service, CancellationToken ct) =>
                Results.Ok(service.Describe(await service.GetStatus(ct))))
                .AllowAnonymous()
                .WithName("GetLicenceStatus").WithSummary("Licence state of this PC (always active when no licence is required)");

            group.MapPost("/activate", (ActivateRequest request, LicenceService service, CancellationToken ct) =>
                Run(async () => Results.Ok(service.Describe(await service.Activate(request.Key, ct)))))
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithName("ActivateLicence").WithSummary("Enter the licence key and fetch the licence from the licence server");

            group.MapPost("/refresh", (LicenceService service, CancellationToken ct) =>
                Run(async () => Results.Ok(service.Describe(await service.RenewStored(ct)))))
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithName("RefreshLicence").WithSummary("Fetch a fresh licence now with the key already entered");

            group.MapGet("/updates", (ShopUpdates updates) => Results.Ok(updates.Describe()))
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithName("GetShopUpdates").WithSummary("Automatic updates on this shop PC: support's setting, the last result, and whether an update was asked for");

            group.MapPost("/updates/run", (ShopUpdates updates) => Run(() => Task.FromResult(Results.Ok(updates.RequestNow()))))
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithName("RunShopUpdate").WithSummary("Update now: the PC starts the update within 2 minutes");

            group.MapGet("/backups", (ShopBackups backups) => Results.Ok(backups.Describe()))
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithName("GetShopBackups").WithSummary("Backups on this shop PC: the newest backup, the newest copy to the USB disk or NAS, and a warning when they are old");

            group.MapPost("/backups/run", (ShopBackups backups) => Run(() => Task.FromResult(Results.Ok(backups.RequestNow()))))
                .RequireAuthorization(p => p.RequireRole("admin"))
                .WithName("RunShopBackup").WithSummary("Back up now: a fresh backup is made and copied within 2 minutes");

            return app;
        }

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try { return await action(); }
            catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }
        }
    }
}
