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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    // 2-step sign-in with an authenticator app: turn on with the app's code, sign in with a code or a backup code, turn off.
    public class TwoFactorTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string Password = "Manager#2026";
        private readonly WebApplicationFactory<Program> _factory;

        public TwoFactorTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("RateLimits:LoginPerMinute", "1000");
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

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string? userId = null)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            if (userId != null) client.DefaultRequestHeaders.Add("X-Test-User", userId);
            return client;
        }

        private static async Task<string> CreateStaff(WebApplicationFactory<Program> f, string email, string role, bool isDevice = false)
        {
            var res = await As(f, "admin").PostAsync("/api/staff", Json(new { email, displayName = "Meena", role, password = Password, isDevice }));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            return (await Read(res)).GetProperty("id").GetString()!;
        }

        private static Task<HttpResponseMessage> Login(WebApplicationFactory<Program> f, string email) =>
            f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }).PostAsync("/api/auth/login", Json(new { email, password = Password }));

        private static Task<HttpResponseMessage> Code(WebApplicationFactory<Program> f, string challenge, string code) =>
            f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }).PostAsync("/api/auth/login/code", Json(new { challenge, code }));

        private static async Task<string> Challenge(WebApplicationFactory<Program> f, string email)
        {
            var res = await Login(f, email);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await Read(res);
            Assert.True(body.GetProperty("twoStep").GetBoolean());
            Assert.Equal("app", body.GetProperty("method").GetString());
            return body.GetProperty("challenge").GetString()!;
        }

        // The 6-digit code an authenticator app shows now (RFC 6238, SHA-1, 30 seconds).
        private static string Totp(string key)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var secret = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in key.Replace(" ", string.Empty).ToUpperInvariant().TrimEnd('='))
            {
                buffer = (buffer << 5) | alphabet.IndexOf(c);
                bits += 5;
                if (bits >= 8)
                {
                    secret.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                    buffer &= (1 << bits) - 1;
                }
            }
            var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);
            using var hmac = new HMACSHA1(secret.ToArray());
            var hash = hmac.ComputeHash(counter);
            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (binary % 1_000_000).ToString("D6");
        }

        [Fact]
        public async Task AuthenticatorApp_TurnOn_SignIn_BackupCode_TurnOff()
        {
            using var f = Build();
            var id = await CreateStaff(f, "meena@test.local", "manager");
            var me = As(f, "manager", id);

            Assert.False((await Read(await me.GetAsync("/api/auth/2fa"))).GetProperty("enabled").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await me.PostAsync("/api/auth/2fa/enable", Json(new { code = "123456" }))).StatusCode);

            var setup = await Read(await me.PostAsync("/api/auth/2fa/setup", Json(new { })));
            var key = setup.GetProperty("key").GetString()!;
            Assert.StartsWith("otpauth://totp/", setup.GetProperty("uri").GetString());
            Assert.Contains("<svg", setup.GetProperty("qrSvg").GetString());

            Assert.Equal(HttpStatusCode.BadRequest, (await me.PostAsync("/api/auth/2fa/enable", Json(new { code = "000000" }))).StatusCode);
            var enabled = await me.PostAsync("/api/auth/2fa/enable", Json(new { code = Totp(key) }));
            Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
            var backups = (await Read(enabled)).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
            Assert.Equal(10, backups.Count);
            var status = await Read(await me.GetAsync("/api/auth/2fa"));
            Assert.True(status.GetProperty("enabled").GetBoolean());
            Assert.Equal(10, status.GetProperty("recoveryCodesLeft").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await me.PostAsync("/api/auth/2fa/setup", Json(new { }))).StatusCode);

            // Sign in: the password alone is not enough any more.
            var challenge = await Challenge(f, "meena@test.local");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, challenge, "123456")).StatusCode);
            var signedIn = await Code(f, challenge, Totp(key));
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
            Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), c => c.StartsWith("pos.auth=", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, challenge, Totp(key))).StatusCode);   // a challenge works once

            // A backup code works once.
            Assert.Equal(HttpStatusCode.OK, (await Code(f, await Challenge(f, "meena@test.local"), backups[0])).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, await Challenge(f, "meena@test.local"), backups[0])).StatusCode);
            Assert.Equal(9, (await Read(await me.GetAsync("/api/auth/2fa"))).GetProperty("recoveryCodesLeft").GetInt32());

            // New backup codes need the app's code; the old ones stop working.
            Assert.Equal(HttpStatusCode.BadRequest, (await me.PostAsync("/api/auth/2fa/recovery-codes", Json(new { code = "000000" }))).StatusCode);
            var fresh = await me.PostAsync("/api/auth/2fa/recovery-codes", Json(new { code = Totp(key) }));
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, await Challenge(f, "meena@test.local"), backups[1])).StatusCode);

            // Turn off: needs a code; then the password alone signs in again.
            Assert.Equal(HttpStatusCode.BadRequest, (await me.PostAsync("/api/auth/2fa/disable", Json(new { code = "000000" }))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsync("/api/auth/2fa/disable", Json(new { code = Totp(key) }))).StatusCode);
            var plain = await Login(f, "meena@test.local");
            Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
            Assert.DoesNotContain("twoStep", await plain.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task WrongCodes_EndTheChallenge_AndAreCountedLikeWrongPasswords()
        {
            using var f = Build();
            var id = await CreateStaff(f, "ravi@test.local", "admin");
            var me = As(f, "admin", id);
            var key = (await Read(await me.PostAsync("/api/auth/2fa/setup", Json(new { })))).GetProperty("key").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await me.PostAsync("/api/auth/2fa/enable", Json(new { code = Totp(key) }))).StatusCode);

            var challenge = await Challenge(f, "ravi@test.local");
            for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, challenge, "000000")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, challenge, Totp(key))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Code(f, "not-a-challenge", Totp(key))).StatusCode);

            // Five wrong codes locked the account, like five wrong passwords.
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f, "ravi@test.local")).StatusCode);
        }

        [Fact]
        public async Task OnlyOwnersAndManagers_AndNotKitchenScreens_UseTheApp()
        {
            using var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/auth/2fa")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync("/api/auth/2fa/setup", Json(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "manager").GetAsync("/api/auth/2fa")).StatusCode);
            var device = await CreateStaff(f, "kds@test.local", "kitchen", isDevice: true);
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "manager", device).PostAsync("/api/auth/2fa/setup", Json(new { }))).StatusCode);
        }
    }
}
