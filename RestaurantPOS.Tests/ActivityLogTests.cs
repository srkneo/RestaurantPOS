using System;
using System.Collections.Generic;
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
    // UI1: the Activity log screen reads GET /api/admin/audit (owner, also in a support session)
    // and GET /api/platform/tenants/{id}/audit (platform owner, one restaurant).
    public class ActivityLogTests : IClassFixture<WebApplicationFactory<Program>>
    {
        public ActivityLogTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private readonly WebApplicationFactory<Program> _factory;

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
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

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1", string? user = null)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            if (user != null) client.DefaultRequestHeaders.Add("X-Test-User", user);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task<List<JsonElement>> Log(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Read(response)).EnumerateArray().ToList();
        }

        private static async Task<int> AddMenuItem(HttpClient admin, string name)
        {
            var category = await Read(await admin.PostAsync("/api/admin/menu/categories", Json(new { name = "Snacks " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1 })));
            var created = await admin.PostAsync("/api/admin/menu/items", Json(new
            {
                name, price = 15m, categoryId = category.GetProperty("id").GetInt32(), description = (string?)null,
                isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1
            }));
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            return (await Read(created)).GetProperty("id").GetInt32();
        }

        // Writes audit rows straight into the database for one restaurant, the way SupportAccess does.
        private static async Task AddRows(WebApplicationFactory<Program> f, int tenantId, params (string Action, string Type, object Details)[] rows)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CurrentTenantId = tenantId;
            foreach (var r in rows)
                db.AuditLogs.Add(new AuditLog { TenantId = tenantId, Action = r.Action, EntityType = r.Type, Details = JsonSerializer.Serialize(r.Details) });
            await db.SaveChangesAsync();
        }

        private static async Task<int> AddTenant(WebApplicationFactory<Program> f, string name)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = name };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return tenant.Id;
        }

        [Fact]
        public async Task Owner_SeesNewestFirst_WithWhoWhatAndJsonDetails()
        {
            using var f = Build();
            var admin = As(f, "admin");
            await AddMenuItem(admin, "First Dosa");
            await AddMenuItem(admin, "Second Dosa");

            var log = await Log(admin, "/api/admin/audit");
            Assert.NotEmpty(log);
            var ids = log.Select(e => e.GetProperty("id").GetInt64()).ToList();
            Assert.Equal(ids.OrderByDescending(i => i).ToList(), ids);
            foreach (var e in log)
            {
                Assert.False(string.IsNullOrEmpty(e.GetProperty("by").GetString()));
                Assert.False(string.IsNullOrEmpty(e.GetProperty("action").GetString()));
                Assert.False(string.IsNullOrEmpty(e.GetProperty("entityType").GetString()));
                Assert.True(e.TryGetProperty("createdAt", out _));
                Assert.False(e.GetProperty("support").GetBoolean());
                var details = e.GetProperty("details").GetString();
                if (details != null) JsonDocument.Parse(details).Dispose();
            }
            var items = log.Where(e => e.GetProperty("entityType").GetString() == "MenuItem").ToList();
            Assert.Equal(2, items.Count);
            Assert.Contains("Second Dosa", items[0].GetProperty("details").GetString());
            Assert.Contains("First Dosa", items[1].GetProperty("details").GetString());
            Assert.All(items, e => Assert.Equal("Created", e.GetProperty("action").GetString()));
        }

        [Fact]
        public async Task TypeFilter_ReturnsOnlyThatType_AndAnUnknownTypeReturnsNothing()
        {
            using var f = Build();
            var admin = As(f, "admin");
            await AddMenuItem(admin, "Filter Vada");

            var items = await Log(admin, "/api/admin/audit?type=MenuItem");
            Assert.NotEmpty(items);
            Assert.All(items, e => Assert.Equal("MenuItem", e.GetProperty("entityType").GetString()));
            Assert.Empty(await Log(admin, "/api/admin/audit?type=NoSuchType"));
            Assert.NotEmpty(await Log(admin, "/api/admin/audit?type="));
        }

        [Fact]
        public async Task Take_IsKeptBetweenOneAndFiveHundred()
        {
            using var f = Build();
            var admin = As(f, "admin");
            await AddMenuItem(admin, "Take Idli");
            await AddMenuItem(admin, "Take Upma");

            Assert.Single(await Log(admin, "/api/admin/audit?take=1"));
            Assert.Single(await Log(admin, "/api/admin/audit?take=0"));
            Assert.Single(await Log(admin, "/api/admin/audit?take=-5"));
            Assert.True((await Log(admin, "/api/admin/audit?take=100000")).Count <= 500);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/audit?take=abc")).StatusCode);
        }

        [Theory]
        [InlineData("manager")]
        [InlineData("cashier")]
        [InlineData("waiter")]
        [InlineData("kitchen")]
        [InlineData("platform")]
        public async Task OnlyTheOwnerReadsTheRestaurantLog(string role)
        {
            using var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).GetAsync("/api/admin/audit")).StatusCode);
        }

        [Fact]
        public async Task AnotherRestaurantsLog_IsNeverShown()
        {
            using var f = Build();
            await AddMenuItem(As(f, "admin"), "Secret Samosa");
            var other = As(f, "admin", "2");
            Assert.DoesNotContain("Secret Samosa", await (await other.GetAsync("/api/admin/audit")).Content.ReadAsStringAsync());
            Assert.DoesNotContain("Secret Samosa", await (await other.GetAsync("/api/admin/audit?type=MenuItem")).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task SupportSignIn_IsMarkedAsSupport()
        {
            using var f = Build();
            var admin = As(f, "admin");
            await AddMenuItem(admin, "Normal Poha");
            await AddRows(f, 1, ("SupportLogin", "Support", new { support = "Support session started by mahesh@vyom.in" }));

            var log = await Log(admin, "/api/admin/audit");
            var newest = log[0];
            Assert.Equal("SupportLogin", newest.GetProperty("action").GetString());
            Assert.True(newest.GetProperty("support").GetBoolean());
            using var details = JsonDocument.Parse(newest.GetProperty("details").GetString()!);
            Assert.Equal("Support session started by mahesh@vyom.in", details.RootElement.GetProperty("support").GetString());
            Assert.All(log.Skip(1), e => Assert.False(e.GetProperty("support").GetBoolean()));
        }

        [Fact]
        public async Task ChangesMadeByASupportUser_AreMarkedAsSupport()
        {
            using var f = Build();
            string supportId;
            using (var scope = f.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var support = new AppUser { UserName = "support-1@pos.local", Email = "support-1@pos.local", TenantId = 1, IsSupport = true };
                Assert.True((await users.CreateAsync(support, "Support#12345")).Succeeded);
                supportId = support.Id;
            }
            await AddMenuItem(As(f, "admin", "1", supportId), "Support Pakoda");

            var log = await Log(As(f, "admin"), "/api/admin/audit?type=MenuItem");
            var row = Assert.Single(log);
            Assert.True(row.GetProperty("support").GetBoolean());
            Assert.Equal("support-1@pos.local", row.GetProperty("by").GetString());
        }

        [Fact]
        public async Task Platform_ReadsOneRestaurantsLog_NewestFirst_WithTypeFilter()
        {
            using var f = Build();
            await AddMenuItem(As(f, "admin"), "Tenant One Thali");
            var cafe = await AddTenant(f, "Activity Cafe");
            await AddRows(f, cafe,
                ("Changed", "MenuItem", new { changes = new { Price = new object[] { 10, 12 } } }),
                ("SupportLogin", "Support", new { support = "Support session started by mahesh@vyom.in" }));

            var platform = As(f, "platform", "0");
            var log = await Log(platform, $"/api/platform/tenants/{cafe}/audit");
            Assert.Equal("SupportLogin", log[0].GetProperty("action").GetString());
            Assert.True(log[0].GetProperty("support").GetBoolean());
            Assert.Contains(log, e => e.GetProperty("action").GetString() == "Changed" && e.GetProperty("details").GetString()!.Contains("Price"));
            Assert.DoesNotContain("Tenant One Thali", await (await platform.GetAsync($"/api/platform/tenants/{cafe}/audit")).Content.ReadAsStringAsync());

            var support = await Log(platform, $"/api/platform/tenants/{cafe}/audit?type=Support");
            Assert.Single(support);
            Assert.Single(await Log(platform, $"/api/platform/tenants/{cafe}/audit?take=1"));
        }

        [Fact]
        public async Task Platform_UnknownRestaurant_Is404()
        {
            using var f = Build();
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "platform", "0").GetAsync("/api/platform/tenants/99999/audit")).StatusCode);
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("manager")]
        [InlineData("cashier")]
        public async Task OnlyThePlatformReadsAnotherRestaurantsLog(string role)
        {
            using var f = Build();
            var cafe = await AddTenant(f, "Closed Cafe");
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).GetAsync($"/api/platform/tenants/{cafe}/audit")).StatusCode);
        }
    }
}
