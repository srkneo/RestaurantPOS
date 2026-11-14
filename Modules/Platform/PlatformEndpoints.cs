using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public record PlatformTenantDto(int Id, string Name, bool IsActive, DateTime CreatedAt, int OutletCount, int StaffCount, DateTime? LastOrderAt);

    public record CreateTenantRequest(string? RestaurantName, string? OutletName, string? OwnerName, string? OwnerEmail, string? OwnerPassword);

    // The POS owner's screen: list restaurants, add one with its owner, disable or enable it.
    public static class PlatformEndpoints
    {
        public const int StarterTables = 10;

        public static WebApplication MapPlatformEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/platform").RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole)).WithTags("Platform");

            group.MapGet("/tenants", async (AppDbContext db) => Results.Ok(await ListAsync(db)))
                .WithName("GetPlatformTenants").WithSummary("All restaurants with outlet and staff counts");

            group.MapPost("/tenants", async (CreateTenantRequest req, AppDbContext db, UserManager<AppUser> users) =>
            {
                var errors = await ValidateAsync(req, users);
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var tenant = new Tenant { Name = req.RestaurantName!.Trim(), IsActive = true };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();

                var outlet = new Outlet { TenantId = tenant.Id, Name = req.OutletName!.Trim() };
                db.Outlets.Add(outlet);
                await db.SaveChangesAsync();

                // Tables and the owner belong to the new restaurant, so switch to it while adding them.
                var tenantBefore = db.CurrentTenantId;
                var outletBefore = db.CurrentOutletId;
                db.CurrentTenantId = tenant.Id;
                db.CurrentOutletId = outlet.Id;
                try
                {
                    for (var i = 1; i <= StarterTables; i++)
                    {
                        db.RestaurantTables.Add(new RestaurantTable { TableNumber = i.ToString(), QRCodeString = $"t{tenant.Id}-table-{i}-qr", SortOrder = i });
                    }
                    await db.SaveChangesAsync();

                    var email = req.OwnerEmail!.Trim();
                    var owner = new AppUser
                    {
                        UserName = email, Email = email, EmailConfirmed = true, DisplayName = req.OwnerName!.Trim(),
                        TenantId = tenant.Id, OutletId = outlet.Id
                    };
                    var created = await users.CreateAsync(owner, req.OwnerPassword!);
                    if (!created.Succeeded)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["owneremail"] = created.Errors.Select(e => e.Description).ToArray()
                        });
                    }
                    await users.AddToRoleAsync(owner, "admin");
                }
                finally
                {
                    db.CurrentTenantId = tenantBefore;
                    db.CurrentOutletId = outletBefore;
                }

                var dto = (await ListAsync(db)).First(t => t.Id == tenant.Id);
                return Results.Created($"/api/platform/tenants/{tenant.Id}", dto);
            }).WithName("CreatePlatformTenant").WithSummary("Add a restaurant with its first outlet, 10 tables and an owner");

            group.MapPost("/tenants/{id:int}/disable", (int id, AppDbContext db, TenantStatus status) => SetActive(id, false, db, status))
                .WithName("DisablePlatformTenant").WithSummary("Stop a restaurant: its staff can no longer sign in");

            group.MapPost("/tenants/{id:int}/enable", (int id, AppDbContext db, TenantStatus status) => SetActive(id, true, db, status))
                .WithName("EnablePlatformTenant").WithSummary("Let a disabled restaurant sign in again");

            return app;
        }

        private static async Task<IResult> SetActive(int id, bool active, AppDbContext db, TenantStatus status)
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return Results.NotFound();
            if (tenant.IsActive != active)
            {
                tenant.IsActive = active;
                await db.SaveChangesAsync();
            }
            status.Forget(id);
            return Results.Ok((await ListAsync(db)).First(t => t.Id == id));
        }

        // Separate simple queries, joined in memory: the list is short and this works the same on every database.
        private static async Task<List<PlatformTenantDto>> ListAsync(AppDbContext db)
        {
            var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
            var outlets = await db.Outlets.AsNoTracking().GroupBy(o => o.TenantId)
                .Select(g => new { TenantId = g.Key, Count = g.Count() }).ToListAsync();
            var staff = await db.Users.AsNoTracking().Where(u => !u.IsPlatformAdmin).GroupBy(u => u.TenantId)
                .Select(g => new { TenantId = g.Key, Count = g.Count() }).ToListAsync();
            var lastOrders = await db.KitchenOrders.IgnoreQueryFilters().AsNoTracking().GroupBy(o => o.TenantId)
                .Select(g => new { TenantId = g.Key, Last = g.Max(o => o.CreatedAt) }).ToListAsync();

            return tenants.Select(t => new PlatformTenantDto(
                t.Id,
                t.Name,
                t.IsActive,
                t.CreatedAt,
                outlets.FirstOrDefault(o => o.TenantId == t.Id)?.Count ?? 0,
                staff.FirstOrDefault(s => s.TenantId == t.Id)?.Count ?? 0,
                lastOrders.FirstOrDefault(o => o.TenantId == t.Id)?.Last)).ToList();
        }

        // All checks before anything is saved, so a bad form never leaves a half-made restaurant.
        private static async Task<Dictionary<string, string[]>> ValidateAsync(CreateTenantRequest req, UserManager<AppUser> users)
        {
            var errors = new Dictionary<string, string[]>();
            void Name(string key, string? value, string label)
            {
                var v = value?.Trim() ?? string.Empty;
                if (v.Length == 0) errors[key] = new[] { $"Enter the {label}." };
                else if (v.Length > 200) errors[key] = new[] { $"The {label} can be at most 200 characters." };
            }
            Name("restaurantname", req.RestaurantName, "restaurant name");
            Name("outletname", req.OutletName, "outlet name");
            Name("ownername", req.OwnerName, "owner's name");
            if (!errors.ContainsKey("ownername") && req.OwnerName!.Trim().Length > 100)
                errors["ownername"] = new[] { "The owner's name can be at most 100 characters." };

            var email = req.OwnerEmail?.Trim() ?? string.Empty;
            if (email.Length == 0 || !new EmailAddressAttribute().IsValid(email))
                errors["owneremail"] = new[] { "Enter a valid email address." };
            else if (await users.FindByEmailAsync(email) != null)
                errors["owneremail"] = new[] { "This email is already used by another account." };

            if (string.IsNullOrEmpty(req.OwnerPassword))
            {
                errors["ownerpassword"] = new[] { "Enter a password." };
            }
            else
            {
                var probe = new AppUser { UserName = email, Email = email };
                var problems = new List<string>();
                foreach (var validator in users.PasswordValidators)
                {
                    var result = await validator.ValidateAsync(users, probe, req.OwnerPassword);
                    if (!result.Succeeded) problems.AddRange(result.Errors.Select(e => e.Description));
                }
                if (problems.Count > 0) errors["ownerpassword"] = problems.ToArray();
            }
            return errors;
        }
    }
}
