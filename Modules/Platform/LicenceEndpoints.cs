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

            return app;
        }

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try { return await action(); }
            catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }
        }
    }
}
