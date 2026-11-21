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
using POSSystem.Menu;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // v1.7 cloud-to-shop menu sync, cloud side: a linked shop gets exactly its restaurant's menu, nothing else.
    public class MenuSyncServerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public MenuSyncServerTests(WebApplicationFactory<Program> factory) => _factory = factory;

        public void Dispose() => _key.Dispose();

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            var privateKey = Convert.ToBase64String(_key.ExportPkcs8PrivateKey());
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Licence:PrivateKey"] = privateKey
                }));
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

        // A licence that has been activated on "device-1", so it is bound to that PC.
        private static async Task<(int Id, string Key)> ActiveLicence(WebApplicationFactory<Program> f)
        {
            var created = await As(f, "platform").PostAsync("/api/platform/licences", Json(new { customer = "Anand Restaurant", plan = "trial" }));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var body = await Read(created);
            var key = body.GetProperty("key").GetString()!;
            var renewed = await As(f, "cashier").PostAsync("/api/licence/renew", Json(new { key, deviceId = "device-1", version = "2.3.1" }));
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            return (body.GetProperty("id").GetInt32(), key);
        }

        // Restaurant 1 gets a category, two items (one with a size and an extra) and a deleted item; restaurant 2 gets one item.
        private static void SeedMenus(WebApplicationFactory<Program> f)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.Tenants.Any(t => t.Id == 1)) db.Tenants.Add(new Tenant { Id = 1, Name = "My Restaurant" });
            db.MenuCategories.Add(new MenuCategory { Id = 9001, TenantId = 1, Name = "Sync Starters", SortOrder = 1, AvailableFrom = new TimeOnly(11, 0) });
            db.MenuItems.Add(new MenuItem { Id = 9001, TenantId = 1, Name = "Sync Paneer Tikka", Price = 220.50m, CategoryId = 9001, Category = "Sync Starters", TaxRate = 5 });
            db.MenuItems.Add(new MenuItem { Id = 9002, TenantId = 1, Name = "Sync Thali", Price = 180m, TaxRate = 5 });
            db.MenuItems.Add(new MenuItem { Id = 9003, TenantId = 1, Name = "Sync Old Item", Price = 50m, IsDeleted = true });
            db.ItemVariants.Add(new ItemVariant { Id = 9001, TenantId = 1, MenuItemId = 9001, Name = "Full", Price = 300m });
            db.ItemAddOns.Add(new ItemAddOn { Id = 9001, TenantId = 1, MenuItemId = 9001, Name = "Extra cheese", Price = 30m });
            db.ComboParts.Add(new ComboPart { Id = 9001, TenantId = 1, ComboItemId = 9002, PartItemId = 9001, Quantity = 1 });
            db.SaveChanges();

            // New rows get the context's current restaurant on save, so restaurant 2's rows are saved as restaurant 2.
            db.CurrentTenantId = 2;
            db.MenuItems.Add(new MenuItem { Id = 9004, TenantId = 2, Name = "Other Shop Dosa", Price = 90m });
            db.ItemAddOns.Add(new ItemAddOn { Id = 9002, TenantId = 2, MenuItemId = 9004, Name = "Other shop chutney", Price = 10m });
            db.SaveChanges();
        }

        private static Task<HttpResponseMessage> Menu(WebApplicationFactory<Program> f, string key, string device = "device-1") =>
            f.CreateClient().PostAsync("/api/licence/menu", Json(new { key, deviceId = device }));

        [Fact]
        public async Task A_shop_that_is_not_linked_gets_no_menu()
        {
            using var f = Build();
            var (_, key) = await ActiveLicence(f);

            var res = await Menu(f, key);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            Assert.False(body.GetProperty("linked").GetBoolean());
            Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task A_linked_shop_gets_only_its_own_restaurant_menu()
        {
            using var f = Build();
            SeedMenus(f);
            var (id, key) = await ActiveLicence(f);
            var link = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/restaurant", Json(new { tenantId = 1 }));
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
            Assert.Equal(1, (await Read(link)).GetProperty("tenantId").GetInt32());

            var body = await Read(await Menu(f, key));
            Assert.True(body.GetProperty("linked").GetBoolean());
            var names = body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
            Assert.Contains("Sync Paneer Tikka", names);
            Assert.Contains("Sync Thali", names);
            Assert.DoesNotContain("Sync Old Item", names);
            Assert.DoesNotContain("Other Shop Dosa", names);
            var addOns = body.GetProperty("addOns").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
            Assert.Contains("Extra cheese", addOns);
            Assert.DoesNotContain("Other shop chutney", addOns);
            Assert.Contains(body.GetProperty("variants").EnumerateArray(), x => x.GetProperty("name").GetString() == "Full");
            Assert.Contains(body.GetProperty("comboParts").EnumerateArray(), x => x.GetProperty("comboItemId").GetInt32() == 9002);
            var starters = body.GetProperty("categories").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == 9001);
            Assert.Equal("11:00", starters.GetProperty("availableFrom").GetString());
        }

        [Fact]
        public async Task The_version_changes_only_when_the_menu_changes()
        {
            using var f = Build();
            SeedMenus(f);
            var (id, key) = await ActiveLicence(f);
            await As(f, "platform").PostAsync($"/api/platform/licences/{id}/restaurant", Json(new { tenantId = 1 }));

            var first = (await Read(await Menu(f, key))).GetProperty("version").GetString();
            var again = (await Read(await Menu(f, key))).GetProperty("version").GetString();
            Assert.False(string.IsNullOrEmpty(first));
            Assert.Equal(first, again);

            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var item = db.MenuItems.IgnoreQueryFilters().Single(x => x.Id == 9002);
                item.Price = 199m;
                db.SaveChanges();
            }
            var changed = (await Read(await Menu(f, key))).GetProperty("version").GetString();
            Assert.NotEqual(first, changed);
        }

        [Fact]
        public async Task Another_pc_or_an_unknown_key_gets_no_menu()
        {
            using var f = Build();
            SeedMenus(f);
            var (id, key) = await ActiveLicence(f);
            await As(f, "platform").PostAsync($"/api/platform/licences/{id}/restaurant", Json(new { tenantId = 1 }));

            Assert.Equal(HttpStatusCode.Forbidden, (await Menu(f, key, "device-2")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Menu(f, "POS-AAAAA-BBBBB-CCCCC-DDDDD")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Menu(f, key, "")).StatusCode);
        }

        [Fact]
        public async Task Only_the_platform_owner_links_and_only_to_a_real_restaurant()
        {
            using var f = Build();
            SeedMenus(f);
            var (id, _) = await ActiveLicence(f);

            var owner = await As(f, "admin").PostAsync($"/api/platform/licences/{id}/restaurant", Json(new { tenantId = 1 }));
            Assert.Equal(HttpStatusCode.Forbidden, owner.StatusCode);
            var missing = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/restaurant", Json(new { tenantId = 98765 }));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            var unlink = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/restaurant", Json(new { tenantId = (int?)null }));
            Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        }
    }
}
