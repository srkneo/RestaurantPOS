using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
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
    // v1.7 cloud-to-shop sync of the store profile and outlet settings (GSTIN, bill text, tax mode and the rest).
    public class MenuSyncStoreTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public MenuSyncStoreTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        // Restaurant 1 has one outlet with a GSTIN; it is created here if the test database has none.
        private static void SeedStore(WebApplicationFactory<Program> f)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.Tenants.Any(t => t.Id == 1)) db.Tenants.Add(new Tenant { Id = 1, Name = "My Restaurant" });
            var outlet = db.Outlets.IgnoreQueryFilters().Where(o => o.TenantId == 1).OrderBy(o => o.Id).FirstOrDefault();
            if (outlet is null) { outlet = new Outlet { TenantId = 1, Name = "Main" }; db.Outlets.Add(outlet); }
            outlet.Gstin = "24ABCDE1234F1Z5";
            outlet.ReceiptFooter = "Thank you, visit again";
            db.SaveChanges();
        }

        [Fact]
        public async Task The_cloud_sends_the_store_profile_with_the_menu()
        {
            using var f = Build();
            SeedStore(f);
            var created = await Read(await As(f, "platform").PostAsync("/api/platform/licences", Json(new { customer = "Anand Restaurant", plan = "trial" })));
            var key = created.GetProperty("key").GetString()!;
            await As(f, "cashier").PostAsync("/api/licence/renew", Json(new { key, deviceId = "device-1", version = "2.3.1" }));
            await As(f, "platform").PostAsync($"/api/platform/licences/{created.GetProperty("id").GetInt32()}/restaurant", Json(new { tenantId = 1 }));

            var body = await Read(await f.CreateClient().PostAsync("/api/licence/menu", Json(new { key, deviceId = "device-1" })));
            var store = body.GetProperty("store");
            Assert.Equal("24ABCDE1234F1Z5", store.GetProperty("gstin").GetString());
            Assert.Equal("Thank you, visit again", store.GetProperty("receiptFooter").GetString());
            var first = body.GetProperty("version").GetString();

            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Outlets.IgnoreQueryFilters().Where(o => o.TenantId == 1).OrderBy(o => o.Id).First().ReceiptFooter = "Come again";
                db.SaveChanges();
            }
            var changed = await Read(await f.CreateClient().PostAsync("/api/licence/menu", Json(new { key, deviceId = "device-1" })));
            Assert.NotEqual(first, changed.GetProperty("version").GetString());
        }

        [Fact]
        public async Task The_shop_takes_over_the_store_profile_and_settings()
        {
            using var f = Build();
            SeedStore(f);
            var store = new SyncStore("Anand Cafe", "Station Road", "Asia/Kolkata", 4, "{\"billPrefix\":\"ANC\"}",
                "Station Road, Anand", "9876543210", "24ZZZZZ9999Z1Z5", "12345678901234", "Shukriya");
            var snapshot = new MenuSnapshot(true, "s1", new List<SyncCategory>(), new List<SyncStation>(), new List<SyncItem>(),
                new List<SyncOption>(), new List<SyncOption>(), new List<SyncComboPart>(), store);

            using (var scope = f.Services.CreateScope())
                await MenuSyncApplier.Apply(scope.ServiceProvider.GetRequiredService<AppDbContext>(), snapshot, CancellationToken.None);

            using var check = f.Services.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            var outlet = db.Outlets.IgnoreQueryFilters().Where(o => o.TenantId == 1).OrderBy(o => o.Id).First();
            Assert.Equal("Station Road", outlet.Name);
            Assert.Equal("24ZZZZZ9999Z1Z5", outlet.Gstin);
            Assert.Equal("Shukriya", outlet.ReceiptFooter);
            Assert.Equal(4, outlet.BusinessDayStartHour);
            Assert.Contains("ANC", outlet.Settings);
            Assert.Equal("Anand Cafe", db.Tenants.Single(t => t.Id == 1).Name);
        }

        [Fact]
        public async Task A_cloud_managed_shop_cannot_change_settings_or_store_profile_locally()
        {
            using var f = Build();
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LicenceRecords.Add(new LicenceRecord { Id = 1, DeviceId = "device-1", LastSeenUtc = DateTime.UtcNow, MenuFromCloud = true });
                db.SaveChanges();
            }
            var admin = As(f, "admin");
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync("/api/admin/settings", Json(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync("/api/admin/store", Json(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/admin/settings/preset", Json(new { preset = "cafe" }))).StatusCode);
            Assert.NotEqual(HttpStatusCode.Conflict, (await admin.GetAsync("/api/admin/settings")).StatusCode);
        }
    }
}
