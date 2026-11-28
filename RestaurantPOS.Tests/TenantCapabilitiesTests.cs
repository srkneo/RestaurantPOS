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
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // C3: capabilities per restaurant (Tenants.Capabilities, a JSON list). Null = every capability (restaurants added before C3).
    public class TenantCapabilitiesTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string PlatformEmail = "owner@pos.local";
        private const string PlatformPassword = "Platform#2026x";
        private const string OwnerPassword = "Owner#2026xy";
        private readonly WebApplicationFactory<Program> _factory;

        public TenantCapabilitiesTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private WebApplicationFactory<Program> Build()
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

        private static StringContent NewRestaurant(string name, string email) => new StringContent(JsonSerializer.Serialize(new
        {
            restaurantName = name,
            outletName = "Station Road",
            ownerName = "Ravi Patel",
            ownerEmail = email,
            ownerPassword = OwnerPassword
        }), Encoding.UTF8, "application/json");

        private static async Task<int> Create(WebApplicationFactory<Program> f, string name, string email)
        {
            var response = await As(f, "platform", "0").PostAsync("/api/platform/tenants", NewRestaurant(name, email));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("id").GetInt32();
        }

        private static async Task<Tenant> Load(WebApplicationFactory<Program> f, int id)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == id);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("null")]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("[1,2]")]
        public void Parse_NoListOrBrokenList_GivesEveryCapability(string? json)
        {
            Assert.Equal(Capabilities.Keys.ToArray(), TenantCapabilities.Parse(json).ToArray());
        }

        [Fact]
        public void Parse_StoredList_GivesRegistryOrder_AndDropsUnknownKeys()
        {
            Assert.Equal(new[] { "tables", "stock" }, TenantCapabilities.Parse("[\"stock\",\"tables\",\"night-club\"]").ToArray());
            Assert.Empty(TenantCapabilities.Parse("[]"));
        }

        [Fact]
        public void ToJson_WritesValidJson_InRegistryOrder()
        {
            var json = TenantCapabilities.ToJson(new[] { "stock", "tables" });
            Assert.Equal("[\"tables\",\"stock\"]", json);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal("[]", TenantCapabilities.ToJson(Array.Empty<string>()));
        }

        [Fact]
        public void ToJson_BadList_Throws_WithTheRegistryMessage()
        {
            Assert.Equal("Capabilities are missing.", Assert.Throws<ArgumentException>(() => TenantCapabilities.ToJson(null)).Message);
            Assert.Equal("Unknown capability: night-club", Assert.Throws<ArgumentException>(() => TenantCapabilities.ToJson(new[] { "tables", "night-club" })).Message);
            Assert.Equal("Capability tables is listed twice.", Assert.Throws<ArgumentException>(() => TenantCapabilities.ToJson(new[] { "tables", "tables" })).Message);
            Assert.Equal("Capability recipes needs stock.", Assert.Throws<ArgumentException>(() => TenantCapabilities.ToJson(new[] { "recipes" })).Message);
        }

        [Fact]
        public void EveryOutletType_RoundTrips_AndFitsTheColumn()
        {
            foreach (var t in OutletTypes.All)
            {
                Assert.Equal(TenantCapabilities.ToJson(t.Capabilities), TenantCapabilities.ForType(t.Key));
                Assert.Equal(t.Capabilities.ToArray(), TenantCapabilities.Parse(TenantCapabilities.ForType(t.Key)).ToArray());
            }
            Assert.True(TenantCapabilities.ToJson(Capabilities.Keys).Length <= 1000);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("night-club")]
        [InlineData("Restaurant")]
        public void ForType_UnknownType_Throws(string? type)
        {
            var e = Assert.Throws<ArgumentException>(() => TenantCapabilities.ForType(type));
            Assert.Equal($"Unknown outlet type: {type}", e.Message);
        }

        [Fact]
        public void Has_ReadsTheTenantsList()
        {
            var old = new Tenant { Name = "Old shop" };
            foreach (var key in Capabilities.Keys) Assert.True(TenantCapabilities.Has(old, key), key);

            var kitchen = new Tenant { Name = "Cloud kitchen", Capabilities = TenantCapabilities.ForType("cloud-kitchen") };
            Assert.False(TenantCapabilities.Has(kitchen, Capabilities.Tables));
            Assert.False(TenantCapabilities.Has(kitchen, Capabilities.Reservations));
            Assert.True(TenantCapabilities.Has(kitchen, Capabilities.KitchenScreen));

            var none = new Tenant { Name = "Nothing", Capabilities = "[]" };
            Assert.False(TenantCapabilities.Has(none, Capabilities.Tables));
            Assert.False(TenantCapabilities.Has(old, "night-club"));
            Assert.Equal("Tenant is missing.", Assert.Throws<ArgumentException>(() => TenantCapabilities.Has(null!, Capabilities.Tables)).Message);
        }

        [Fact]
        public async Task NewRestaurant_GetsTheRestaurantTemplate()
        {
            using var f = Build();
            var id = await Create(f, "Anand Thali House", "ravi@thali.local");
            var tenant = await Load(f, id);
            Assert.Equal(TenantCapabilities.ForType(OutletTypes.Restaurant), tenant.Capabilities);
            Assert.True(TenantCapabilities.Has(tenant, Capabilities.Tables));
            Assert.False(TenantCapabilities.Has(tenant, Capabilities.SellByWeight));
            Assert.False(TenantCapabilities.Has(tenant, Capabilities.SimpleCounter));
        }

        [Fact]
        public async Task ExistingRestaurant_WithoutAList_KeepsEverything()
        {
            using var f = Build();
            int id;
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = new Tenant { Name = "Before C3" };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                id = tenant.Id;
            }
            var loaded = await Load(f, id);
            Assert.Null(loaded.Capabilities);
            Assert.Equal(Capabilities.Keys.ToArray(), TenantCapabilities.Parse(loaded.Capabilities).ToArray());
        }

        [Fact]
        public async Task EachRestaurant_KeepsItsOwnList()
        {
            using var f = Build();
            var a = await Create(f, "Anand Thali House", "ravi@thali.local");
            var b = await Create(f, "Sweet Corner", "mina@sweet.local");
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = await db.Tenants.FirstAsync(t => t.Id == b);
                tenant.Capabilities = TenantCapabilities.ForType(OutletTypes.Bakery);
                await db.SaveChangesAsync();
            }
            Assert.Equal(TenantCapabilities.ForType(OutletTypes.Restaurant), (await Load(f, a)).Capabilities);
            Assert.Equal(TenantCapabilities.ForType(OutletTypes.Bakery), (await Load(f, b)).Capabilities);
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("manager")]
        [InlineData("cashier")]
        public async Task RestaurantStaff_CannotCreateARestaurant_Returns403(string role)
        {
            using var f = Build();
            var response = await As(f, role, "1").PostAsync("/api/platform/tenants", NewRestaurant("Sneaky", "sneaky@test.local"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Tenants.AnyAsync(t => t.Name == "Sneaky"));
        }
    }
}
