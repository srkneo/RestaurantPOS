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
using Microsoft.AspNetCore.Identity;
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
    // SUP2: support signs in on a shop PC with today's code. The platform shows the code; it works only today and only on that PC.
    public class SupportDayLoginTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private const string Device = "dev-0000000001";
        private const string Wrong = "The code is wrong or not for today. Ask support for today's code.";
        private const string NeedsFreshLease = "Day codes need a fresh licence on this PC. Connect it to the internet once, then try again.";
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly Clock _clock = new() { Now = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero) };

        public SupportDayLoginTests(WebApplicationFactory<Program> factory) => _factory = factory;

        public void Dispose() => _key.Dispose();

        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }

        // One app plays both parts: the cloud (private key, shop licences) and the shop PC (public key, licence record).
        private WebApplicationFactory<Program> Build(bool withPrivateKey = true)
        {
            var dbName = Guid.NewGuid().ToString();
            var publicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
            var privateKey = withPrivateKey ? Convert.ToBase64String(_key.ExportPkcs8PrivateKey()) : "";
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("RateLimits:LoginPerMinute", "1000");
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Licence:Required"] = "false",
                    ["Licence:PublicKey"] = publicKey,
                    ["Licence:PrivateKey"] = privateKey
                }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.RemoveAll(typeof(TimeProvider));
                    services.AddSingleton<TimeProvider>(_clock);
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                });
            });
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", role == "platform" ? "0" : "1");
            client.DefaultRequestHeaders.Add("X-Test-User", "caller-1");
            return client;
        }

        private static async Task<int> MakeRestaurant(WebApplicationFactory<Program> f, string settings = "{}")
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // A shop PC has one restaurant: the seeded restaurant 1 with outlet 1. The day login takes the first restaurant.
            db.CurrentTenantId = 1;
            var outlet = await db.Outlets.IgnoreQueryFilters().Where(o => o.TenantId == 1).OrderBy(o => o.Id).FirstAsync();
            outlet.Settings = settings;
            await db.SaveChangesAsync();
            return 1;
        }

        // The cloud side: a licence that a PC has used.
        private static async Task<int> MakeLicence(WebApplicationFactory<Program> f, string? device = Device, bool revoked = false)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var licence = new ShopLicence
            {
                Key = "POS-T" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Customer = "Day Shop", Plan = "yearly",
                PaidUntil = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                DeviceId = device, Revoked = revoked
            };
            db.ShopLicences.Add(licence);
            await db.SaveChangesAsync();
            return licence.Id;
        }

        // The shop side: the lease this PC keeps.
        private static async Task KeepLease(WebApplicationFactory<Program> f, string payload, string signature)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.LicenceRecords.FirstOrDefaultAsync(x => x.Id == 1);
            if (record is null)
            {
                record = new LicenceRecord { Id = 1, LastSeenUtc = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc) };
                db.LicenceRecords.Add(record);
            }
            record.DeviceId = Device;
            record.Payload = payload;
            record.Signature = signature;
            await db.SaveChangesAsync();
        }

        // A real renewal, so the kept lease carries the support secret exactly as the cloud signs it.
        private static async Task KeepRenewedLease(WebApplicationFactory<Program> f, int licenceId)
        {
            string key;
            using (var scope = f.Services.CreateScope())
                key = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShopLicences.FirstAsync(x => x.Id == licenceId)).Key;
            var res = await As(f, "cashier").PostAsync("/api/licence/renew", Json(new { key, deviceId = Device, version = "2.3.3" }));
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var lease = doc.RootElement.GetProperty("licence");
            await KeepLease(f, lease.GetProperty("payload").GetString()!, lease.GetProperty("signature").GetString()!);
        }

        private static async Task<JsonElement> SupportCode(WebApplicationFactory<Program> f, int licenceId)
        {
            var res = await As(f, "platform").GetAsync($"/api/platform/licences/{licenceId}/support-code");
            Assert.True(res.StatusCode == HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static Task<HttpResponseMessage> DayLogin(WebApplicationFactory<Program> f, object body) =>
            f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }).PostAsync("/api/auth/support-day-login", Json(body));

        private static async Task<string> Message(HttpResponseMessage res)
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("message").GetString()!;
        }

        [Fact]
        public async Task Platform_GetsTodaysCode_ForTheShopPc()
        {
            using var f = Build();
            var id = await MakeLicence(f);
            var answer = await SupportCode(f, id);
            Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", answer.GetProperty("code").GetString()!);
            Assert.Equal("2026-11-27", answer.GetProperty("day").GetString());
            Assert.Equal("DEV-0000", answer.GetProperty("deviceCode").GetString());
        }

        [Fact]
        public async Task DayCode_SignsInAsSupport_OnTheShopPc()
        {
            using var f = Build();
            var tenantId = await MakeRestaurant(f);
            var id = await MakeLicence(f);
            await KeepRenewedLease(f, id);
            var code = (await SupportCode(f, id)).GetProperty("code").GetString()!;

            var res = await DayLogin(f, new { code });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            Assert.True(res.Headers.Contains("Set-Cookie"));

            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var support = await users.FindByEmailAsync($"support-t{tenantId}@support.local");
            Assert.NotNull(support);
            Assert.True(support!.IsSupport);
            Assert.Equal(tenantId, support.TenantId);
            Assert.True(await users.IsInRoleAsync(support, "admin"));
            Assert.True(await users.IsInRoleAsync(support, "support"));
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId && a.Action == "SupportLogin" && a.Details!.Contains("day code")));
            var dayAudit = await db.AuditLogs.IgnoreQueryFilters().FirstAsync(a => a.TenantId == tenantId && a.Action == "SupportLogin");
            // Details is a json column in PostgreSQL: it must be valid JSON.
            using (System.Text.Json.JsonDocument.Parse(dayAudit.Details!)) { }
        }

        [Fact]
        public async Task DayCode_TypedInLowerCaseWithoutTheDash_StillWorks()
        {
            using var f = Build();
            await MakeRestaurant(f);
            var id = await MakeLicence(f);
            await KeepRenewedLease(f, id);
            var code = (await SupportCode(f, id)).GetProperty("code").GetString()!;
            var res = await DayLogin(f, new { code = " " + code.Replace("-", "").ToLowerInvariant() + " " });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task YesterdaysCode_IsRefused()
        {
            using var f = Build();
            await MakeRestaurant(f);
            var id = await MakeLicence(f);
            await KeepRenewedLease(f, id);
            var code = (await SupportCode(f, id)).GetProperty("code").GetString()!;
            _clock.Now = _clock.Now.AddDays(1);

            var res = await DayLogin(f, new { code });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal(Wrong, await Message(res));
        }

        [Fact]
        public async Task CodeOfAnotherPc_IsRefused()
        {
            using var f = Build();
            await MakeRestaurant(f);
            var id = await MakeLicence(f);
            await KeepRenewedLease(f, id);
            var other = await MakeLicence(f, "dev-other-0002");
            var code = (await SupportCode(f, other)).GetProperty("code").GetString()!;

            var res = await DayLogin(f, new { code });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal(Wrong, await Message(res));
        }

        [Fact]
        public async Task MissingOrTooLongCode_IsRefused()
        {
            using var f = Build();
            await MakeRestaurant(f);
            var id = await MakeLicence(f);
            await KeepRenewedLease(f, id);

            var empty = await DayLogin(f, new { code = "  " });
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal("Enter the code from support.", await Message(empty));

            var missing = await DayLogin(f, new { });
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal("Enter the code from support.", await Message(missing));

            var tooLong = await DayLogin(f, new { code = new string('A', 500) });
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Equal(Wrong, await Message(tooLong));
        }

        [Fact]
        public async Task OldLeaseWithoutASecret_AsksForAFreshLicence()
        {
            using var f = Build();
            await MakeRestaurant(f);
            var now = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { licenceId = "1", customer = "Old Shop", plan = "yearly", issuedAt = now, leaseUntil = now.AddDays(7), paidUntil = now.AddDays(300) },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await KeepLease(f, Convert.ToBase64String(bytes), Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256)));

            var res = await DayLogin(f, new { code = "ABCD-EFGH" });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal(NeedsFreshLease, await Message(res));
        }

        [Fact]
        public async Task PcWithoutALease_AsksForAFreshLicence()
        {
            using var f = Build();
            await MakeRestaurant(f);
            var res = await DayLogin(f, new { code = "ABCD-EFGH" });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal(NeedsFreshLease, await Message(res));
        }

        [Fact]
        public async Task ForgedLease_AsksForAFreshLicence()
        {
            using var f = Build();
            await MakeRestaurant(f);
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);
            var secret = SupportDayCode.SecretFor("forger", 1, Device);
            var forged = Licence.Sign(new LicencePayload("1", "Forged", "yearly", now, now.AddDays(7), now.AddDays(300), secret), otherKey);
            await KeepLease(f, forged.Payload, forged.Signature);

            var res = await DayLogin(f, new { code = SupportDayCode.CodeFor(secret, Device, new DateOnly(2026, 11, 27)) });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal(NeedsFreshLease, await Message(res));
        }

        [Fact]
        public async Task SupportAccessSwitchedOff_IsRefused_AndNoSupportUserIsMade()
        {
            using var f = Build();
            var tenantId = await MakeRestaurant(f, "{\"allowSupportAccess\":false}");
            var id = await MakeLicence(f);
            await KeepRenewedLease(f, id);
            var code = (await SupportCode(f, id)).GetProperty("code").GetString()!;

            var res = await DayLogin(f, new { code });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("This restaurant has turned off support access.", await Message(res));
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Null(await users.FindByEmailAsync($"support-t{tenantId}@support.local"));
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("manager")]
        [InlineData("cashier")]
        public async Task SupportCode_IsOnlyForThePlatform(string role)
        {
            using var f = Build();
            var id = await MakeLicence(f);
            var res = await As(f, role).GetAsync($"/api/platform/licences/{id}/support-code");
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        [Fact]
        public async Task SupportCode_UnknownStoppedOrUnusedLicence_IsRefused()
        {
            using var f = Build();
            var unknown = await As(f, "platform").GetAsync("/api/platform/licences/99999/support-code");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("Licence not found.", await Message(unknown));

            var stopped = await As(f, "platform").GetAsync($"/api/platform/licences/{await MakeLicence(f, Device, revoked: true)}/support-code");
            Assert.Equal(HttpStatusCode.BadRequest, stopped.StatusCode);
            Assert.Equal("This licence has been stopped.", await Message(stopped));

            var unused = await As(f, "platform").GetAsync($"/api/platform/licences/{await MakeLicence(f, null)}/support-code");
            Assert.Equal(HttpStatusCode.BadRequest, unused.StatusCode);
            Assert.Equal("This licence is not used on a PC yet.", await Message(unused));
        }

        [Fact]
        public async Task SupportCode_WithoutTheLicenceServer_IsNotAvailable()
        {
            using var f = Build(withPrivateKey: false);
            var id = await MakeLicence(f);
            var res = await As(f, "platform").GetAsync($"/api/platform/licences/{id}/support-code");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Equal("The licence server is not switched on here.", await Message(res));
        }
    }
}
