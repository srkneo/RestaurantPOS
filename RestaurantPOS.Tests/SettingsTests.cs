using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    public class SettingsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        public SettingsTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private readonly WebApplicationFactory<Program> _factory;

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

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task<int> AddMenuItem(WebApplicationFactory<Program> f, string name)
        {
            var admin = As(f, "admin");
            var category = await Read(await admin.PostAsync("/api/admin/menu/categories", Json(new { name = "Snacks " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1 })));
            var created = await admin.PostAsync("/api/admin/menu/items", Json(new
            {
                name, price = 15m, categoryId = category.GetProperty("id").GetInt32(), description = (string?)null,
                isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1
            }));
            return (await Read(created)).GetProperty("id").GetInt32();
        }

        private static object Settings(bool dineIn = true, bool takeaway = true, string defaultType = "DineIn", string zone = "Asia/Kolkata", int hour = 4) => new
        {
            timeZone = zone, businessDayStartHour = hour, dineInEnabled = dineIn, takeawayEnabled = takeaway, defaultOrderType = defaultType,
            takeawayNeedsName = true, maxItemQuantity = 50, firstToken = 1, kitchenScreenEnabled = true
        };

        [Fact]
        public async Task Defaults_AndAnyStaffCanReadThem()
        {
            using var f = Build();
            var s = await Read(await As(f, "admin").GetAsync("/api/admin/settings"));
            Assert.Equal("Asia/Kolkata", s.GetProperty("timeZone").GetString());
            Assert.Equal(4, s.GetProperty("businessDayStartHour").GetInt32());
            Assert.True(s.GetProperty("dineInEnabled").GetBoolean());
            Assert.True(s.GetProperty("takeawayEnabled").GetBoolean());
            Assert.Equal("DineIn", s.GetProperty("defaultOrderType").GetString());
            Assert.Equal(99, s.GetProperty("maxItemQuantity").GetInt32());

            var pos = await As(f, "cashier").GetAsync("/api/store/settings");
            Assert.Equal(HttpStatusCode.OK, pos.StatusCode);
        }

        [Fact]
        public async Task Save_ChecksTheRules()
        {
            using var f = Build();
            var admin = As(f, "admin");

            var none = await admin.PutAsync("/api/admin/settings", Json(Settings(dineIn: false, takeaway: false)));
            var zone = await admin.PutAsync("/api/admin/settings", Json(Settings(zone: "Mars/Base")));
            var hour = await admin.PutAsync("/api/admin/settings", Json(Settings(hour: 24)));
            var defaultOff = await admin.PutAsync("/api/admin/settings", Json(Settings(dineIn: false, defaultType: "DineIn")));
            var ok = await admin.PutAsync("/api/admin/settings", Json(Settings()));

            Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
            Assert.Contains("ordertypes", await none.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, zone.StatusCode);
            Assert.Contains("timezone", await zone.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, hour.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, defaultOff.StatusCode);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(50, (await Read(ok)).GetProperty("maxItemQuantity").GetInt32());
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/settings", Json(Settings()))).StatusCode);
        }

        [Fact]
        public async Task TakeawayCounterPreset_TurnsOffDineIn_AndOrdersFollowIt()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Vada Pav");

            var preset = await As(f, "admin").PostAsync("/api/admin/settings/preset", Json(new { preset = "takeaway-counter" }));
            Assert.Equal(HttpStatusCode.OK, preset.StatusCode);
            var s = await Read(preset);
            Assert.False(s.GetProperty("dineInEnabled").GetBoolean());
            Assert.Equal("Takeaway", s.GetProperty("defaultOrderType").GetString());
            Assert.False(s.GetProperty("takeawayNeedsName").GetBoolean());

            var cashier = As(f, "cashier");
            var takeaway = await cashier.PostAsync("/api/orders", Json(new { items = new[] { new { menuItemId = item, quantity = 1 } } }));
            var dineIn = await cashier.PostAsync("/api/orders", Json(new { orderType = "DineIn", table = 1, items = new[] { new { menuItemId = item, quantity = 1 } } }));
            Assert.Equal(HttpStatusCode.OK, takeaway.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, dineIn.StatusCode);
            Assert.Contains("turned off", await dineIn.Content.ReadAsStringAsync());

            var unknown = await As(f, "admin").PostAsync("/api/admin/settings/preset", Json(new { preset = "night-club" }));
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        }
    }
}
