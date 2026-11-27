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
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // v2.4 RN1: the product is called AllInOne POS unless the platform owner saved another name.
    public class AppNameTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public AppNameTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
        public void Default_ProductName_IsAllInOnePos()
        {
            Assert.Equal("AllInOne POS", SupportService.Default.ProductName);
            Assert.Equal("Vyom Solutions", SupportService.Default.CompanyName);
        }

        [Fact]
        public async Task About_WithoutSavedDetails_ShowsAllInOnePos()
        {
            var f = Build();
            var res = await As(f, "cashier").GetAsync("/api/about");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var about = await Read(res);
            Assert.Equal("AllInOne POS", about.GetProperty("productName").GetString());
            Assert.DoesNotContain("RestaurantPOS", about.GetRawText());
        }

        [Fact]
        public async Task About_SavedProductName_StillWins()
        {
            var f = Build();
            var saved = await As(f, "platform").PutAsync("/api/platform/support", Json(new { companyName = "Vyom Solutions", productName = "My Brand POS", phone = "9429549007" }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var about = await Read(await As(f, "cashier").GetAsync("/api/about"));
            Assert.Equal("My Brand POS", about.GetProperty("productName").GetString());
        }

        [Fact]
        public async Task RestaurantOwnerAndCashier_CannotRenameTheProduct()
        {
            var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "admin").PutAsync("/api/platform/support", Json(new { companyName = "Vyom Solutions", productName = "Other POS" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync("/api/platform/support", Json(new { companyName = "Vyom Solutions", productName = "Other POS" }))).StatusCode);
            var about = await Read(await As(f, "cashier").GetAsync("/api/about"));
            Assert.Equal("AllInOne POS", about.GetProperty("productName").GetString());
        }
    }
}
