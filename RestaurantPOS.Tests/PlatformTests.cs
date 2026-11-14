using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
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
    public class PlatformTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string PlatformEmail = "owner@pos.local";
        private const string PlatformPassword = "Platform#2026x";
        private const string OwnerPassword = "Owner#2026xy";
        private readonly WebApplicationFactory<Program> _factory;

        public PlatformTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        // testAuth: requests sign in by headers (X-Test-Role, X-Test-Tenant). Otherwise real cookie sign-in.
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

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static object NewRestaurant(string email, string password = OwnerPassword) => new
        {
            restaurantName = "Anand Thali House",
            outletName = "Station Road",
            ownerName = "Ravi Patel",
            ownerEmail = email,
            ownerPassword = password
        };

        // A client that keeps its sign-in cookie, signed in with the given account.
        private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
        {
            var client = f.CreateClient();
            var login = await client.PostAsync("/api/auth/login", Json(new { email, password }));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }

        private static async Task<int> CreateRestaurant(HttpClient platform, string ownerEmail)
        {
            var response = await platform.PostAsync("/api/platform/tenants", Json(NewRestaurant(ownerEmail)));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Anand Thali House", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("outletCount").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("staffCount").GetInt32());
            return doc.RootElement.GetProperty("id").GetInt32();
        }

        [Fact]
        public async Task RestaurantAdmin_CannotUsePlatform_Returns403()
        {
            using var f = Build(testAuth: true);
            var response = await As(f, "admin", "1").GetAsync("/api/platform/tenants");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task SignedOut_CannotUsePlatform_Returns401()
        {
            using var f = Build(testAuth: false);
            var response = await f.CreateClient().GetAsync("/api/platform/tenants");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Platform_ListsRestaurants()
        {
            using var f = Build(testAuth: true);
            var response = await As(f, "platform", "0").GetAsync("/api/platform/tenants");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"name\":\"My Restaurant\"", body);
            Assert.Contains("\"isActive\":true", body);
        }

        [Fact]
        public async Task PlatformOwner_IsSeededFromSettings_AndBelongsToNoRestaurant()
        {
            using var f = Build(testAuth: false);
            var client = await SignedIn(f, PlatformEmail, PlatformPassword);

            using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
            Assert.Equal("0", me.RootElement.GetProperty("tenantId").GetString());
            Assert.Contains("platform", me.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

            // Restaurant data stays out of reach: restaurant 1 has tables, the platform owner sees none.
            var tables = await client.GetAsync("/api/tables");
            if (tables.StatusCode == HttpStatusCode.OK) Assert.Equal("[]", await tables.Content.ReadAsStringAsync());
            else Assert.Equal(HttpStatusCode.Forbidden, tables.StatusCode);

            var staff = await client.GetAsync("/api/staff");
            Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        }

        [Fact]
        public async Task NewRestaurant_OwnerCanSignIn_AndHasTenTables()
        {
            using var f = Build(testAuth: false);
            var platform = await SignedIn(f, PlatformEmail, PlatformPassword);
            var id = await CreateRestaurant(platform, "ravi@thali.local");
            Assert.True(id > 1);

            var owner = await SignedIn(f, "ravi@thali.local", OwnerPassword);
            using var me = JsonDocument.Parse(await owner.GetStringAsync("/api/auth/me"));
            Assert.Equal(id.ToString(), me.RootElement.GetProperty("tenantId").GetString());
            Assert.Contains("admin", me.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

            using var tables = JsonDocument.Parse(await owner.GetStringAsync("/api/tables"));
            Assert.Equal(10, tables.RootElement.GetArrayLength());

            using var store = JsonDocument.Parse(await owner.GetStringAsync("/api/store"));
            Assert.Contains("Anand Thali House", store.RootElement.ToString());

            // The new restaurant's owner does not see restaurant 1's staff, and restaurant 1 keeps its own tables.
            var staff = await owner.GetStringAsync("/api/staff");
            Assert.Contains("ravi@thali.local", staff);
            Assert.DoesNotContain(PlatformEmail, staff);
        }

        [Fact]
        public async Task CreateRestaurant_WithUsedEmailOrWeakPassword_Returns400_AndSavesNothing()
        {
            using var f = Build(testAuth: true);
            var platform = As(f, "platform", "0");

            var used = await platform.PostAsync("/api/platform/tenants", Json(NewRestaurant(PlatformEmail)));
            Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
            Assert.Contains("owneremail", await used.Content.ReadAsStringAsync());

            var weak = await platform.PostAsync("/api/platform/tenants", Json(NewRestaurant("new@thali.local", "short")));
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            Assert.Contains("ownerpassword", await weak.Content.ReadAsStringAsync());

            var empty = await platform.PostAsync("/api/platform/tenants", Json(new { restaurantName = "", outletName = "", ownerName = "", ownerEmail = "not-an-email", ownerPassword = OwnerPassword }));
            var emptyBody = await empty.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Contains("restaurantname", emptyBody);
            Assert.Contains("outletname", emptyBody);
            Assert.Contains("ownername", emptyBody);
            Assert.Contains("owneremail", emptyBody);

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Tenants.CountAsync());
        }

        [Fact]
        public async Task DisabledRestaurant_CannotSignIn_AndOpenSessionsStop_UntilEnabled()
        {
            using var f = Build(testAuth: false);
            var platform = await SignedIn(f, PlatformEmail, PlatformPassword);
            var id = await CreateRestaurant(platform, "ravi@thali.local");
            var owner = await SignedIn(f, "ravi@thali.local", OwnerPassword);

            var disable = await platform.PostAsync($"/api/platform/tenants/{id}/disable", null);
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
            Assert.Contains("\"isActive\":false", await disable.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/tables")).StatusCode);
            var login = await f.CreateClient().PostAsync("/api/auth/login", Json(new { email = "ravi@thali.local", password = OwnerPassword }));
            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

            // The platform owner keeps working while a restaurant is disabled.
            Assert.Equal(HttpStatusCode.OK, (await platform.GetAsync("/api/platform/tenants")).StatusCode);

            var enable = await platform.PostAsync($"/api/platform/tenants/{id}/enable", null);
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/tables")).StatusCode);
            await SignedIn(f, "ravi@thali.local", OwnerPassword);
        }

        [Fact]
        public async Task DisableUnknownRestaurant_Returns404()
        {
            using var f = Build(testAuth: true);
            var response = await As(f, "platform", "0").PostAsync("/api/platform/tenants/999/disable", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task RestaurantAdmin_CannotGiveStaffThePlatformRole()
        {
            using var f = Build(testAuth: true);
            var response = await As(f, "admin", "1").PostAsync("/api/staff",
                Json(new { email = "sneaky@test.local", displayName = "Sneaky", role = "platform", password = OwnerPassword, isDevice = false }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task DisablingOneRestaurant_DoesNotAffectAnother()
        {
            using var f = Build(testAuth: true);
            var platform = As(f, "platform", "0");
            var create = await platform.PostAsync("/api/platform/tenants", Json(NewRestaurant("ravi@thali.local")));
            using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var id = doc.RootElement.GetProperty("id").GetInt32();

            await platform.PostAsync($"/api/platform/tenants/{id}/disable", null);

            Assert.Equal(HttpStatusCode.Unauthorized, (await As(f, "admin", id.ToString()).GetAsync("/api/tables")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin", "1").GetAsync("/api/tables")).StatusCode);
        }
    }
}
