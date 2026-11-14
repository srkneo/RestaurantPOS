using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Staff
{
    public record StaffDto(string Id, string Email, string DisplayName, string Role, bool IsActive, bool IsDevice);
    public record CreateStaffRequest(string? Email, string? DisplayName, string? Role, string? Password, bool IsDevice);
    public record UpdateStaffRequest(string? DisplayName, string? Role, bool IsActive);
    public record ResetPasswordRequest(string? Password);

    // Restaurant owner (admin role) manages the staff of their own restaurant only.
    public static class StaffEndpoints
    {
        public static WebApplication MapStaffEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/staff").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Staff");

            group.MapGet("/", async (ClaimsPrincipal caller, UserManager<AppUser> users) =>
            {
                var tenantId = TenantOf(caller);
                var list = await users.Users.Where(u => u.TenantId == tenantId).OrderBy(u => u.Email).ToListAsync();
                var result = new List<StaffDto>();
                foreach (var u in list) result.Add(await ToDto(users, u));
                return Results.Ok(result);
            }).WithName("GetStaff").WithSummary("List the staff of this restaurant");

            group.MapPost("/", async (CreateStaffRequest req, ClaimsPrincipal caller, UserManager<AppUser> users) =>
            {
                var errors = Validate(req.Email, req.DisplayName, req.Role, req.IsDevice);
                if (string.IsNullOrEmpty(req.Password)) errors["password"] = new[] { "Enter a password." };
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var email = req.Email!.Trim();
                var user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = req.DisplayName!.Trim(),
                    IsActive = true,
                    IsDevice = req.IsDevice,
                    TenantId = TenantOf(caller),
                    OutletId = OutletOf(caller)
                };
                var created = await users.CreateAsync(user, req.Password!);
                if (!created.Succeeded) return Results.ValidationProblem(ToErrors(created));
                await users.AddToRoleAsync(user, req.Role!);
                return Results.Created($"/api/staff/{user.Id}", await ToDto(users, user));
            }).WithName("CreateStaff").WithSummary("Add a staff member or kitchen screen device");

            group.MapPut("/{id}", async (string id, UpdateStaffRequest req, ClaimsPrincipal caller, UserManager<AppUser> users) =>
            {
                var user = await FindInTenant(users, id, caller);
                if (user is null) return Results.NotFound();

                var errors = Validate(user.Email, req.DisplayName, req.Role, user.IsDevice);
                var isSelf = user.Id == caller.FindFirstValue(ClaimTypes.NameIdentifier);
                if (isSelf && (!req.IsActive || req.Role != "admin"))
                    errors["role"] = new[] { "You cannot remove your own admin access." };
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var wasActive = user.IsActive;
                var currentRoles = await users.GetRolesAsync(user);
                var roleChanged = !(currentRoles.Count == 1 && currentRoles[0] == req.Role);
                if (roleChanged)
                {
                    if (currentRoles.Count > 0) await users.RemoveFromRolesAsync(user, currentRoles);
                    await users.AddToRoleAsync(user, req.Role!);
                }

                user.DisplayName = req.DisplayName!.Trim();
                user.IsActive = req.IsActive;
                var updated = await users.UpdateAsync(user);
                if (!updated.Succeeded) return Results.ValidationProblem(ToErrors(updated));

                // Ends their current sessions at the next security check.
                if (roleChanged || (wasActive && !user.IsActive)) await users.UpdateSecurityStampAsync(user);
                return Results.Ok(await ToDto(users, user));
            }).WithName("UpdateStaff").WithSummary("Change name, role or active status");

            group.MapPost("/{id}/password", async (string id, ResetPasswordRequest req, ClaimsPrincipal caller, UserManager<AppUser> users) =>
            {
                var user = await FindInTenant(users, id, caller);
                if (user is null) return Results.NotFound();
                if (string.IsNullOrEmpty(req.Password))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = new[] { "Enter a password." } });

                // Check the new password first so a weak one never leaves the account without a password.
                foreach (var validator in users.PasswordValidators)
                {
                    var check = await validator.ValidateAsync(users, user, req.Password);
                    if (!check.Succeeded) return Results.ValidationProblem(ToErrors(check));
                }

                await users.RemovePasswordAsync(user);
                var added = await users.AddPasswordAsync(user, req.Password);
                if (!added.Succeeded) return Results.ValidationProblem(ToErrors(added));
                await users.ResetAccessFailedCountAsync(user);
                await users.SetLockoutEndDateAsync(user, null);
                return Results.NoContent();
            }).WithName("ResetStaffPassword").WithSummary("Set a new password for a staff member");

            return app;
        }

        private static int TenantOf(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue("tenant_id"), out var id) ? id : 0;

        private static int OutletOf(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue("outlet_id"), out var id) ? id : 1;

        private static async Task<AppUser?> FindInTenant(UserManager<AppUser> users, string id, ClaimsPrincipal caller)
        {
            var user = await users.FindByIdAsync(id);
            return user != null && user.TenantId == TenantOf(caller) ? user : null;
        }

        private static async Task<StaffDto> ToDto(UserManager<AppUser> users, AppUser u)
        {
            var roles = await users.GetRolesAsync(u);
            return new StaffDto(u.Id, u.Email ?? string.Empty, u.DisplayName ?? string.Empty, roles.FirstOrDefault() ?? string.Empty, u.IsActive, u.IsDevice);
        }

        private static Dictionary<string, string[]> Validate(string? email, string? displayName, string? role, bool isDevice)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
                errors["displayname"] = new[] { "Enter a name of up to 100 characters." };
            if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email.Trim()))
                errors["email"] = new[] { "Enter a valid email." };
            if (string.IsNullOrWhiteSpace(role) || !AuthSetup.Roles.Contains(role))
                errors["role"] = new[] { "Choose one of: " + string.Join(", ", AuthSetup.Roles) + "." };
            else if (isDevice && role != "kitchen")
                errors["role"] = new[] { "A kitchen screen device must have the kitchen role." };
            return errors;
        }

        private static Dictionary<string, string[]> ToErrors(IdentityResult result) =>
            result.Errors
                .GroupBy(e => e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "password"
                    : e.Code.Contains("Email", StringComparison.OrdinalIgnoreCase) || e.Code.Contains("UserName", StringComparison.OrdinalIgnoreCase) ? "email"
                    : "general")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
    }
}
