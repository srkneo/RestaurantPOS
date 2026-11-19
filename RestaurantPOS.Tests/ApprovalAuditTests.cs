using System;
using System.Linq;
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
    // Approvals by manager PIN are written to the audit log as JSON (the Details column is jsonb on Postgres;
    // plain text there made the reprint and discount approvals fail with an error on the real database).
    public class ApprovalAuditTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApprovalAuditTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        [Fact]
        public async Task PinApproval_IsStoredAsValidJson()
        {
            var f = Build();
            var staff = await As(f, "admin").PostAsync("/api/staff",
                Json(new { email = "audit.manager@test.local", displayName = "Meena", role = "manager", password = "Staff#2026x", isDevice = false }));
            Assert.Equal(HttpStatusCode.Created, staff.StatusCode);
            var id = JsonDocument.Parse(await staff.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            Assert.True((await As(f, "admin").PutAsync($"/api/staff/{id}/pin", Json(new { pin = "4321" }))).IsSuccessStatusCode);

            var approved = await As(f, "cashier").PostAsync("/api/approvals", Json(new { pin = "4321", action = "reprint", billId = 7 }));
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = db.AuditLogs.IgnoreQueryFilters().Where(a => a.Action == "Approve").OrderByDescending(a => a.Id).First();
            using var details = JsonDocument.Parse(log.Details!);
            Assert.Equal("reprint approved by Meena", details.RootElement.GetProperty("approval").GetString());
        }
    }
}
