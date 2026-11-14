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
    // Owner and manager manage tables; tables not in use leave the POS but stay for old orders.
    public class TableTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TableTests(WebApplicationFactory<Program> factory)
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

        private static async Task<int> CreateTable(WebApplicationFactory<Program> f, string number)
        {
            var response = await As(f, "admin").PostAsync("/api/admin/tables", Json(new { tableNumber = number, isActive = true, sortOrder = 20 }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var table = await Read(response);
            Assert.Equal(number, table.GetProperty("tableNumber").GetString());
            Assert.True(table.GetProperty("isActive").GetBoolean());
            return table.GetProperty("id").GetInt32();
        }

        [Fact]
        public async Task Cashier_CannotManageTables_ManagerCan()
        {
            using var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/admin/tables")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "manager").GetAsync("/api/admin/tables")).StatusCode);
        }

        [Fact]
        public async Task AddTable_ShowsOnPos_AndNumbersMustBeUniqueAndPresent()
        {
            using var f = Build();
            await CreateTable(f, "Garden 1");

            var pos = await (await As(f, "cashier").GetAsync("/api/tables")).Content.ReadAsStringAsync();
            Assert.Contains("\"tableNumber\":\"Garden 1\"", pos);

            var duplicate = await As(f, "admin").PostAsync("/api/admin/tables", Json(new { tableNumber = "Garden 1", isActive = true, sortOrder = 0 }));
            var empty = await As(f, "admin").PostAsync("/api/admin/tables", Json(new { tableNumber = "  ", isActive = true, sortOrder = 0 }));
            var tooLong = await As(f, "admin").PostAsync("/api/admin/tables", Json(new { tableNumber = new string('9', 21), isActive = true, sortOrder = 0 }));
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            Assert.Contains("tablenumber", await duplicate.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        }

        [Fact]
        public async Task TableNotInUse_LeavesPos_AndCannotTakeOrders()
        {
            using var f = Build();
            var id = await CreateTable(f, "Terrace");
            var admin = As(f, "admin");

            var update = await admin.PutAsync($"/api/admin/tables/{id}", Json(new { tableNumber = "Terrace", isActive = false, sortOrder = 20 }));
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            Assert.False((await Read(update)).GetProperty("isActive").GetBoolean());

            var pos = await (await As(f, "cashier").GetAsync("/api/tables")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("Terrace", pos);
            var list = await (await admin.GetAsync("/api/admin/tables")).Content.ReadAsStringAsync();
            Assert.Contains("Terrace", list);

            var category = await Read(await admin.PostAsync("/api/admin/menu/categories", Json(new { name = "Snacks", sortOrder = 1 })));
            var item = await Read(await admin.PostAsync("/api/admin/menu/items", Json(new
            {
                name = "Samosa", price = 15m, categoryId = category.GetProperty("id").GetInt32(), description = (string?)null,
                isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1
            })));
            var order = await As(f, "cashier").PostAsync("/api/orders", Json(new
            {
                orderType = "DineIn", table = id, items = new[] { new { menuItemId = item.GetProperty("id").GetInt32(), quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
        }

        [Fact]
        public async Task RenameToAnExistingNumber_IsRefused_OtherRestaurantGetsNotFound()
        {
            using var f = Build();
            var a = await CreateTable(f, "A1");
            await CreateTable(f, "A2");

            var clash = await As(f, "admin").PutAsync($"/api/admin/tables/{a}", Json(new { tableNumber = "A2", isActive = true, sortOrder = 0 }));
            var keepOwnName = await As(f, "admin").PutAsync($"/api/admin/tables/{a}", Json(new { tableNumber = "A1", isActive = true, sortOrder = 5 }));
            var otherTenant = await As(f, "admin", "2").PutAsync($"/api/admin/tables/{a}", Json(new { tableNumber = "X", isActive = true, sortOrder = 0 }));

            Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
            Assert.Equal(HttpStatusCode.OK, keepOwnName.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
        }
    }
}
