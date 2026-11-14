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
    public class StoreTests : IClassFixture<WebApplicationFactory<Program>>
    {
        public StoreTests(WebApplicationFactory<Program> factory)
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

        private static object Profile(string? gstin = "24aaacc1206d1zm", string? fssai = "12345678901234", string restaurant = "Shree Krishna Dining") => new
        {
            restaurantName = restaurant, outletName = "Anand Station Road", address = "Station Road, Anand 388001", phone = "+91 98250 12345",
            gstin, fssai, receiptFooter = "Thank you, visit again"
        };

        [Fact]
        public async Task Owner_SeesAndSavesProfile_GstinStoredInCapitals()
        {
            using var f = Build();
            var admin = As(f, "admin");

            var before = await Read(await admin.GetAsync("/api/admin/store"));
            Assert.Equal("My Restaurant", before.GetProperty("restaurantName").GetString());
            Assert.Equal("Main", before.GetProperty("outletName").GetString());

            var saved = await admin.PutAsync("/api/admin/store", Json(Profile()));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var profile = await Read(saved);
            Assert.Equal("24AAACC1206D1ZM", profile.GetProperty("gstin").GetString());
            Assert.Equal("Shree Krishna Dining", profile.GetProperty("restaurantName").GetString());

            var summary = await Read(await As(f, "cashier").GetAsync("/api/store"));
            Assert.Equal("Shree Krishna Dining", summary.GetProperty("restaurantName").GetString());
            Assert.Equal("Anand Station Road", summary.GetProperty("outletName").GetString());
            Assert.False(summary.GetProperty("hasLogo").GetBoolean());
        }

        [Fact]
        public async Task WrongGstinFssaiOrEmptyName_AreRefused()
        {
            using var f = Build();
            var admin = As(f, "admin");

            var gstin = await admin.PutAsync("/api/admin/store", Json(Profile(gstin: "24AAACC1206D1Z")));
            var fssai = await admin.PutAsync("/api/admin/store", Json(Profile(fssai: "123")));
            var name = await admin.PutAsync("/api/admin/store", Json(Profile(restaurant: " ")));
            var optional = await admin.PutAsync("/api/admin/store", Json(Profile(gstin: null, fssai: null)));

            Assert.Equal(HttpStatusCode.BadRequest, gstin.StatusCode);
            Assert.Contains("gstin", await gstin.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, fssai.StatusCode);
            Assert.Contains("fssai", await fssai.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, name.StatusCode);
            Assert.Equal(HttpStatusCode.OK, optional.StatusCode);
        }

        [Fact]
        public async Task OnlyOwnerChangesProfile_UnknownOutletIsNotFound()
        {
            using var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/store", Json(Profile()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/admin/store")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "admin", "2").GetAsync("/api/admin/store")).StatusCode);
        }
    }
}
