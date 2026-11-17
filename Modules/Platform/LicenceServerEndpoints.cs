using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using POSSystem.Data;

namespace POSSystem.Platform
{
    // Cloud side of the licence: shop PCs renew here, and the platform owner manages licences.
    public static class LicenceServerEndpoints
    {
        public static WebApplication MapLicenceServerEndpoints(this WebApplication app)
        {
            // Open to shop PCs (they have no sign-in), so it is rate limited like sign-in and needs the long random key.
            app.MapPost("/api/licence/renew", (RenewRequest request, LicenceIssuer issuer, CancellationToken ct) =>
                Run(async () => Results.Ok(await issuer.Renew(request, ct))))
                .AllowAnonymous().RequireRateLimiting(RateLimits.Login)
                .WithTags("Licence")
                .WithName("RenewLicence").WithSummary("A shop PC asks for a fresh licence lease with its key");

            var group = app.MapGroup("/api/platform/licences").RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole)).WithTags("Platform");

            group.MapGet("/", async (LicenceIssuer issuer, CancellationToken ct) => Results.Ok(await issuer.List(ct)))
                .WithName("GetShopLicences").WithSummary("All shop licences with their state and last contact");

            group.MapPost("/", (CreateShopLicenceRequest request, LicenceIssuer issuer, CancellationToken ct) =>
            {
                var errors = LicenceIssuer.Validate(request);
                if (errors.Count > 0) return Task.FromResult(Results.ValidationProblem(errors));
                return Run(async () => Results.Ok(await issuer.Create(request, ct)));
            })
                .WithName("CreateShopLicence").WithSummary("Create a licence for a shop (a 30-day trial or a yearly licence)");

            group.MapPost("/{id:int}/extend", (int id, ExtendShopLicenceRequest request, LicenceIssuer issuer, CancellationToken ct) =>
                Run(async () => Results.Ok(await issuer.Extend(id, request, ct))))
                .WithName("ExtendShopLicence").WithSummary("Add days to the paid period of a licence");

            group.MapPost("/{id:int}/revoke", (int id, LicenceIssuer issuer, CancellationToken ct) =>
                Run(async () => Results.Ok(await issuer.SetRevoked(id, true, ct))))
                .WithName("RevokeShopLicence").WithSummary("Stop a licence (the shop PC locks when its lease runs out)");

            group.MapPost("/{id:int}/restore", (int id, LicenceIssuer issuer, CancellationToken ct) =>
                Run(async () => Results.Ok(await issuer.SetRevoked(id, false, ct))))
                .WithName("RestoreShopLicence").WithSummary("Start a stopped licence again");

            group.MapPost("/{id:int}/reset-device", (int id, LicenceIssuer issuer, CancellationToken ct) =>
                Run(async () => Results.Ok(await issuer.ResetDevice(id, ct))))
                .WithName("ResetShopLicenceDevice").WithSummary("Let the licence be used on a different PC");

            return app;
        }

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try { return await action(); }
            catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }
            catch (KeyNotFoundException e) { return Results.NotFound(new { message = e.Message }); }
            catch (LicenceRefusedException e) { return Results.Json(new { message = e.Message }, statusCode: 403); }
        }
    }
}
