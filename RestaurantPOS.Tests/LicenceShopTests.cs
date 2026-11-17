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
    public class LicenceShopTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public LicenceShopTests(WebApplicationFactory<Program> factory) => _factory = factory;

        public void Dispose() => _key.Dispose();

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

        private WebApplicationFactory<Program> Build(FakeCloud cloud, bool required = true)
        {
            var dbName = Guid.NewGuid().ToString();
            var publicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Licence:Required"] = required ? "true" : "false",
                    ["Licence:PublicKey"] = publicKey
                }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.RemoveAll(typeof(ILicenceCloud));
                    services.AddSingleton<ILicenceCloud>(cloud);
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
    }
}
