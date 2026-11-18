using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using POSSystem.Data;
using POSSystem.Models;

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

            // Owner forgot the password on a shop PC: support reads a one-time code from the platform page.
            app.MapPost("/api/auth/owner-reset", async (OwnerResetRequest request, LicenceService licence, UserManager<AppUser> users, CancellationToken ct) =>
            {
                if (!licence.Required) return Results.NotFound(new { message = "Ask support to reset your password." });
                var email = (request.Email ?? "").Trim();
                var password = request.NewPassword ?? "";
                if (email == "" || string.IsNullOrWhiteSpace(request.Code) || password == "")
                    return Results.BadRequest(new { message = "Enter your email, the code from support and a new password." });

                // The newest code comes with a licence renewal (needs internet for a moment; an earlier one may already be here).
                try { await licence.RenewStored(ct); }
                catch (Exception e) when (e is not OperationCanceledException) { }

                const string wrong = "The email or the code is not right, or the code has expired. Ask support for a new code.";
                var user = await users.FindByEmailAsync(email);
                if (user is null || !user.IsActive || user.IsPlatformAdmin || !await users.IsInRoleAsync(user, "admin")) return Results.BadRequest(new { message = wrong });
                if (!await licence.CheckOwnerReset(request.Code, use: false, ct)) return Results.BadRequest(new { message = wrong });

                // The password rules are checked before the code is used up, so a weak password does not waste it.
                foreach (var validator in users.PasswordValidators)
                {
                    var check = await validator.ValidateAsync(users, user, password);
                    if (!check.Succeeded) return Results.BadRequest(new { message = string.Join(" ", check.Errors.Select(e => e.Description)) });
                }
                if (!await licence.CheckOwnerReset(request.Code, use: true, ct)) return Results.BadRequest(new { message = wrong });

                await users.RemovePasswordAsync(user);
                var added = await users.AddPasswordAsync(user, password);
                if (!added.Succeeded) return Results.BadRequest(new { message = string.Join(" ", added.Errors.Select(e => e.Description)) });
                await users.SetLockoutEndDateAsync(user, null);
                await users.ResetAccessFailedCountAsync(user);
                await users.UpdateSecurityStampAsync(user);
                return Results.Ok(new { message = "Password changed. Sign in with the new password." });
            })
                .AllowAnonymous().RequireRateLimiting(RateLimits.Login).WithTags("Auth")
                .WithName("ResetOwnerPassword").WithSummary("Shop PC: the owner sets a new password with a one-time code from support");

            return app;
        }

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try { return await action(); }
            catch (ArgumentException e) { return Results.BadRequest(new { message = e.Message }); }
        }
    }
}
