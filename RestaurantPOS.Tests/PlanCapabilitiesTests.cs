using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // C4: plans carry allowed capabilities (Plans:<plan>:Capabilities). Not set = every capability.
    public class PlanCapabilitiesTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string PlatformEmail = "owner@pos.local";
        private const string PlatformPassword = "Platform#2026x";
        private const string OwnerPassword = "Owner#2026xy";
        private readonly WebApplicationFactory<Program> _factory;

        public PlanCapabilitiesTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

        private WebApplicationFactory<Program> Build(string? monthly)
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Platform:Email", PlatformEmail);
                builder.UseSetting("Platform:Password", PlatformPassword);
                if (monthly != null) builder.UseSetting("Plans:monthly:Capabilities", monthly);
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

        private static async Task<int> Create(WebApplicationFactory<Program> f, string name, string email)
        {
            var response = await As(f, "platform", "0").PostAsync("/api/platform/tenants", Json(new
            {
                restaurantName = name, outletName = "Station Road", ownerName = "Ravi Patel", ownerEmail = email, ownerPassword = OwnerPassword
            }));
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
        [InlineData("monthly")]
        [InlineData("yearly")]
        [InlineData("trial")]
        public void NoSetting_OrNoPlan_AllowsEverything(string? plan)
        {
            Assert.Equal(Capabilities.Keys.ToArray(), PlanCapabilities.Allowed(Config(), plan).ToArray());
            Assert.Equal(Capabilities.Keys.ToArray(), PlanCapabilities.Allowed(Config(("Plans:monthly:Capabilities", " ")), plan).ToArray());
        }

        [Fact]
        public void Setting_GivesRegistryOrder_TrimsSpaces_DropsUnknownKeys_IgnoresCase()
        {
            var config = Config(("Plans:monthly:Capabilities", " stock , tables,night-club,,"));
            Assert.Equal(new[] { "tables", "stock" }, PlanCapabilities.Allowed(config, "monthly").ToArray());
            Assert.Equal(new[] { "tables", "stock" }, PlanCapabilities.Allowed(config, " Monthly ").ToArray());
            Assert.Equal(Capabilities.Keys.ToArray(), PlanCapabilities.Allowed(config, "yearly").ToArray());
        }

        [Fact]
        public void MissingInput_Throws()
        {
            Assert.Equal("Configuration is missing.", Assert.Throws<ArgumentException>(() => PlanCapabilities.Allowed(null!, "monthly")).Message);
            Assert.Equal("Tenant is missing.", Assert.Throws<ArgumentException>(() => PlanCapabilities.Effective(null!, Config())).Message);
        }

        [Fact]
        public void Effective_IsTheRestaurantsList_AndThePlan()
        {
            var config = Config(("Plans:monthly:Capabilities", "tables,kitchen-screen,stock,loyalty"));
            var tenant = new Tenant { Name = "Anand", Plan = "monthly", Capabilities = TenantCapabilities.ToJson(new[] { "tables", "stock", "tokens" }) };
            Assert.Equal(new[] { "tables", "stock" }, PlanCapabilities.Effective(tenant, config).ToArray());
            Assert.True(PlanCapabilities.Has(tenant, config, Capabilities.Stock));
            Assert.False(PlanCapabilities.Has(tenant, config, Capabilities.Tokens));
            Assert.False(PlanCapabilities.Has(tenant, config, Capabilities.Loyalty));
            Assert.False(PlanCapabilities.Has(tenant, config, "night-club"));
        }

        [Fact]
        public void Effective_OldRestaurant_WithoutAList_GetsThePlan()
        {
            var config = Config(("Plans:trial:Capabilities", "tables,tokens"));
            var tenant = new Tenant { Name = "Before C3", Plan = "trial" };
            Assert.Equal(new[] { "tables", "tokens" }, PlanCapabilities.Effective(tenant, config).ToArray());
            var free = new Tenant { Name = "Free use", Plan = null };
            Assert.Equal(Capabilities.Keys.ToArray(), PlanCapabilities.Effective(free, config).ToArray());
        }

        [Fact]
        public void Effective_DropsACapability_WhoseDependencyIsNotLeft()
        {
            var config = Config(("Plans:monthly:Capabilities", "recipes,reservations,qr-ordering,loyalty"));
            var tenant = new Tenant { Name = "Anand", Plan = "monthly" };
            Assert.Equal(new[] { "loyalty" }, PlanCapabilities.Effective(tenant, config).ToArray());
        }

        [Fact]
        public void Check_RefusesACapabilityOutsideThePlan()
        {
            var config = Config(("Plans:monthly:Capabilities", "tables,stock"));
            Assert.Null(PlanCapabilities.Check(config, "monthly", new[] { "tables", "stock" }));
            Assert.Null(PlanCapabilities.Check(config, "monthly", Array.Empty<string>()));
            Assert.Equal("Capability loyalty is not in the monthly plan.", PlanCapabilities.Check(config, "Monthly", new[] { "tables", "loyalty" }));
            Assert.Equal("Capabilities are missing.", PlanCapabilities.Check(config, "monthly", null));
            Assert.Null(PlanCapabilities.Check(config, "yearly", Capabilities.Keys));
            Assert.Null(PlanCapabilities.Check(config, null, Capabilities.Keys));
        }

        [Fact]
        public async Task PaidRestaurant_GetsTheIntersection_OtherRestaurantKeepsItsOwn()
        {
            using var f = Build("tables,kitchen-screen,tokens");
            var a = await Create(f, "Anand Thali House", "ravi@thali.local");
            var b = await Create(f, "Sweet Corner", "mina@sweet.local");
            var pay = await As(f, "platform", "0").PostAsync($"/api/platform/tenants/{a}/payments", Json(new { plan = "monthly", amount = 1000 }));
            Assert.Equal(HttpStatusCode.OK, pay.StatusCode);

            var config = f.Services.GetRequiredService<IConfiguration>();
            Assert.Equal(new[] { "tables", "kitchen-screen", "tokens" }, PlanCapabilities.Effective(await Load(f, a), config).ToArray());
            var other = await Load(f, b);
            Assert.Equal(TenantCapabilities.Parse(other.Capabilities).ToArray(), PlanCapabilities.Effective(other, config).ToArray());
        }

        [Fact]
        public async Task WithoutTheSetting_NothingChanges()
        {
            using var f = Build(null);
            var a = await Create(f, "Anand Thali House", "ravi@thali.local");
            var pay = await As(f, "platform", "0").PostAsync($"/api/platform/tenants/{a}/payments", Json(new { plan = "monthly", amount = 1000 }));
            Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
            var tenant = await Load(f, a);
            Assert.Equal(TenantCapabilities.Parse(tenant.Capabilities).ToArray(), PlanCapabilities.Effective(tenant, f.Services.GetRequiredService<IConfiguration>()).ToArray());
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("manager")]
        [InlineData("cashier")]
        public async Task RestaurantStaff_CannotChangeThePlan_Returns403(string role)
        {
            using var f = Build("tables");
            var a = await Create(f, "Anand Thali House", "ravi@thali.local");
            var response = await As(f, role, a.ToString()).PostAsync($"/api/platform/tenants/{a}/payments", Json(new { plan = "yearly", amount = 0 }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.NotEqual("yearly", (await Load(f, a)).Plan);
        }
    }
}
