using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class StaffTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string GoodPassword = "Staff#2026x";
        private readonly WebApplicationFactory<Program> _factory;

        public StaffTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

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

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<string> CreateStaff(WebApplicationFactory<Program> f, string tenant, string email, string role = "cashier", bool isDevice = false)
        {
            var response = await As(f, "admin", tenant).PostAsync("/api/staff", Json(new { email, displayName = "Asha", role, password = GoodPassword, isDevice }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private static Task<HttpResponseMessage> Login(WebApplicationFactory<Program> f, string email, string password) =>
            f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false })
                .PostAsync("/api/auth/login", Json(new { email, password }));

        private static string AuthCookie(HttpResponseMessage response) =>
            response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.FirstOrDefault(v => v.StartsWith("pos.auth=", StringComparison.Ordinal)) ?? string.Empty
                : string.Empty;

        [Fact]
        public async Task Cashier_CannotListStaff_Returns403()
        {
            using var f = Build();
            var response = await As(f, "cashier", "1").GetAsync("/api/staff");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Admin_SeesOnlyOwnRestaurantStaff()
        {
            using var f = Build();
            await CreateStaff(f, "1", "cashier1@test.local");
            await CreateStaff(f, "2", "cashier2@test.local");

            var list = await (await As(f, "admin", "1").GetAsync("/api/staff")).Content.ReadAsStringAsync();

            Assert.Contains("cashier1@test.local", list);
            Assert.DoesNotContain("cashier2@test.local", list);
        }

        [Fact]
        public async Task WeakPassword_AndUnknownRole_ReturnFieldErrors()
        {
            using var f = Build();
            var weak = await As(f, "admin", "1").PostAsync("/api/staff", Json(new { email = "weak@test.local", displayName = "Weak", role = "cashier", password = "short", isDevice = false }));
            var badRole = await As(f, "admin", "1").PostAsync("/api/staff", Json(new { email = "role@test.local", displayName = "Role", role = "owner", password = GoodPassword, isDevice = false }));
            var adminDevice = await As(f, "admin", "1").PostAsync("/api/staff", Json(new { email = "dev@test.local", displayName = "Device", role = "admin", password = GoodPassword, isDevice = true }));

            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            Assert.Contains("\"password\"", await weak.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
            Assert.Contains("\"role\"", await badRole.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, adminDevice.StatusCode);
        }

        [Fact]
        public async Task DeactivatedStaff_CannotSignIn()
        {
            using var f = Build();
            var id = await CreateStaff(f, "1", "leaver@test.local");
            Assert.Equal(HttpStatusCode.OK, (await Login(f, "leaver@test.local", GoodPassword)).StatusCode);

            var update = await As(f, "admin", "1").PutAsync($"/api/staff/{id}", Json(new { displayName = "Asha", role = "cashier", isActive = false }));
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f, "leaver@test.local", GoodPassword)).StatusCode);
        }

        [Fact]
        public async Task DeviceLogin_IsPersistent_PersonLoginIsNot()
        {
            using var f = Build();
            await CreateStaff(f, "1", "kitchen-screen@test.local", role: "kitchen", isDevice: true);
            await CreateStaff(f, "1", "person@test.local");

            var device = AuthCookie(await Login(f, "kitchen-screen@test.local", GoodPassword));
            var person = AuthCookie(await Login(f, "person@test.local", GoodPassword));

            Assert.Contains("expires=", device, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(person);
            Assert.DoesNotContain("expires=", person, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ResetPassword_ReplacesOldPassword()
        {
            using var f = Build();
            var id = await CreateStaff(f, "1", "forgot@test.local");

            var reset = await As(f, "admin", "1").PostAsync($"/api/staff/{id}/password", Json(new { password = "NewPass#2026y" }));
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f, "forgot@test.local", GoodPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(f, "forgot@test.local", "NewPass#2026y")).StatusCode);
        }

        [Fact]
        public async Task OtherRestaurant_CannotEditOrResetStaff()
        {
            using var f = Build();
            var id = await CreateStaff(f, "1", "mine@test.local");

            var edit = await As(f, "admin", "2").PutAsync($"/api/staff/{id}", Json(new { displayName = "Hacked", role = "admin", isActive = true }));
            var reset = await As(f, "admin", "2").PostAsync($"/api/staff/{id}/password", Json(new { password = "NewPass#2026y" }));

            Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(f, "mine@test.local", GoodPassword)).StatusCode);
        }
    }
}
