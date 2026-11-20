using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class SecurityEventTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string PlatformEmail = "security-owner@pos.local";
        private const string PlatformPassword = "Platform#2026x";
        private readonly WebApplicationFactory<Program> _factory;

        public SecurityEventTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private WebApplicationFactory<Program> Build(bool testAuth)
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Platform:Email", PlatformEmail);
                builder.UseSetting("Platform:Password", PlatformPassword);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    if (testAuth)
                    {
                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                        services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                    }
                });
            });
        }

        private static StringContent Json(object body) =>
            new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        // The event is saved right after the answer; wait briefly for it.
        private static async Task<List<SecurityEvent>> Events(WebApplicationFactory<Program> f, int atLeast)
        {
            for (var i = 0; i < 40; i++)
            {
                using var scope = f.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var list = await db.SecurityEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
                if (list.Count >= atLeast) return list;
                await Task.Delay(50);
            }
            using var last = f.Services.CreateScope();
            return await last.ServiceProvider.GetRequiredService<AppDbContext>().SecurityEvents.AsNoTracking().ToListAsync();
        }

        [Theory]
        [InlineData("POST", "/api/auth/login", "sign-in")]
        [InlineData("POST", "/api/auth/login/code", "sign-in-code")]
        [InlineData("POST", "/api/auth/2fa/disable", "2fa-disable")]
        [InlineData("POST", "/api/staff/", "staff-create")]
        [InlineData("PUT", "/api/staff/{id}", "staff-update")]
        [InlineData("POST", "/api/staff/{id}/password", "staff-password-reset")]
        [InlineData("PUT", "/api/staff/{id}/pin", "staff-pin")]
        [InlineData("POST", "/api/platform/tenants/{id:int}/payments", "platform-change")]
        [InlineData("GET", "/api/auth/login", null)]
        [InlineData("GET", "/api/platform/security-events", null)]
        [InlineData("POST", "/api/orders", null)]
        [InlineData("POST", null, null)]
        public void Classify_PicksSecurityRequests(string method, string? route, string? expected)
        {
            Assert.Equal(expected, SecurityEvents.Classify(method, route));
        }

        [Fact]
        public async Task FailedSignIn_IsRecorded_WithTheAccount()
        {
            var f = Build(testAuth: false);
            var res = await f.CreateClient().PostAsync("/api/auth/login", Json(new { email = PlatformEmail, password = "wrong-password" }));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

            var e = Assert.Single(await Events(f, 1));
            Assert.Equal("sign-in", e.Type);
            Assert.False(e.Success);
            Assert.Equal(401, e.Status);
            Assert.False(string.IsNullOrEmpty(e.UserId));
            Assert.False(string.IsNullOrEmpty(e.RequestId));
            Assert.True(e.AtUtc > DateTime.UtcNow.AddMinutes(-5));
        }

        [Fact]
        public async Task UnknownAccount_IsRecorded_WithoutUser()
        {
            var f = Build(testAuth: false);
            await f.CreateClient().PostAsync("/api/auth/login", Json(new { email = "nobody@pos.local", password = "x" }));
            var e = Assert.Single(await Events(f, 1));
            Assert.Equal("sign-in", e.Type);
            Assert.False(e.Success);
            Assert.Null(e.UserId);
        }

        [Fact]
        public async Task Reads_AreNotRecorded()
        {
            var f = Build(testAuth: true);
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            await client.GetAsync("/health");
            await client.GetAsync("/api/auth/me");
            await Task.Delay(300);
            Assert.Empty(await Events(f, 0));
        }

        [Fact]
        public async Task OwnerCannotReadTheLog_PlatformCan()
        {
            var f = Build(testAuth: true);
            var owner = f.CreateClient();
            owner.DefaultRequestHeaders.Add("X-Test-Role", "admin");
            owner.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/platform/security-events")).StatusCode);

            var platform = f.CreateClient();
            platform.DefaultRequestHeaders.Add("X-Test-Role", AuthSetup.PlatformRole);
            platform.DefaultRequestHeaders.Add("X-Test-Tenant", "0");
            Assert.Equal(HttpStatusCode.OK, (await platform.GetAsync("/api/platform/security-events")).StatusCode);
        }

        [Fact]
        public async Task OldEvents_AreRemoved_AfterAYear()
        {
            var f = Build(testAuth: false);
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = new DateTime(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc);
            db.SecurityEvents.Add(new SecurityEvent { AtUtc = now.AddDays(-400), Type = "sign-in", Method = "POST", Path = "/api/auth/login" });
            db.SecurityEvents.Add(new SecurityEvent { AtUtc = now.AddDays(-366), Type = "sign-in", Method = "POST", Path = "/api/auth/login" });
            db.SecurityEvents.Add(new SecurityEvent { AtUtc = now.AddDays(-300), Type = "sign-out", Method = "POST", Path = "/api/auth/logout" });
            await db.SaveChangesAsync();

            var removed = await SecurityEventCleanup.RemoveOldAsync(db, now);

            Assert.Equal(2, removed);
            var left = Assert.Single(await db.SecurityEvents.AsNoTracking().ToListAsync());
            Assert.Equal("sign-out", left.Type);
        }
    }
}
