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
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // v2.4 RN5: messages users see say "business", not "restaurant". Field keys (restaurantname) and API paths stay.
    public class BusinessWordsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public BusinessWordsTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static object NewBusiness(string name, string email) => new
        {
            restaurantName = name, outletName = "Main", ownerName = "Ravi Patel", ownerEmail = email, ownerPassword = "Owner#2026xy"
        };

        private static object Profile(string name) => new
        {
            restaurantName = name, outletName = "Anand Station Road", address = "Station Road, Anand 388001", phone = "+91 98250 12345",
            gstin = (string?)null, fssai = (string?)null, receiptFooter = "Thank you, visit again"
        };

        [Fact]
        public async Task Store_WithoutName_AsksForTheBusinessName()
        {
            using var f = Build();
            var admin = As(f, "admin", "1");
            foreach (var name in new[] { " ", new string('A', 201) })
            {
                var res = await admin.PutAsync("/api/admin/store", Json(Profile(name)));
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
                var body = await res.Content.ReadAsStringAsync();
                Assert.Contains("Enter the business name (up to 200 characters).", body);
                Assert.DoesNotContain("restaurant name", body, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager", "1").PutAsync("/api/admin/store", Json(Profile("Shree Krishna Dining")))).StatusCode);
        }

        [Fact]
        public async Task Platform_AddBusiness_WithoutOrWithTooLongName_SaysBusiness()
        {
            using var f = Build();
            var platform = As(f, "platform", "0");

            var empty = await platform.PostAsync("/api/platform/tenants", Json(NewBusiness("", "empty@biz.local")));
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            var emptyBody = await empty.Content.ReadAsStringAsync();
            Assert.Contains("Enter the business name.", emptyBody);
            Assert.Contains("restaurantname", emptyBody);
            Assert.DoesNotContain("restaurant name", emptyBody, StringComparison.OrdinalIgnoreCase);

            var tooLong = await platform.PostAsync("/api/platform/tenants", Json(NewBusiness(new string('B', 201), "long@biz.local")));
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Contains("The business name can be at most 200 characters.", await tooLong.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "admin", "1").PostAsync("/api/platform/tenants", Json(NewBusiness("Anand Dairy", "admin@biz.local")))).StatusCode);
        }

        [Fact]
        public async Task DemoData_OnABusinessWithData_SaysBusiness()
        {
            using var f = Build();
            var platform = As(f, "platform", "0");
            var created = await platform.PostAsync("/api/platform/tenants", Json(NewBusiness("Demo Bakery", "demo@biz.local")));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = doc.RootElement.GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await platform.PostAsync($"/api/platform/tenants/{id}/demo", Json(new { }))).StatusCode);

            var again = await platform.PostAsync($"/api/platform/tenants/{id}/demo", Json(new { }));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            var body = await again.Content.ReadAsStringAsync();
            Assert.Contains("This business already has a menu or orders. Demo data is only for an empty business.", body);
            Assert.DoesNotContain("restaurant", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SupportAccess_TurnedOff_SaysBusiness()
        {
            using var f = Build();
            int id;
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = new Tenant { Name = "No Support Dairy" };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                db.CurrentTenantId = tenant.Id;
                db.Outlets.Add(new Outlet { TenantId = tenant.Id, Name = "Main", Settings = "{\"allowSupportAccess\":false}" });
                await db.SaveChangesAsync();
                id = tenant.Id;
            }
            var res = await As(f, "platform", "0").PostAsync($"/api/platform/tenants/{id}/support-login", Json(new { }));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            Assert.Contains("This business has turned off support access.", body);
            Assert.DoesNotContain("restaurant", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
