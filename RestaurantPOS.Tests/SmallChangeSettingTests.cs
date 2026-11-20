using System;
using System.Net;
using System.Net.Http;
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
    // The owner sets how much small change a cashier may waive; the bill screen reads it from the print settings.
    public class SmallChangeSettingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SmallChangeSettingTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
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

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        [Fact]
        public async Task SmallChangeLimit_IsSavedAndChecked()
        {
            using var f = Build();
            var admin = As(f, "admin");
            Assert.Equal(5m, (await Read(await admin.GetAsync("/api/admin/billing-settings"))).GetProperty("smallChangeLimit").GetDecimal());

            var saved = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", paperWidth = 80, printLanguage = "en", smallChangeLimit = 10m }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(10m, (await Read(saved)).GetProperty("smallChangeLimit").GetDecimal());
            Assert.Equal(10m, (await Read(await As(f, "cashier").GetAsync("/api/print-settings"))).GetProperty("smallChangeLimit").GetDecimal());

            // An older screen that does not send it keeps the saved value.
            await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", paperWidth = 80, printLanguage = "en" }));
            Assert.Equal(10m, (await Read(await admin.GetAsync("/api/admin/billing-settings"))).GetProperty("smallChangeLimit").GetDecimal());

            var bad = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", paperWidth = 80, printLanguage = "en", smallChangeLimit = 50m }));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("smallchangelimit", (await bad.Content.ReadAsStringAsync()).ToLowerInvariant());
        }
    }
}
