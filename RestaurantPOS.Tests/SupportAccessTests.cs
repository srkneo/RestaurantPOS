using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Support login: the platform owner opens a restaurant as Support; money and password actions stay blocked.
    public class SupportAccessTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SupportAccessTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("RateLimits:LoginPerMinute", "1000");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                });
            });
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1", string? extraRole = null)
        {
            var client = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            client.DefaultRequestHeaders.Add("X-Test-User", "caller-1");
            if (extraRole != null) client.DefaultRequestHeaders.Add("X-Test-Extra-Role", extraRole);
            return client;
        }

        private static async Task<int> MakeRestaurant(WebApplicationFactory<Program> f, string settings = "{}")
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Support Test" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            db.CurrentTenantId = tenant.Id;
            db.Outlets.Add(new Outlet { TenantId = tenant.Id, Name = "Main", Settings = settings });
            await db.SaveChangesAsync();
            return tenant.Id;
        }

        private static string Email(int tenantId) => $"support-t{tenantId}@support.local";

        [Fact]
        public void Settings_AllowSupportAccess_IsOnByDefault()
        {
            Assert.True(new OutletSettings().AllowSupportAccess);
        }

        [Fact]
        public async Task Platform_OpensRestaurantAsSupport()
        {
            using var f = Build();
            var id = await MakeRestaurant(f);
            var res = await As(f, "platform", "0").PostAsync($"/api/platform/tenants/{id}/support-login", Json(new { }));
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            Assert.True(res.Headers.Contains("Set-Cookie"));

            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var support = await users.FindByEmailAsync(Email(id));
            Assert.NotNull(support);
            Assert.True(support!.IsSupport);
            Assert.Equal(id, support.TenantId);
            Assert.True(await users.IsInRoleAsync(support, "admin"));
            Assert.True(await users.IsInRoleAsync(support, "support"));

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = await db.AuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.TenantId == id && a.Action == "SupportLogin");
            Assert.NotNull(audit);
            // Details is a json column in PostgreSQL: it must be valid JSON (the in-memory database does not check).
            using (System.Text.Json.JsonDocument.Parse(audit!.Details!)) { }
        }

        [Fact]
        public async Task SecondSupportLogin_ReusesTheSameUser()
        {
            using var f = Build();
            var id = await MakeRestaurant(f);
            var client = As(f, "platform", "0");
            Assert.True((await client.PostAsync($"/api/platform/tenants/{id}/support-login", Json(new { }))).IsSuccessStatusCode);
            Assert.True((await client.PostAsync($"/api/platform/tenants/{id}/support-login", Json(new { }))).IsSuccessStatusCode);
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Equal(1, users.Users.Count(u => u.Email == Email(id)));
        }

        [Fact]
        public async Task Refused_WhenTheOwnerTurnedSupportAccessOff()
        {
            using var f = Build();
            var id = await MakeRestaurant(f, "{\"allowSupportAccess\":false}");
            var res = await As(f, "platform", "0").PostAsync($"/api/platform/tenants/{id}/support-login", Json(new { }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Null(await users.FindByEmailAsync(Email(id)));
        }

        [Fact]
        public async Task OnlyThePlatformOwner_CanOpenSupport()
        {
            using var f = Build();
            var id = await MakeRestaurant(f);
            var res = await As(f, "admin").PostAsync($"/api/platform/tenants/{id}/support-login", Json(new { }));
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        [Fact]
        public async Task SupportSession_CannotTouchMoneyStaffOrPasswords()
        {
            using var f = Build();
            var support = As(f, "admin", extraRole: "support");  // a real Support user is admin + support
            foreach (var url in new[] { "/api/bills/1/payments", "/api/bills/1/void", "/api/bills/1/credit-note", "/api/staff/abc/password", "/api/staff", "/api/auth/password", "/api/auth/2fa/disable" })
            {
                var res = await support.PostAsync(url, Json(new { }));
                Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
                Assert.Contains("support session", (await res.Content.ReadAsStringAsync()).ToLowerInvariant());
            }
            Assert.NotEqual(HttpStatusCode.Forbidden, (await support.GetAsync("/api/auth/me")).StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, (await As(f, "admin").PostAsync("/api/bills/1/payments", Json(new { }))).StatusCode);
        }
    }
}
