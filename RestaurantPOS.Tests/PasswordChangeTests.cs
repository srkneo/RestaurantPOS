using System;
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
    // Change my password: the platform owner may pick a simple one (2-step sign-in protects it); everyone else keeps the strong rules.
    public class PasswordChangeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string Password = "Manager#2026";
        private readonly WebApplicationFactory<Program> _factory;

        public PasswordChangeTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string userId)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", role == "platform" ? "0" : "1");
            client.DefaultRequestHeaders.Add("X-Test-User", userId);
            return client;
        }

        private static async Task<string> MakeUser(WebApplicationFactory<Program> f, string email, bool platform)
        {
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Test",
                TenantId = platform ? 0 : 1, OutletId = platform ? 0 : 1, IsPlatformAdmin = platform,
            };
            var made = await users.CreateAsync(user, Password);
            Assert.True(made.Succeeded);
            return user.Id;
        }

        private static async Task<bool> PasswordIs(WebApplicationFactory<Program> f, string email, string password)
        {
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            return user != null && await users.CheckPasswordAsync(user, password);
        }

        private static Task<HttpResponseMessage> Change(HttpClient client, string current, string next) =>
            client.PostAsync("/api/auth/password", Json(new { currentPassword = current, newPassword = next }));

        [Fact]
        public async Task Platform_CanSetASimplePassword()
        {
            using var f = Build();
            var id = await MakeUser(f, "owner@platform.test", platform: true);
            var res = await Change(As(f, "platform", id), Password, "mahesh pos");
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            Assert.True(await PasswordIs(f, "owner@platform.test", "mahesh pos"));
            Assert.False(await PasswordIs(f, "owner@platform.test", Password));
        }

        [Fact]
        public async Task Platform_RefusesShortSameOrWrongCurrent()
        {
            using var f = Build();
            var id = await MakeUser(f, "owner2@platform.test", platform: true);
            var client = As(f, "platform", id);
            Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, Password, "short1")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, Password, Password)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, "Wrong#2026", "mahesh pos")).StatusCode);
            Assert.True(await PasswordIs(f, "owner2@platform.test", Password));
        }

        [Fact]
        public async Task Staff_StillNeedStrongPasswords()
        {
            using var f = Build();
            var id = await MakeUser(f, "meena@shop.test", platform: false);
            var client = As(f, "manager", id);
            Assert.Equal(HttpStatusCode.BadRequest, (await Change(client, Password, "simple password")).StatusCode);
            Assert.True(await PasswordIs(f, "meena@shop.test", Password));
            var ok = await Change(client, Password, "Strong#2027");
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
            Assert.True(await PasswordIs(f, "meena@shop.test", "Strong#2027"));
        }
    }
}
