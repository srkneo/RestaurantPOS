using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public static class SupportAccess
    {
        public const string Role = "support";

        public static string EmailFor(int tenantId) => $"support-t{tenantId}@support.local";

        public static WebApplication UseSupportGuard(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                if (context.User.IsInRole(Role) && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                {
                    var path = context.Request.Path;
                    var regexes = new List<Regex>
                    {
                        new Regex("^/api/bills/\\d+/(payments|void|credit-note)$", RegexOptions.IgnoreCase),
                        new Regex("^/api/staff(/.*)?$", RegexOptions.IgnoreCase),
                        new Regex("^/api/auth/password$", RegexOptions.IgnoreCase),
                        new Regex("^/api/auth/2fa(/.*)?$", RegexOptions.IgnoreCase)
                    };

                    if (regexes.Any(r => r.IsMatch(path)))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new { message = "A support session cannot take payments, void bills, give refunds or change staff and passwords." });
                        return;
                    }
                }
                await next();
            });
            return app;
        }

        public static WebApplication MapSupportAccessEndpoints(this WebApplication app)
        {
            var group = app.MapPost("/api/platform/tenants/{id:int}/support-login", async (int id, ClaimsPrincipal caller, AppDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole> roles, SignInManager<AppUser> signIn) =>
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
                if (tenant == null || !tenant.IsActive)
                    return Results.NotFound();

                var outlet = await db.Outlets.Where(o => o.TenantId == id).OrderBy(o => o.Id).FirstOrDefaultAsync();
                if (outlet == null)
                    return Results.NotFound();

                if (!outlet.GetSettings().AllowSupportAccess)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["support"] = new[] { "This restaurant has turned off support access." } });

                if (!await roles.RoleExistsAsync(Role))
                {
                    await roles.CreateAsync(new IdentityRole(Role));
                }

                var email = EmailFor(id);
                var user = await users.FindByEmailAsync(email);
                if (user == null)
                {
                    user = new AppUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        DisplayName = "Support (platform)",
                        TenantId = id,
                        OutletId = outlet.Id,
                        IsSupport = true
                    };
                    await users.CreateAsync(user);
                    await users.AddToRolesAsync(user, new[] { "admin", Role });
                }
                else
                {
                    user.IsActive = true;
                    await users.UpdateAsync(user);
                }

                db.CurrentTenantId = id;
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = id,
                    Action = "SupportLogin",
                    EntityType = "Support",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { support = "Support session started by " + (caller.FindFirstValue(ClaimTypes.Email) ?? "the platform owner") })
                });
                await db.SaveChangesAsync();

                await signIn.SignOutAsync();
                await signIn.SignInAsync(user, new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                    AllowRefresh = false
                });

                return Results.Ok(new { tenantId = id });
            })
            .RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole))
            .WithTags("Platform")
            .WithName("SupportLogin")
            .WithSummary("Open a restaurant as Support for 2 hours");

            return app;
        }
    }
}
