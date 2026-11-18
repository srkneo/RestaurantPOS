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
    // The owner chooses whether a cashier needs a manager PIN to print a bill again.
    public class ApprovalRulesTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApprovalRulesTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage res) =>
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            return client;
        }

        [Fact]
        public async Task ReprintNeedsAPinByDefault_TheOwnerCanDropIt()
        {
            var f = Build();
            var owner = f.CreateClient();
            var cashier = As(f, "cashier");
            Assert.True((await Read(await owner.GetAsync("/api/admin/approval-rules"))).GetProperty("reprintNeedsPin").GetBoolean());
            Assert.True((await Read(await cashier.GetAsync("/api/print-settings"))).GetProperty("reprintNeedsPin").GetBoolean());

            var saved = await owner.PutAsync("/api/admin/approval-rules", Json(new { reprintNeedsPin = false }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.False((await Read(saved)).GetProperty("reprintNeedsPin").GetBoolean());
            Assert.False((await Read(await cashier.GetAsync("/api/print-settings"))).GetProperty("reprintNeedsPin").GetBoolean());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/approval-rules", Json(new { reprintNeedsPin = true }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/admin/approval-rules")).StatusCode);
        }
    }
}
