using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class LicenceServerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public LicenceServerTests(WebApplicationFactory<Program> factory) => _factory = factory;

        public void Dispose() => _key.Dispose();

        private WebApplicationFactory<Program> Build(bool withPrivateKey = true)
        {
            var dbName = Guid.NewGuid().ToString();
            var privateKey = Convert.ToBase64String(_key.ExportPkcs8PrivateKey());
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Licence:PrivateKey"] = withPrivateKey ? privateKey : ""
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

        private static async Task<JsonElement> Create(WebApplicationFactory<Program> f, object? body = null)
        {
            var res = await As(f, "platform").PostAsync("/api/platform/licences", Json(body ?? new { customer = "Anand Restaurant", plan = "trial" }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return await Read(res);
        }

        private static Task<HttpResponseMessage> Renew(WebApplicationFactory<Program> f, string key, string device = "device-1") =>
            As(f, "cashier").PostAsync("/api/licence/renew", Json(new { key, deviceId = device, version = "1.7.0" }));

        [Fact]
        public async Task Create_MakesA30DayTrial_WithAReadableKey()
        {
            var f = Build();
            var licence = await Create(f);
            Assert.Matches(new Regex("^POS(-[A-HJKMNP-TV-Z2-9]{5}){4}$"), licence.GetProperty("key").GetString()!);
            Assert.Equal("Trial", licence.GetProperty("state").GetString());
            var days = (licence.GetProperty("paidUntil").GetDateTime() - DateTime.UtcNow).TotalDays;
            Assert.InRange(days, 29, 31);

            var list = await Read(await As(f, "platform").GetAsync("/api/platform/licences"));
            Assert.Single(list.EnumerateArray());
        }

        [Fact]
        public async Task Renew_SendsTheSupportDetails()
        {
            var f = Build();
            var saved = await As(f, "platform").PutAsync("/api/platform/support", Json(new { companyName = "Vyom Solutions", productName = "RestaurantPOS", phone = "9429549007" }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var licence = await Create(f);
            var res = await Renew(f, licence.GetProperty("key").GetString()!);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var support = (await Read(res)).GetProperty("support");
            Assert.Equal("Vyom Solutions", support.GetProperty("companyName").GetString());
            Assert.Equal("9429549007", support.GetProperty("phone").GetString());
        }

        [Fact]
        public async Task Updates_CanBePausedOrPinned_AndTheShopIsTold()
        {
            var f = Build();
            var licence = await Create(f);
            var id = licence.GetProperty("id").GetInt32();
            Assert.Equal("auto", licence.GetProperty("updateMode").GetString());

            var noVersion = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/updates", Json(new { mode = "pinned" }));
            Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
            var badMode = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/updates", Json(new { mode = "sometimes" }));
            Assert.Equal(HttpStatusCode.BadRequest, badMode.StatusCode);
            var owner = await As(f, "admin").PostAsync($"/api/platform/licences/{id}/updates", Json(new { mode = "paused" }));
            Assert.Equal(HttpStatusCode.Forbidden, owner.StatusCode);

            var pinned = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/updates", Json(new { mode = "pinned", version = "v1.7.3" }));
            Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
            Assert.Equal("1.7.3", (await Read(pinned)).GetProperty("pinnedVersion").GetString());

            var renewed = await Read(await Renew(f, licence.GetProperty("key").GetString()!));
            Assert.Equal("pinned", renewed.GetProperty("updates").GetProperty("mode").GetString());
            Assert.Equal("1.7.3", renewed.GetProperty("updates").GetProperty("version").GetString());

            var auto = await Read(await As(f, "platform").PostAsync($"/api/platform/licences/{id}/updates", Json(new { mode = "auto", version = "1.7.3" })));
            Assert.Equal(JsonValueKind.Null, auto.GetProperty("pinnedVersion").ValueKind);
        }

        [Fact]
        public async Task OwnerResetCode_IsMadeBySupport_AndOnlyItsHashGoesToTheShop()
        {
            var f = Build();
            var licence = await Create(f);
            var id = licence.GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "admin").PostAsync($"/api/platform/licences/{id}/owner-reset", null)).StatusCode);

            var made = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/owner-reset", null);
            Assert.Equal(HttpStatusCode.OK, made.StatusCode);
            var code = (await Read(made)).GetProperty("code").GetString()!;
            Assert.Matches(new Regex("^[A-HJKMNP-TV-Z2-9]{4}-[A-HJKMNP-TV-Z2-9]{4}$"), code);

            var renewed = await Read(await Renew(f, licence.GetProperty("key").GetString()!));
            var reset = renewed.GetProperty("ownerReset");
            Assert.Equal(POSSystem.Platform.OwnerReset.Hash(code), reset.GetProperty("hash").GetString());
            Assert.DoesNotContain(code, renewed.GetRawText());
        }

        [Fact]
        public async Task Create_ChecksTheForm()
        {
            var f = Build();
            var noName = await As(f, "platform").PostAsync("/api/platform/licences", Json(new { customer = " ", plan = "trial" }));
            Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
            Assert.True((await Read(noName)).GetProperty("errors").TryGetProperty("customer", out _));

            var badPlan = await As(f, "platform").PostAsync("/api/platform/licences", Json(new { customer = "A", plan = "forever" }));
            Assert.Equal(HttpStatusCode.BadRequest, badPlan.StatusCode);
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("manager")]
        [InlineData("cashier")]
        public async Task ManagingLicences_IsOnlyForThePlatformOwner(string role)
        {
            var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).GetAsync("/api/platform/licences")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).PostAsync("/api/platform/licences", Json(new { customer = "X" }))).StatusCode);
        }

        [Fact]
        public async Task Renew_GivesASignedLease_ThatTheShopPcAccepts()
        {
            var f = Build();
            var key = (await Create(f)).GetProperty("key").GetString()!;

            var res = await Renew(f, key.ToLowerInvariant());
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            var file = new LicenceFile(body.GetProperty("licence").GetProperty("payload").GetString()!, body.GetProperty("licence").GetProperty("signature").GetString()!);

            var status = Licence.Evaluate(file, _key, DateTime.UtcNow, null);
            Assert.False(status.Locked);
            Assert.Equal("Anand Restaurant", status.Customer);
            Assert.Equal("trial", status.Plan);
            var lease = (status.LeaseUntil!.Value - DateTime.UtcNow).TotalDays;
            Assert.InRange(lease, 6.9, 7.1);
            Assert.InRange((body.GetProperty("serverTimeUtc").GetDateTime() - DateTime.UtcNow).TotalMinutes, -2, 2);
        }

        [Fact]
        public async Task Renew_LeaseNeverReachesPastThePaidPeriod()
        {
            var f = Build();
            var key = (await Create(f, new { customer = "Short", plan = "trial", days = 3 })).GetProperty("key").GetString()!;
            var body = await Read(await Renew(f, key));
            var file = new LicenceFile(body.GetProperty("licence").GetProperty("payload").GetString()!, body.GetProperty("licence").GetProperty("signature").GetString()!);
            var payload = Licence.Verify(file, _key)!;
            Assert.Equal(payload.PaidUntil, payload.LeaseUntil);
        }

        [Fact]
        public async Task Renew_UnknownKey_IsNotFound()
        {
            var f = Build();
            var res = await Renew(f, "POS-AAAAA-AAAAA-AAAAA-AAAAA");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Contains("not known", (await Read(res)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task Revoke_StopsRenewing_AndRestoreStartsAgain()
        {
            var f = Build();
            var licence = await Create(f);
            var id = licence.GetProperty("id").GetInt32();
            var key = licence.GetProperty("key").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await Renew(f, key)).StatusCode);

            var revoked = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/revoke", null);
            Assert.Equal("Revoked", (await Read(revoked)).GetProperty("state").GetString());
            var refused = await Renew(f, key);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("stopped", (await Read(refused)).GetProperty("message").GetString());

            await As(f, "platform").PostAsync($"/api/platform/licences/{id}/restore", null);
            Assert.Equal(HttpStatusCode.OK, (await Renew(f, key)).StatusCode);
        }

        [Fact]
        public async Task Renew_AfterThePaidPeriod_IsRefused_UntilExtended()
        {
            var f = Build();
            var licence = await Create(f);
            var id = licence.GetProperty("id").GetInt32();
            var key = licence.GetProperty("key").GetString()!;
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ShopLicences.Single(x => x.Id == id).PaidUntil = DateTime.UtcNow.AddDays(-2);
                db.SaveChanges();
            }

            var refused = await Renew(f, key);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("ended", (await Read(refused)).GetProperty("message").GetString());

            // Extending from an ended licence counts from today.
            var extended = await Read(await As(f, "platform").PostAsync($"/api/platform/licences/{id}/extend", Json(new { days = 365, plan = "yearly" })));
            Assert.Equal("Active", extended.GetProperty("state").GetString());
            Assert.InRange((extended.GetProperty("paidUntil").GetDateTime() - DateTime.UtcNow).TotalDays, 364, 366);
            Assert.Equal(HttpStatusCode.OK, (await Renew(f, key)).StatusCode);
        }

        [Fact]
        public async Task Extend_AddsToTheEndOfThePaidPeriod()
        {
            var f = Build();
            var licence = await Create(f);
            var id = licence.GetProperty("id").GetInt32();
            var extended = await Read(await As(f, "platform").PostAsync($"/api/platform/licences/{id}/extend", Json(new { days = 365 })));
            Assert.InRange((extended.GetProperty("paidUntil").GetDateTime() - DateTime.UtcNow).TotalDays, 394, 396);

            var bad = await As(f, "platform").PostAsync($"/api/platform/licences/{id}/extend", Json(new { days = 0 }));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var missing = await As(f, "platform").PostAsync("/api/platform/licences/999/extend", Json(new { days = 5 }));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        [Fact]
        public async Task KeyBelongsToTheFirstPc_UntilTheOwnerResetsIt()
        {
            var f = Build();
            var licence = await Create(f);
            var id = licence.GetProperty("id").GetInt32();
            var key = licence.GetProperty("key").GetString()!;

            Assert.Equal(HttpStatusCode.OK, (await Renew(f, key, "pc-1")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Renew(f, key, "pc-1")).StatusCode);
            var other = await Renew(f, key, "pc-2");
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
            Assert.Contains("another PC", (await Read(other)).GetProperty("message").GetString());

            await As(f, "platform").PostAsync($"/api/platform/licences/{id}/reset-device", null);
            Assert.Equal(HttpStatusCode.OK, (await Renew(f, key, "pc-2")).StatusCode);
        }

        [Fact]
        public async Task Renew_WithoutAPrivateKey_IsNotAvailable()
        {
            var f = Build(withPrivateKey: false);
            var key = (await Create(f)).GetProperty("key").GetString()!;
            var res = await Renew(f, key);
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Contains("not switched on", (await Read(res)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task Renew_RemembersLastContactAndVersion()
        {
            var f = Build();
            var key = (await Create(f)).GetProperty("key").GetString()!;
            await Renew(f, key);
            var item = (await Read(await As(f, "platform").GetAsync("/api/platform/licences"))).EnumerateArray().Single();
            Assert.Equal("1.7.0", item.GetProperty("lastVersion").GetString());
            Assert.True(item.GetProperty("deviceBound").GetBoolean());
            Assert.NotEqual(JsonValueKind.Null, item.GetProperty("lastSeenAt").ValueKind);
        }
    }
}
