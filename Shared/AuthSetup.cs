using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using POSSystem.Models;

namespace POSSystem.Data
{
    public record LoginRequest(string Email, string Password);

    public class AppUserClaimsFactory : UserClaimsPrincipalFactory<AppUser, IdentityRole>
    {
        public AppUserClaimsFactory(UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options)
            : base(userManager, roleManager, options) { }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);
            identity.AddClaim(new Claim("tenant_id", user.TenantId.ToString()));
            identity.AddClaim(new Claim("outlet_id", user.OutletId.ToString()));
            return identity;
        }
    }

    public static class AuthSetup
    {
        public static readonly string[] Roles = { "admin", "manager", "cashier", "kitchen" };

        public static IServiceCollection AddPosAuth(this IServiceCollection services)
        {
            services.AddIdentity<AppUser, IdentityRole>(o =>
                {
                    o.Password.RequiredLength = 8;
                    o.User.RequireUniqueEmail = true;
                    o.Lockout.MaxFailedAccessAttempts = 5;
                })
                .AddEntityFrameworkStores<AppDbContext>()
                .AddClaimsPrincipalFactory<AppUserClaimsFactory>();

            services.ConfigureApplicationCookie(o =>
            {
                o.Cookie.Name = "pos.auth";
                o.ExpireTimeSpan = TimeSpan.FromHours(12);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            });

            services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            return services;
        }

        public static WebApplication UsePosAuth(this WebApplication app)
        {
            app.UseAuthentication();
            app.UseAuthorization();

            app.Use(async (ctx, next) =>
            {
                if (ctx.User.Identity?.IsAuthenticated == true)
                {
                    if (int.TryParse(ctx.User.FindFirst("tenant_id")?.Value, out var tenantId))
                        ctx.RequestServices.GetRequiredService<AppDbContext>().CurrentTenantId = tenantId;

                    bool InAny(params string[] roles) => roles.Any(ctx.User.IsInRole);
                    if (ctx.Request.Path.StartsWithSegments("/api/orders"))
                    {
                        if (HttpMethods.IsPost(ctx.Request.Method) && !InAny("cashier", "manager", "admin")) { ctx.Response.StatusCode = 403; return; }
                        if (HttpMethods.IsPut(ctx.Request.Method) && !InAny("kitchen", "manager", "admin")) { ctx.Response.StatusCode = 403; return; }
                    }
                }
                await next();
            });
            return app;
        }

        public static WebApplication MapPosAuth(this WebApplication app)
        {
            app.MapPost("/api/auth/login", async (LoginRequest req, SignInManager<AppUser> signInManager) =>
            {
                var result = await signInManager.PasswordSignInAsync(req.Email, req.Password, isPersistent: false, lockoutOnFailure: true);
                return result.Succeeded ? Results.Ok() : Results.Unauthorized();
            }).AllowAnonymous();

            app.MapPost("/api/auth/logout", async (SignInManager<AppUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.Ok();
            });

            app.MapGet("/api/auth/me", (ClaimsPrincipal user) => Results.Ok(new
            {
                email = user.FindFirst(ClaimTypes.Email)?.Value,
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
                tenantId = user.FindFirst("tenant_id")?.Value,
                outletId = user.FindFirst("outlet_id")?.Value
            }));
            return app;
        }
    }

    public static class IdentitySeed
    {
        public static async Task EnsureAsync(IServiceProvider services, IConfiguration config)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in AuthSetup.Roles)
            {
                if (!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole(role));
            }

            var email = config["Admin:Email"];
            var password = config["Admin:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

            var userManager = services.GetRequiredService<UserManager<AppUser>>();
            if (await userManager.FindByEmailAsync(email) != null) return;

            var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = 1, OutletId = 1 };
            var created = await userManager.CreateAsync(admin, password);
            if (created.Succeeded) await userManager.AddToRoleAsync(admin, "admin");
        }
    }
}
