using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // C5: renewal signs the linked cloud restaurant's capabilities (its own list AND its plan) into the shop PC lease.
    public class LicenceRenewCapabilitiesTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public LicenceRenewCapabilitiesTests(WebApplicationFactory<Program> factory) => _factory = factory;

        public void Dispose() => _key.Dispose();

        private WebApplicationFactory<Program> Build(string? monthly = null)
        {
            var dbName = Guid.NewGuid().ToString();
            var settings = new Dictionary<string, string?> { ["Licence:PrivateKey"] = Convert.ToBase64String(_key.ExportPkcs8PrivateKey()) };
            if (monthly != null) settings["Plans:monthly:Capabilities"] = monthly;
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
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

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage res) => JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        private static async Task<(int Id, string Key)> CreateLicence(WebApplicationFactory<Program> f, string customer = "Anand Restaurant")
        {
            var res = await As(f, "platform").PostAsync("/api/platform/licences", Json(new { customer, plan = "yearly" }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            return (body.GetProperty("id").GetInt32(), body.GetProperty("key").GetString()!);
        }

        private static async Task<int> AddTenant(WebApplicationFactory<Program> f, string name, string? plan, string? capabilities)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = name, Plan = plan, Capabilities = capabilities };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return tenant.Id;
        }

        private static async Task SetCapabilities(WebApplicationFactory<Program> f, int tenantId, string? capabilities)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
            tenant.Capabilities = capabilities;
            await db.SaveChangesAsync();
        }

        private static async Task Link(WebApplicationFactory<Program> f, int licenceId, int? tenantId)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var licence = await db.ShopLicences.FirstAsync(l => l.Id == licenceId);
            licence.TenantId = tenantId;
            await db.SaveChangesAsync();
        }

        private static Task<HttpResponseMessage> PostRenew(WebApplicationFactory<Program> f, string key, string device) =>
            As(f, "cashier").PostAsync("/api/licence/renew", Json(new { key, deviceId = device, version = "2.5.0" }));

        // Renews, checks the lease is signed and accepted by the shop PC, and gives its payload.
        private async Task<LicencePayload> Renew(WebApplicationFactory<Program> f, string key, string device = "device-1")
        {
            var res = await PostRenew(f, key, device);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            var file = new LicenceFile(body.GetProperty("licence").GetProperty("payload").GetString()!, body.GetProperty("licence").GetProperty("signature").GetString()!);
            Assert.False(Licence.Evaluate(file, _key, DateTime.UtcNow, null).Locked);
            var payload = Licence.Verify(file, _key);
            Assert.NotNull(payload);
            return payload!;
        }

        [Fact]
        public async Task NotLinked_LeaseHasNoList_EverythingStays()
        {
            using var f = Build("tables");
            var (_, key) = await CreateLicence(f);
            var payload = await Renew(f, key);
            Assert.Null(payload.Capabilities);
            Assert.Equal(Capabilities.Keys.ToArray(), Licence.CapabilitiesOf(payload).ToArray());
        }

        [Fact]
        public async Task Linked_LeaseCarriesTheRestaurantsList()
        {
            using var f = Build();
            var (id, key) = await CreateLicence(f);
            var tenant = await AddTenant(f, "Anand Thali House", null, TenantCapabilities.ToJson(new[] { "stock", "tables", "kitchen-screen" }));
            await Link(f, id, tenant);
            var payload = await Renew(f, key);
            Assert.Equal(new[] { "tables", "kitchen-screen", "stock" }, payload.Capabilities);
        }

        [Fact]
        public async Task Linked_ThePlanLimitsTheList()
        {
            using var f = Build("tables,stock");
            var (id, key) = await CreateLicence(f);
            var tenant = await AddTenant(f, "Anand Thali House", "monthly", TenantCapabilities.ToJson(new[] { "tables", "kitchen-screen", "stock", "recipes" }));
            await Link(f, id, tenant);
            var payload = await Renew(f, key);
            Assert.Equal(new[] { "tables", "stock" }, payload.Capabilities);
        }

        [Fact]
        public async Task Linked_PlanWithoutTheDependency_DropsTheDependentCapability()
        {
            using var f = Build("tables,recipes");
            var (id, key) = await CreateLicence(f);
            var tenant = await AddTenant(f, "Anand Thali House", "monthly", TenantCapabilities.ToJson(new[] { "tables", "stock", "recipes" }));
            await Link(f, id, tenant);
            Assert.Equal(new[] { "tables" }, (await Renew(f, key)).Capabilities);
        }

        [Fact]
        public async Task Linked_RestaurantFromBeforeC3_GetsEveryCapability()
        {
            using var f = Build();
            var (id, key) = await CreateLicence(f);
            var tenant = await AddTenant(f, "Old Restaurant", null, null);
            await Link(f, id, tenant);
            Assert.Equal(Capabilities.Keys.ToArray(), (await Renew(f, key)).Capabilities);
        }

        [Fact]
        public async Task ListChange_ReachesThePc_WithTheNextRenewal()
        {
            using var f = Build();
            var (id, key) = await CreateLicence(f);
            var tenant = await AddTenant(f, "Anand Thali House", null, TenantCapabilities.ToJson(new[] { "tables", "stock" }));
            await Link(f, id, tenant);
            Assert.Equal(new[] { "tables", "stock" }, (await Renew(f, key)).Capabilities);

            await SetCapabilities(f, tenant, TenantCapabilities.ToJson(new[] { "tables" }));
            Assert.Equal(new[] { "tables" }, (await Renew(f, key)).Capabilities);
        }

        [Fact]
        public async Task TwoRestaurants_EachPcGetsOnlyItsOwnList()
        {
            using var f = Build();
            var (idA, keyA) = await CreateLicence(f, "Anand Thali House");
            var (idB, keyB) = await CreateLicence(f, "Sweet Bakery");
            await Link(f, idA, await AddTenant(f, "Anand Thali House", null, TenantCapabilities.ToJson(new[] { "tables" })));
            await Link(f, idB, await AddTenant(f, "Sweet Bakery", null, TenantCapabilities.ToJson(new[] { "simple-counter" })));

            Assert.Equal(new[] { "tables" }, (await Renew(f, keyA, "pc-a")).Capabilities);
            Assert.Equal(new[] { "simple-counter" }, (await Renew(f, keyB, "pc-b")).Capabilities);
        }

        [Fact]
        public async Task LinkedToAMissingRestaurant_LeaseHasNoList()
        {
            using var f = Build();
            var (id, key) = await CreateLicence(f);
            await Link(f, id, 99999);
            Assert.Null((await Renew(f, key)).Capabilities);
        }

        [Fact]
        public async Task StoppedLicence_GetsNoLease_Returns403()
        {
            using var f = Build();
            var (id, key) = await CreateLicence(f);
            await Link(f, id, await AddTenant(f, "Anand Thali House", null, TenantCapabilities.ToJson(new[] { "tables" })));
            var revoked = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/revoke", null);
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

            var refused = await PostRenew(f, key, "device-1");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("stopped", (await Read(refused)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task KeyUsedOnAnotherPc_GetsNoLease_Returns403()
        {
            using var f = Build();
            var (id, key) = await CreateLicence(f);
            await Link(f, id, await AddTenant(f, "Anand Thali House", null, TenantCapabilities.ToJson(new[] { "tables" })));
            await Renew(f, key, "pc-1");

            var other = await PostRenew(f, key, "pc-2");
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        }
    }
}
