using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
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
    public class LicenceShopTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // Stands in for the folder shared with deploy/auto-update.sh (one per test).
        private readonly string _updates = Directory.CreateTempSubdirectory("pos-updates-").FullName;

        public LicenceShopTests(WebApplicationFactory<Program> factory) => _factory = factory;

        public void Dispose()
        {
            _key.Dispose();
            try { Directory.Delete(_updates, true); } catch (IOException) { }
        }

        private class FakeCloud : ILicenceCloud
        {
            public Func<RenewRequest, (RenewResponse?, string?)> Handler { get; set; } = _ => (null, "Could not reach the licence server.");
            public string? LastKey { get; private set; }

            public Task<(RenewResponse? Response, string? Error)> Renew(RenewRequest request, CancellationToken ct)
            {
                LastKey = request.Key;
                return Task.FromResult(Handler(request));
            }
        }

        // A clock the test can move, like someone changing the PC's date.
        private sealed class MovableClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private WebApplicationFactory<Program> Build(FakeCloud cloud, bool required = true, TimeProvider? clock = null)
        {
            var dbName = Guid.NewGuid().ToString();
            var publicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Licence:Required"] = required ? "true" : "false",
                    ["Licence:PublicKey"] = publicKey,
                    ["Updates:Folder"] = _updates
                }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.RemoveAll(typeof(ILicenceCloud));
                    services.AddSingleton<ILicenceCloud>(cloud);
                    if (clock is not null)
                    {
                        services.RemoveAll(typeof(TimeProvider));
                        services.AddSingleton<TimeProvider>(clock);
                    }
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

        private (RenewResponse?, string?) Signed(int issuedDaysAgo = 0, int leaseDaysLeft = 7, int paidDaysLeft = 300, ECDsa? signer = null)
        {
            var now = DateTime.UtcNow;
            var payload = new LicencePayload("L1", "Anand Restaurant", "yearly", now.AddDays(-issuedDaysAgo), now.AddDays(leaseDaysLeft), now.AddDays(paidDaysLeft));
            return (new RenewResponse(POSSystem.Platform.Licence.Sign(payload, signer ?? _key), now), null);
        }

        private static StringContent OpenShift() => Json(new { openingCash = 100 });

        [Fact]
        public async Task NotRequired_NothingIsBlocked()
        {
            var f = Build(new FakeCloud(), required: false);
            var status = await As(f, "cashier").GetAsync("/api/licence/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var body = await Read(status);
            Assert.Equal("Active", body.GetProperty("state").GetString());
            Assert.False(body.GetProperty("locked").GetBoolean());
            Assert.False(body.GetProperty("required").GetBoolean());

            var write = await As(f, "cashier").PostAsync("/api/shifts/open", OpenShift());
            Assert.NotEqual(HttpStatusCode.PaymentRequired, write.StatusCode);
        }

        [Fact]
        public async Task Required_WithoutLicence_BlocksWrites_ButNotReadsOrSignIn()
        {
            var f = Build(new FakeCloud());
            var status = await Read(await As(f, "cashier").GetAsync("/api/licence/status"));
            Assert.Equal("Missing", status.GetProperty("state").GetString());
            Assert.True(status.GetProperty("locked").GetBoolean());

            var write = await As(f, "cashier").PostAsync("/api/shifts/open", OpenShift());
            Assert.Equal(HttpStatusCode.PaymentRequired, write.StatusCode);
            Assert.Equal("licence-locked", (await Read(write)).GetProperty("code").GetString());

            Assert.Equal(HttpStatusCode.OK, (await As(f, "cashier").GetAsync("/api/shifts/current")).StatusCode);
            var login = await As(f, "cashier").PostAsync("/api/auth/login", Json(new { email = "x@y.z", password = "wrong" }));
            Assert.NotEqual(HttpStatusCode.PaymentRequired, login.StatusCode);
        }

        [Fact]
        public async Task Activate_WithValidLicence_UnlocksWrites()
        {
            var cloud = new FakeCloud { Handler = _ => Signed() };
            var f = Build(cloud);

            var res = await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = " ABC-123 " }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            Assert.Equal("Active", body.GetProperty("state").GetString());
            Assert.Equal("Anand Restaurant", body.GetProperty("customer").GetString());
            Assert.Equal("ABC-123", cloud.LastKey);

            var write = await As(f, "cashier").PostAsync("/api/shifts/open", OpenShift());
            Assert.NotEqual(HttpStatusCode.PaymentRequired, write.StatusCode);
        }

        [Fact]
        public async Task Activate_AsCashier_IsForbidden()
        {
            var f = Build(new FakeCloud { Handler = _ => Signed() });
            var res = await As(f, "cashier").PostAsync("/api/licence/activate", Json(new { key = "ABC" }));
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        [Fact]
        public async Task Activate_WithEmptyKey_IsBadRequest()
        {
            var f = Build(new FakeCloud());
            var res = await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "  " }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task Activate_WhenServerCannotBeReached_ExplainsAndStaysLocked()
        {
            var f = Build(new FakeCloud());
            var res = await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "ABC" }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("reach", (await Read(res)).GetProperty("message").GetString());
            Assert.True((await Read(await As(f, "admin").GetAsync("/api/licence/status"))).GetProperty("locked").GetBoolean());
        }

        [Fact]
        public async Task Activate_WithLicenceFromAnotherKey_IsRefused()
        {
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var f = Build(new FakeCloud { Handler = _ => Signed(signer: other) });
            var res = await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "ABC" }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("Missing", (await Read(await As(f, "admin").GetAsync("/api/licence/status"))).GetProperty("state").GetString());
        }

        [Fact]
        public async Task LeaseRunOut_LocksWritesAgain()
        {
            var f = Build(new FakeCloud { Handler = _ => Signed(issuedDaysAgo: 8, leaseDaysLeft: -1) });
            var res = await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "ABC" }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("LeaseExpired", (await Read(res)).GetProperty("state").GetString());
            Assert.Equal(HttpStatusCode.PaymentRequired, (await As(f, "cashier").PostAsync("/api/shifts/open", OpenShift())).StatusCode);
        }

        [Fact]
        public async Task Refresh_UsesTheStoredKey_AndNeedsOne()
        {
            var cloud = new FakeCloud { Handler = _ => Signed() };
            var f = Build(cloud);
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "admin").PostAsync("/api/licence/refresh", null)).StatusCode);

            await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }));
            cloud.Handler = _ => Signed(leaseDaysLeft: 6);
            var res = await As(f, "admin").PostAsync("/api/licence/refresh", null);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("KEY-1", cloud.LastKey);
        }

        [Fact]
        public async Task ClockWasAhead_UnlocksAfterTheServerConfirmsTheTime()
        {
            var cloud = new FakeCloud { Handler = _ => Signed() };
            var f = Build(cloud);
            var file = Signed().Item1!.Licence;
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LicenceRecords.Add(new LicenceRecord
                {
                    Id = 1, DeviceId = "device-1", LicenceKey = "KEY-1", Payload = file.Payload, Signature = file.Signature,
                    LastSeenUtc = DateTime.UtcNow.AddDays(2) // the clock was once set two days ahead
                });
                db.SaveChanges();
            }

            var before = await Read(await As(f, "admin").GetAsync("/api/licence/status"));
            Assert.Equal("ClockBack", before.GetProperty("state").GetString());
            Assert.True(before.GetProperty("locked").GetBoolean());

            var refreshed = await As(f, "admin").PostAsync("/api/licence/refresh", null);
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            Assert.Equal("Active", (await Read(refreshed)).GetProperty("state").GetString());
        }

        [Fact]
        public async Task Renewal_CopiesTheSupportDetails_ForTheAboutPage()
        {
            var support = new SupportInfoDto("Vyom Solutions", "RestaurantPOS", "9429549007", null, "help@vyom.test", "8 AM - 10 AM", null);
            var f = Build(new FakeCloud { Handler = _ => (Signed().Item1! with { Support = support }, null) });
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }))).StatusCode);

            var about = await Read(await As(f, "admin").GetAsync("/api/about"));
            Assert.Equal("9429549007", about.GetProperty("phone").GetString());
            Assert.Equal("help@vyom.test", about.GetProperty("email").GetString());
            Assert.True(about.GetProperty("shopPc").GetBoolean());
            Assert.Equal("Anand Restaurant", about.GetProperty("licenceCustomer").GetString());
            Assert.Equal(8, about.GetProperty("deviceCode").GetString()!.Length);
        }

        [Fact]
        public async Task Renewal_WritesSupportsUpdateSetting_ForTheUpdateScript()
        {
            var f = Build(new FakeCloud { Handler = _ => (Signed().Item1! with { Updates = new UpdatePolicyDto("pinned", "1.7.3") }, null) });
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }))).StatusCode);
            Assert.Equal("mode=pinned\nversion=1.7.3\n", File.ReadAllText(Path.Combine(_updates, "policy")));

            var updates = await Read(await As(f, "admin").GetAsync("/api/licence/updates"));
            Assert.True(updates.GetProperty("available").GetBoolean());
            Assert.Equal("pinned", updates.GetProperty("mode").GetString());
            Assert.Equal("1.7.3", updates.GetProperty("pinnedVersion").GetString());
        }

        [Fact]
        public async Task Renewal_NeverPassesAStrangeVersionToTheUpdateScript()
        {
            var f = Build(new FakeCloud { Handler = _ => (Signed().Item1! with { Updates = new UpdatePolicyDto("pinned", "1.7.3; rm -rf /") }, null) });
            await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }));
            Assert.Equal("mode=auto\nversion=\n", File.ReadAllText(Path.Combine(_updates, "policy")));
        }

        [Fact]
        public async Task UpdateNow_LeavesARequest_AndShowsTheLastResult()
        {
            var f = Build(new FakeCloud());
            File.WriteAllText(Path.Combine(_updates, "status.json"),
                "{\"checkedAt\":\"2026-11-19T22:00:00Z\",\"trigger\":\"nightly\",\"current\":\"1.7.4\",\"target\":\"1.7.4\",\"result\":\"up-to-date\",\"message\":\"Version 1.7.4 is the newest.\"}\n");

            var before = await Read(await As(f, "admin").GetAsync("/api/licence/updates"));
            Assert.Equal("up-to-date", before.GetProperty("status").GetProperty("result").GetString());
            Assert.Equal("1.7.4", before.GetProperty("status").GetProperty("current").GetString());
            Assert.False(before.GetProperty("requested").GetBoolean());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PostAsync("/api/licence/updates/run", null)).StatusCode);
            var run = await As(f, "admin").PostAsync("/api/licence/updates/run", null);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            Assert.True((await Read(run)).GetProperty("requested").GetBoolean());
            Assert.True(File.Exists(Path.Combine(_updates, "request")));
        }

        [Fact]
        public async Task UpdateNow_WhenSupportPausedUpdates_IsRefused()
        {
            var f = Build(new FakeCloud());
            File.WriteAllText(Path.Combine(_updates, "policy"), "mode=paused\nversion=\n");
            var run = await As(f, "admin").PostAsync("/api/licence/updates/run", null);
            Assert.Equal(HttpStatusCode.BadRequest, run.StatusCode);
            Assert.False(File.Exists(Path.Combine(_updates, "request")));
        }

        [Fact]
        public async Task Backups_WarnWhenTheLastCopyIsOld()
        {
            var f = Build(new FakeCloud());
            var fresh = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            var old = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            void Write(string copiedAt, string result) => File.WriteAllText(Path.Combine(_updates, "backup-status.json"),
                $"{{\"checkedAt\":\"{fresh}\",\"backupAt\":\"{fresh}\",\"lastCopiedAt\":\"{copiedAt}\",\"target\":\"USB disk\",\"result\":\"{result}\",\"message\":\"x\"}}\n");

            // The copy script has not run yet: nothing to warn about.
            var none = await Read(await As(f, "admin").GetAsync("/api/licence/backups"));
            Assert.True(none.GetProperty("available").GetBoolean());
            Assert.False(none.GetProperty("warning").GetBoolean());

            Write(fresh, "copied");
            Assert.False((await Read(await As(f, "admin").GetAsync("/api/licence/backups"))).GetProperty("warning").GetBoolean());

            Write(old, "no-disk");
            var stale = await Read(await As(f, "admin").GetAsync("/api/licence/backups"));
            Assert.True(stale.GetProperty("warning").GetBoolean());
            Assert.Equal("no-disk", stale.GetProperty("result").GetString());

            // Never copied at all.
            Write("", "no-disk");
            Assert.True((await Read(await As(f, "admin").GetAsync("/api/licence/backups"))).GetProperty("warning").GetBoolean());
        }

        [Fact]
        public async Task BackUpNow_LeavesARequest_ForTheOwnerOnly()
        {
            var f = Build(new FakeCloud());
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PostAsync("/api/licence/backups/run", null)).StatusCode);
            var run = await As(f, "admin").PostAsync("/api/licence/backups/run", null);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            Assert.True((await Read(run)).GetProperty("requested").GetBoolean());
            Assert.True(File.Exists(Path.Combine(_updates, "backup-request")));
        }

        [Fact]
        public async Task OwnerReset_WithTheCodeFromSupport_ChangesThePasswordOnce()
        {
            var f = Build(new FakeCloud { Handler = _ => SignedWithReset("ABCD-EFGH") });
            await AddUser(f, "owner@shop.test", "admin");
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }))).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await ResetOwner(f, "owner@shop.test", "ZZZZ-ZZZZ", "New#Pass2026")).StatusCode);
            // A weak password is refused without using up the code.
            Assert.Equal(HttpStatusCode.BadRequest, (await ResetOwner(f, "owner@shop.test", "ABCD-EFGH", "short")).StatusCode);

            var ok = await ResetOwner(f, "owner@shop.test", "abcd efgh", "New#Pass2026");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.True(await PasswordIs(f, "owner@shop.test", "New#Pass2026"));

            // Used once; the next renewal brings the same code again, but it is not taken.
            Assert.Equal(HttpStatusCode.BadRequest, (await ResetOwner(f, "owner@shop.test", "ABCD-EFGH", "Other#Pass2026")).StatusCode);
            Assert.True(await PasswordIs(f, "owner@shop.test", "New#Pass2026"));
        }

        [Fact]
        public async Task OwnerReset_IsOnlyForTheOwner()
        {
            var f = Build(new FakeCloud { Handler = _ => SignedWithReset("ABCD-EFGH") });
            await AddUser(f, "cashier@shop.test", "cashier");
            await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }));
            Assert.Equal(HttpStatusCode.BadRequest, (await ResetOwner(f, "cashier@shop.test", "ABCD-EFGH", "New#Pass2026")).StatusCode);
            Assert.True(await PasswordIs(f, "cashier@shop.test", "Old#Pass2026"));
        }

        [Fact]
        public async Task OwnerReset_IsNotOnTheCloud()
        {
            var f = Build(new FakeCloud(), required: false);
            Assert.Equal(HttpStatusCode.NotFound, (await ResetOwner(f, "owner@shop.test", "ABCD-EFGH", "New#Pass2026")).StatusCode);
        }

        private static async Task<AppUser> AddUser(WebApplicationFactory<Program> f, string email, string role)
        {
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Test", TenantId = 1, OutletId = 1 };
            Assert.True((await users.CreateAsync(user, "Old#Pass2026")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        private static async Task<bool> PasswordIs(WebApplicationFactory<Program> f, string email, string password)
        {
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            return await users.CheckPasswordAsync((await users.FindByEmailAsync(email))!, password);
        }

        private static Task<HttpResponseMessage> ResetOwner(WebApplicationFactory<Program> f, string email, string code, string password) =>
            As(f, "cashier").PostAsync("/api/auth/owner-reset", Json(new { email, code, newPassword = password }));

        private (RenewResponse?, string?) SignedWithReset(string code) =>
            (Signed().Item1! with { OwnerReset = new OwnerResetInfoDto(OwnerReset.Hash(code), DateTime.UtcNow.AddHours(24)) }, null);

        [Fact]
        public async Task ClockSetBack_LocksAtOnce_EvenRightAfterACheck()
        {
            var clock = new MovableClock();
            var f = Build(new FakeCloud { Handler = _ => Signed() }, clock: clock);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/licence/activate", Json(new { key = "KEY-1" }))).StatusCode);
            Assert.False((await Read(await As(f, "admin").GetAsync("/api/licence/status"))).GetProperty("locked").GetBoolean());

            clock.Now = clock.Now.AddDays(-1); // someone sets the PC clock back a day, seconds after the last check

            var status = await Read(await As(f, "admin").GetAsync("/api/licence/status"));
            Assert.Equal("ClockBack", status.GetProperty("state").GetString());
            Assert.True(status.GetProperty("locked").GetBoolean());
            Assert.Equal(HttpStatusCode.PaymentRequired, (await As(f, "cashier").PostAsync("/api/shifts/open", OpenShift())).StatusCode);
        }
    }
}
