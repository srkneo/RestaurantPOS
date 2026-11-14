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
    // Batch B rules: order types, sold-out items, cancel and change before cooking, version checks, owner menu page.
    public class OrderAndMenuTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public OrderAndMenuTests(WebApplicationFactory<Program> factory)
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

        private static async Task<int> AddMenuItem(WebApplicationFactory<Program> f, string name, bool available = true)
        {
            var admin = As(f, "admin");
            var category = await Read(await admin.PostAsync("/api/admin/menu/categories", Json(new { name = "Snacks " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1 })));
            var created = await admin.PostAsync("/api/admin/menu/items", Json(new
            {
                name, price = 15m, categoryId = category.GetProperty("id").GetInt32(), description = (string?)null,
                isVeg = true, isAvailable = available, taxRate = 5m, sortOrder = 1
            }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await Read(created)).GetProperty("id").GetInt32();
        }

        private static async Task<JsonElement> PlaceDineIn(WebApplicationFactory<Program> f, int menuItemId, int quantity = 1)
        {
            var response = await As(f, "cashier").PostAsync("/api/orders", Json(new { orderType = "DineIn", table = 1, items = new[] { new { menuItemId, quantity, note = "Jain" } } }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await Read(response);
        }

        [Fact]
        public async Task Takeaway_NeedsCustomerName_DineInNeedsTable()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Samosa");
            var cashier = As(f, "cashier");

            var noName = await cashier.PostAsync("/api/orders", Json(new { orderType = "Takeaway", items = new[] { new { menuItemId = item, quantity = 1 } } }));
            var noTable = await cashier.PostAsync("/api/orders", Json(new { orderType = "DineIn", items = new[] { new { menuItemId = item, quantity = 1 } } }));
            var takeaway = await cashier.PostAsync("/api/orders", Json(new { orderType = "Takeaway", customerName = "Ravi", customerPhone = "98250 12345", items = new[] { new { menuItemId = item, quantity = 2 } } }));

            Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, noTable.StatusCode);
            Assert.Equal(HttpStatusCode.OK, takeaway.StatusCode);

            var orderId = (await Read(takeaway)).GetProperty("orderId").GetInt32();
            var order = await Read(await cashier.GetAsync($"/api/orders/{orderId}"));
            Assert.Equal("Takeaway", order.GetProperty("orderType").GetString());
            Assert.Equal("Ravi", order.GetProperty("customerName").GetString());
            Assert.Equal(JsonValueKind.Null, order.GetProperty("tableId").ValueKind);
        }

        [Fact]
        public async Task SoldOutItem_AndTooManyItems_AreRefused()
        {
            using var f = Build();
            var soldOut = await AddMenuItem(f, "Kulfi", available: false);
            var item = await AddMenuItem(f, "Chai");
            var cashier = As(f, "cashier");

            var sold = await cashier.PostAsync("/api/orders", Json(new { table = 1, items = new[] { new { menuItemId = soldOut, quantity = 1 } } }));
            var tooMany = await cashier.PostAsync("/api/orders", Json(new { table = 1, items = new[] { new { menuItemId = item, quantity = 100 } } }));

            Assert.Equal(HttpStatusCode.BadRequest, sold.StatusCode);
            Assert.Contains("sold out", await sold.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        }

        [Fact]
        public async Task Tokens_CountUpWithinTheDay_AndNotesAreKept()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Dhokla");

            var first = await PlaceDineIn(f, item);
            var second = await PlaceDineIn(f, item);

            Assert.Equal(1, first.GetProperty("tokenNumber").GetInt32());
            Assert.Equal(2, second.GetProperty("tokenNumber").GetInt32());
            var order = await Read(await As(f, "cashier").GetAsync($"/api/orders/{first.GetProperty("orderId").GetInt32()}"));
            Assert.Equal("Jain", order.GetProperty("items")[0].GetProperty("note").GetString());
        }

        [Fact]
        public async Task Cancel_WorksBeforeCooking_AndIsRefusedAfter()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Pav Bhaji");
            var early = await PlaceDineIn(f, item);
            var late = await PlaceDineIn(f, item);
            var cashier = As(f, "cashier");

            var cancelled = await cashier.PostAsync($"/api/orders/{early.GetProperty("orderId").GetInt32()}/cancel", Json(new { reason = "Customer left", version = 1 }));
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal("Cancelled", (await Read(cancelled)).GetProperty("status").GetString());

            var lateId = late.GetProperty("orderId").GetInt32();
            var cooking = await As(f, "kitchen").PutAsync($"/api/orders/{lateId}/status", Json(new { status = "Preparing", version = 1 }));
            Assert.Equal(HttpStatusCode.OK, cooking.StatusCode);
            var refused = await cashier.PostAsync($"/api/orders/{lateId}/cancel", Json(new { reason = "Customer left", version = 2 }));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("cooking-started", await refused.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Cancel_NeedsAReason_AndKitchenCannotCancel()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Thepla");
            var orderId = (await PlaceDineIn(f, item)).GetProperty("orderId").GetInt32();

            var noReason = await As(f, "cashier").PostAsync($"/api/orders/{orderId}/cancel", Json(new { reason = " ", version = 1 }));
            var kitchen = await As(f, "kitchen").PostAsync($"/api/orders/{orderId}/cancel", Json(new { reason = "Out of stock", version = 1 }));

            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, kitchen.StatusCode);
        }

        [Fact]
        public async Task ChangeItems_ChecksVersion_AndReplacesLines()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Khaman");
            var orderId = (await PlaceDineIn(f, item)).GetProperty("orderId").GetInt32();
            var cashier = As(f, "cashier");

            var stale = await cashier.PostAsync($"/api/orders/{orderId}/items", Json(new { version = 0, items = new[] { new { menuItemId = item, quantity = 3 } } }));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var changed = await cashier.PostAsync($"/api/orders/{orderId}/items", Json(new { version = 1, items = new[] { new { menuItemId = item, quantity = 3 } } }));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var order = await Read(changed);
            Assert.Equal(2, order.GetProperty("version").GetInt32());
            Assert.Equal(1, order.GetProperty("items").GetArrayLength());
            Assert.Equal(3, order.GetProperty("items")[0].GetProperty("quantity").GetInt32());

            var again = await cashier.PostAsync($"/api/orders/{orderId}/items", Json(new { version = 1, items = new[] { new { menuItemId = item, quantity = 1 } } }));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        [Fact]
        public async Task MenuAdmin_SoldOutAndDeletedItems_LeaveThePos()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Fafda");
            var admin = As(f, "admin");
            Assert.Contains("Fafda", await (await As(f, "cashier").GetAsync("/api/menuitems")).Content.ReadAsStringAsync());

            await admin.PutAsync($"/api/admin/menu/items/{item}/availability", Json(new { isAvailable = false }));
            Assert.DoesNotContain("Fafda", await (await As(f, "cashier").GetAsync("/api/menuitems")).Content.ReadAsStringAsync());
            Assert.Contains("Fafda", await (await admin.GetAsync("/api/admin/menu/items")).Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/menu/items/{item}")).StatusCode);
            Assert.DoesNotContain("Fafda", await (await admin.GetAsync("/api/admin/menu/items")).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task MenuAdmin_RulesAndRoles()
        {
            using var f = Build();
            var item = await AddMenuItem(f, "Jalebi");
            var admin = As(f, "admin");

            var badTax = await admin.PostAsync("/api/admin/menu/items", Json(new { name = "Lassi", price = 40m, categoryId = 999, description = (string?)null, isVeg = true, isAvailable = true, taxRate = 7m, sortOrder = 1 }));
            Assert.Equal(HttpStatusCode.BadRequest, badTax.StatusCode);
            var body = await badTax.Content.ReadAsStringAsync();
            Assert.Contains("taxrate", body);
            Assert.Contains("categoryid", body);

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/admin/menu/items")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "manager").GetAsync("/api/admin/menu/items")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "admin", "2").PutAsync($"/api/admin/menu/items/{item}/availability", Json(new { isAvailable = false }))).StatusCode);
        }

        [Fact]
        public async Task Orders_BadDate_Returns400()
        {
            using var f = Build();
            var response = await As(f, "cashier").GetAsync("/api/orders?date=14-11-2026");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
