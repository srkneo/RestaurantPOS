using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
using Xunit;

namespace RestaurantPOS.Tests
{
    public class SupportTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SupportTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["App:Version"] = "9.8.7"
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

        [Fact]
        public async Task About_ShowsTheDefaultsAndTheVersion()
        {
            var f = Build();
            var res = await As(f, "cashier").GetAsync("/api/about");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var about = await Read(res);
            Assert.Equal("Vyom Solutions", about.GetProperty("companyName").GetString());
            Assert.Equal("RestaurantPOS", about.GetProperty("productName").GetString());
            Assert.Equal("9.8.7", about.GetProperty("version").GetString());
            Assert.False(about.GetProperty("shopPc").GetBoolean());
            Assert.Equal(JsonValueKind.Null, about.GetProperty("phone").ValueKind);
        }

        [Fact]
        public async Task PlatformOwner_SavesTheDetails_AndEveryRestaurantSeesThem()
        {
            var f = Build();
            var res = await As(f, "platform").PutAsync("/api/platform/support", Json(new
            {
                companyName = " Vyom Solutions ", productName = "RestaurantPOS", phone = "+91 94295 49007", whatsApp = "9429549007",
                email = "help@vyom.test", hours = "8 AM - 10 AM and 7 PM - 10 PM", website = "  "
            }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var saved = await Read(res);
            Assert.Equal("Vyom Solutions", saved.GetProperty("companyName").GetString());
            Assert.Equal(JsonValueKind.Null, saved.GetProperty("website").ValueKind);

            var about = await Read(await As(f, "cashier").GetAsync("/api/about"));
            Assert.Equal("+91 94295 49007", about.GetProperty("phone").GetString());
            Assert.Equal("help@vyom.test", about.GetProperty("email").GetString());
            Assert.Equal("8 AM - 10 AM and 7 PM - 10 PM", about.GetProperty("hours").GetString());

            var again = await Read(await As(f, "platform").GetAsync("/api/platform/support"));
            Assert.Equal("9429549007", again.GetProperty("whatsApp").GetString());
        }

        [Fact]
        public async Task RestaurantOwner_CannotChangeTheDetails()
        {
            var f = Build();
            var res = await As(f, "admin").PutAsync("/api/platform/support", Json(new { companyName = "Someone else", productName = "POS" }));
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "admin").GetAsync("/api/platform/support")).StatusCode);
        }

        [Fact]
        public async Task Save_ChecksTheForm()
        {
            var f = Build();
            var res = await As(f, "platform").PutAsync("/api/platform/support", Json(new
            {
                companyName = " ", productName = "RestaurantPOS", phone = "call me", email = "not-an-email", website = "ftp://vyom.test"
            }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var errors = (await Read(res)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("companyName", out _));
            Assert.True(errors.TryGetProperty("phone", out _));
            Assert.True(errors.TryGetProperty("email", out _));
            Assert.True(errors.TryGetProperty("website", out _));
            Assert.Equal("Vyom Solutions", (await Read(await As(f, "cashier").GetAsync("/api/about"))).GetProperty("companyName").GetString());
        }
    }
}
