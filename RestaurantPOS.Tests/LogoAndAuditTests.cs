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
    public class LogoAndAuditTests : IClassFixture<WebApplicationFactory<Program>>
    {
        public LogoAndAuditTests(WebApplicationFactory<Program> factory)
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

        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };

        private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string contentType)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new MultipartFormDataContent { { file, "file", fileName } };
        }

        [Fact]
        public async Task Logo_UploadShowAndRemove()
        {
            using var f = Build();
            var admin = As(f, "admin");

            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "cashier").GetAsync("/api/store/logo")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/admin/store/logo", Upload(Png, "logo.png", "image/png"))).StatusCode);

            var shown = await As(f, "cashier").GetAsync("/api/store/logo");
            Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
            Assert.Equal("image/png", shown.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Png, await shown.Content.ReadAsByteArrayAsync());

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/admin/store/logo")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "cashier").GetAsync("/api/store/logo")).StatusCode);
        }

        [Fact]
        public async Task Logo_RejectsNonImages_BigFiles_AndNonOwners()
        {
            using var f = Build();
            var admin = As(f, "admin");

            var text = await admin.PostAsync("/api/admin/store/logo", Upload(Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "logo.png", "image/png"));
            var big = new byte[600 * 1024];
            Png.CopyTo(big, 0);
            var tooBig = await admin.PostAsync("/api/admin/store/logo", Upload(big, "big.png", "image/png"));
            var cashier = await As(f, "cashier").PostAsync("/api/admin/store/logo", Upload(Png, "logo.png", "image/png"));

            Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
            Assert.Contains("\"logo\"", await text.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, cashier.StatusCode);
        }

        [Fact]
        public async Task Audit_RecordsPriceChange_ForOwnerOfThatRestaurantOnly()
        {
            using var f = Build();
            var id = await AddMenuItem(f, "Audit Samosa");
            var admin = As(f, "admin");
            var item = await Read(await admin.GetAsync("/api/admin/menu/items"));
            int categoryId = 0;
            foreach (var i in item.EnumerateArray()) if (i.GetProperty("id").GetInt32() == id) categoryId = i.GetProperty("categoryId").GetInt32();

            var changed = await admin.PutAsync($"/api/admin/menu/items/{id}", Json(new
            {
                name = "Audit Samosa", price = 18m, categoryId, description = (string?)null, isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1
            }));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

            var audit = await (await admin.GetAsync("/api/admin/audit")).Content.ReadAsStringAsync();
            Assert.Contains("MenuItem", audit);
            Assert.Contains("Price", audit);
            Assert.Contains("Changed", audit);
            Assert.DoesNotContain("PasswordHash", audit);

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/admin/audit")).StatusCode);
            var otherRestaurant = await (await As(f, "admin", "2").GetAsync("/api/admin/audit")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("Audit Samosa", otherRestaurant);
        }

        [Fact]
        public async Task Audit_RecordsCancellation()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Dabeli");
            var placed = await Read(await As(f, "cashier").PostAsync("/api/orders", Json(new { table = 1, items = new[] { new { menuItemId = item, quantity = 1 } } })));
            await As(f, "cashier").PostAsync($"/api/orders/{placed.GetProperty("orderId").GetInt32()}/cancel", Json(new { reason = "Customer left", version = 1 }));

            var audit = await (await As(f, "admin").GetAsync("/api/admin/audit")).Content.ReadAsStringAsync();
            Assert.Contains("Cancelled", audit);
            Assert.Contains("Customer left", audit);
        }
    }
}
