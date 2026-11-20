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
    // The owner picks the dish text size for the POS; every POS screen reads it from the store settings.
    public class MenuTextSizeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MenuTextSizeTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static StringContent With(JsonElement settings, string? size)
        {
            var map = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object?>>(settings.GetRawText())!;
            if (size == null) map.Remove("menuTextSize"); else map["menuTextSize"] = size;
            return new StringContent(JsonSerializer.Serialize(map), Encoding.UTF8, "application/json");
        }

        [Fact]
        public async Task MenuTextSize_IsSavedAndChecked()
        {
            using var f = Build();
            var admin = As(f, "admin");
            var current = await Read(await admin.GetAsync("/api/admin/settings"));
            Assert.Equal("normal", current.GetProperty("menuTextSize").GetString());

            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/settings", With(current, "small"))).StatusCode);
            Assert.Equal("small", (await Read(await As(f, "cashier").GetAsync("/api/store/settings"))).GetProperty("menuTextSize").GetString());

            // An older screen that does not send it keeps the saved size.
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/settings", With(current, null))).StatusCode);
            Assert.Equal("small", (await Read(await admin.GetAsync("/api/admin/settings"))).GetProperty("menuTextSize").GetString());

            var bad = await admin.PutAsync("/api/admin/settings", With(current, "huge"));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("menutextsize", (await bad.Content.ReadAsStringAsync()).ToLowerInvariant());
        }
    }
}
