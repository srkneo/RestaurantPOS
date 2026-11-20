using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class DiagnosticsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DiagnosticsTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

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

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        [Fact]
        public async Task Version_IsPublic_AndHasNoSecrets()
        {
            var f = Build();
            var res = await f.CreateClient().GetAsync("/health/version");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var text = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("version").GetString()));
            Assert.True(doc.RootElement.GetProperty("uptimeSeconds").GetInt64() >= 0);
            Assert.DoesNotContain("ConnectionString", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task OnlyPlatformSeesDiagnostics()
        {
            var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "admin", "1").GetAsync("/api/platform/diagnostics")).StatusCode);

            var res = await As(f, AuthSetup.PlatformRole, "0").GetAsync("/api/platform/diagnostics");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            Assert.True(root.GetProperty("databaseOk").GetBoolean());
            Assert.False(root.GetProperty("sentryOn").GetBoolean());
            Assert.False(root.GetProperty("telemetryOn").GetBoolean());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("recentEvents").ValueKind);
        }
    }
}
