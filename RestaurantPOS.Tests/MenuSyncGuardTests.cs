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
    // v1.7 menu sync: a shop PC whose menu comes from the cloud refuses menu changes, but still lets staff mark items sold out.
    public class MenuSyncGuardTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MenuSyncGuardTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build(bool fromCloud)
        {
            var dbName = Guid.NewGuid().ToString();
            var f = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
            }));
            if (fromCloud)
            {
                using var scope = f.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LicenceRecords.Add(new LicenceRecord { Id = 1, DeviceId = "device-1", LastSeenUtc = DateTime.UtcNow, MenuFromCloud = true, MenuVersion = "v1" });
                db.SaveChanges();
            }
            return f;
        }

        private static HttpClient Admin(WebApplicationFactory<Program> f)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        [Fact]
        public async Task A_cloud_managed_menu_refuses_changes_but_allows_sold_out()
        {
            using var f = Build(fromCloud: true);
            var client = Admin(f);

            var category = await client.PostAsync("/api/admin/menu/categories", Json(new { name = "Local Starters", sortOrder = 1 }));
            Assert.Equal(HttpStatusCode.Conflict, category.StatusCode);
            Assert.Contains("managed in the cloud", await category.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/admin/menu/stations", Json(new { name = "Tandoor", sortOrder = 1 }))).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/menu/items")).StatusCode);
            var soldOut = await client.PutAsync("/api/admin/menu/items/999999/availability", Json(new { isAvailable = false }));
            Assert.NotEqual(HttpStatusCode.Conflict, soldOut.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/menu/sync")).StatusCode);
        }

        [Fact]
        public async Task A_shop_with_its_own_menu_can_change_it()
        {
            using var f = Build(fromCloud: false);
            var category = await Admin(f).PostAsync("/api/admin/menu/categories", Json(new { name = "Local Starters", sortOrder = 1 }));
            Assert.NotEqual(HttpStatusCode.Conflict, category.StatusCode);
            Assert.True(category.IsSuccessStatusCode);
        }
    }
}
