using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using POSSystem.Models;

namespace POSSystem.Data
{
    public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public static class PasswordChangeEndpoints
    {
        public static WebApplication MapPasswordChangeEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/auth/password")
                .RequireAuthorization()
                .WithTags("Sign in");

            group.MapPost("/", async (ChangePasswordRequest request, ClaimsPrincipal caller, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
            {
                var id = caller.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(id)) return Results.NotFound();

                var me = await users.FindByIdAsync(id);
                if (me == null) return Results.NotFound();

                if (me.IsDevice)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = new[] { "A kitchen screen device cannot change its password here." } });

                if (!await users.CheckPasswordAsync(me, request.CurrentPassword ?? ""))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = new[] { "The current password is not right." } });

                var newPassword = request.NewPassword ?? string.Empty;
                if (string.IsNullOrEmpty(newPassword) || newPassword.Length > 128)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = new[] { "Type a new password (at most 128 characters)." } });

                if (newPassword == request.CurrentPassword)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = new[] { "Choose a password different from the current one." } });

                if (me.IsPlatformAdmin && newPassword.Length < 8)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = new[] { "Use at least 8 characters." } });

                if (me.IsPlatformAdmin)
                {
                    me.PasswordHash = users.PasswordHasher.HashPassword(me, newPassword);
                    await users.UpdateAsync(me);
                }
                else
                {
                    var result = await users.ChangePasswordAsync(me, request.CurrentPassword!, newPassword!);
                    if (!result.Succeeded)
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = new[] { string.Join(" ", result.Errors.Select(e => e.Description)) } });
                }

                await users.UpdateSecurityStampAsync(me);
                await signIn.RefreshSignInAsync(me);
                return Results.Ok();
            }).WithName("ChangeMyPassword").WithSummary("Change my own password");

            return app;
        }
    }
}
