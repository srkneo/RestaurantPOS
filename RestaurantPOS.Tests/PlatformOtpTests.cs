using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    // The platform owner needs the Telegram code after the password; restaurant staff do not.
    public class PlatformOtpTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string PlatformEmail = "owner@pos.local";
        private const string PlatformPassword = "Platform#2026x";
        private readonly WebApplicationFactory<Program> _factory;

        public PlatformOtpTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private sealed class FakeSender : IOtpSender
        {
            public bool Ready { get; set; } = true;
            public bool Fail { get; set; }
            public List<string> Sent { get; } = new();
            public Task<bool> SendAsync(string text, CancellationToken ct = default)
            {
                if (Fail) return Task.FromResult(false);
                Sent.Add(text);
                return Task.FromResult(true);
            }
            public string LastCode => Regex.Match(Sent.Last(), @"\b(\d{6})\b").Groups[1].Value;
        }

        private WebApplicationFactory<Program> Build(FakeSender sender)
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
                    services.RemoveAll(typeof(IOtpSender));
                    services.AddSingleton<IOtpSender>(sender);
                });
            });
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<string> Challenge(HttpClient client)
        {
            var login = await client.PostAsync("/api/auth/login", Json(new { email = PlatformEmail, password = PlatformPassword }));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetProperty("twoStep").GetBoolean());
            return doc.RootElement.GetProperty("challenge").GetString()!;
        }

        [Fact]
        public async Task PlatformOwner_NeedsTheTelegramCode()
        {
            var sender = new FakeSender();
            using var f = Build(sender);
            var client = f.CreateClient();

            var challenge = await Challenge(client);
            Assert.Single(sender.Sent);
            Assert.Contains(PlatformEmail, sender.Sent[0]);
            Assert.StartsWith("AllInOne POS platform sign-in code: ", sender.Sent[0]);
            Assert.DoesNotContain("RestaurantPOS", sender.Sent[0]);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/tenants")).StatusCode);

            var wrong = await client.PostAsync("/api/auth/login/code", Json(new { challenge, code = "000000" == sender.LastCode ? "111111" : "000000" }));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            var right = await client.PostAsync("/api/auth/login/code", Json(new { challenge, code = sender.LastCode }));
            Assert.Equal(HttpStatusCode.OK, right.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/tenants")).StatusCode);

            // A code works only once.
            var again = await f.CreateClient().PostAsync("/api/auth/login/code", Json(new { challenge, code = sender.LastCode }));
            Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        }

        [Fact]
        public async Task FiveWrongCodes_EndTheChallenge()
        {
            var sender = new FakeSender();
            using var f = Build(sender);
            var client = f.CreateClient();
            var challenge = await Challenge(client);
            var code = sender.LastCode;
            var wrong = code == "123456" ? "654321" : "123456";
            for (var i = 0; i < PlatformOtp.MaxTries; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/login/code", Json(new { challenge, code = wrong }))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/login/code", Json(new { challenge, code }))).StatusCode);
        }

        [Fact]
        public async Task WhenTheCodeCannotBeSent_SignInWaits()
        {
            var sender = new FakeSender { Fail = true };
            using var f = Build(sender);
            var login = await f.CreateClient().PostAsync("/api/auth/login", Json(new { email = PlatformEmail, password = PlatformPassword }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        }

        [Fact]
        public async Task WithoutABot_PasswordAloneWorks_AndAWrongPasswordSendsNoCode()
        {
            var sender = new FakeSender { Ready = false };
            using var f = Build(sender);
            var client = f.CreateClient();
            var login = await client.PostAsync("/api/auth/login", Json(new { email = PlatformEmail, password = PlatformPassword }));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/tenants")).StatusCode);

            var ready = new FakeSender();
            using var g = Build(ready);
            var bad = await g.CreateClient().PostAsync("/api/auth/login", Json(new { email = PlatformEmail, password = "Wrong#2026x" }));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
            Assert.Empty(ready.Sent);
        }
    }
}
