using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
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
            identity.AddClaim(new Claim("display_name", user.DisplayName ?? user.Email ?? string.Empty));
            identity.AddClaim(new Claim("is_device", user.IsDevice ? "true" : "false"));
            return identity;
        }
    }

    public static class AuthSetup
    {
        public static readonly string[] Roles = { "admin", "manager", "cashier", "kitchen" };
        public const int DeviceLoginDays = 30;

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

            // Deactivated staff and changed passwords end other sessions within a minute.
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

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
            app.MapPost("/api/auth/login", async (LoginRequest req, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager) =>
            {
                if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password)) return Results.Unauthorized();

                var user = await userManager.FindByEmailAsync(req.Email.Trim());
                if (user is null || !user.IsActive) return Results.Unauthorized();

                var check = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
                if (!check.Succeeded) return Results.Unauthorized();

                // Kitchen screen devices keep a persistent 30-day session; people get a 12-hour browser session.
                var props = new AuthenticationProperties { IsPersistent = user.IsDevice };
                if (user.IsDevice) props.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(DeviceLoginDays);
                await signInManager.SignInAsync(user, props);
                return Results.Ok();
            }).AllowAnonymous().WithName("Login").WithSummary("Sign in");

            app.MapPost("/api/auth/logout", async (SignInManager<AppUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.Ok();
            }).WithName("Logout").WithSummary("Sign out");

            app.MapGet("/api/auth/me", (ClaimsPrincipal user) => Results.Ok(new
            {
                email = user.FindFirst(ClaimTypes.Email)?.Value,
                displayName = user.FindFirst("display_name")?.Value,
                isDevice = user.FindFirst("is_device")?.Value == "true",
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
                tenantId = user.FindFirst("tenant_id")?.Value,
                outletId = user.FindFirst("outlet_id")?.Value
            })).WithName("Me").WithSummary("Who is signed in");
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

            var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Owner", TenantId = 1, OutletId = 1 };
            var created = await userManager.CreateAsync(admin, password);
            if (created.Succeeded) await userManager.AddToRoleAsync(admin, "admin");
        }
    }
}
