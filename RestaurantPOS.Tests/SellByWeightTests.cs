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
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Sell by weight or volume (dairy, sweets, farsan): the price is per kg or litre and the server works out each line from the menu.
    public class SellByWeightTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SellByWeightTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> F, int PaneerId, int ChaasId, int PuffId);

        private Setup Build()
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
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outlet = db.Outlets.Single(o => o.Id == 1);
            outlet.Settings = outlet.GetSettings().WithPreset("dairy").ToJson();
            var paneer = new MenuItem { Name = "Paneer", Price = 400m, Category = "Dairy", TaxRate = 5m, SellUnit = "kg" };
            var chaas = new MenuItem { Name = "Chaas", Price = 60m, Category = "Dairy", TaxRate = 5m, SellUnit = "litre" };
            var puff = new MenuItem { Name = "Puff", Price = 20m, Category = "Bakery", TaxRate = 5m };
            db.MenuItems.AddRange(paneer, chaas, puff);
            db.SaveChanges();
            return new Setup(f, paneer.Id, chaas.Id, puff.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage res)
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static Task<HttpResponseMessage> Order(HttpClient client, params object[] items) =>
            client.PostAsync("/api/orders", Json(new { orderType = "Takeaway", items, clientOrderId = Guid.NewGuid() }));

        [Fact]
        public async Task Weighed_items_are_priced_from_the_menu_and_named_with_the_amount()
        {
            var s = Build();
            using var f = s.F;
            var cashier = As(f, "cashier");

            var placed = await Order(cashier,
                new { menuItemId = s.PaneerId, quantity = 1, measure = 0.25m },
                new { menuItemId = s.ChaasId, quantity = 2, measure = 1.5m },
                new { menuItemId = s.PuffId, quantity = 3 });
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();

            var order = await Read(await cashier.GetAsync($"/api/orders/{orderId}"));
            var items = order.GetProperty("items").EnumerateArray().ToList();
            var paneer = items.Single(i => i.GetProperty("menuItemId").GetInt32() == s.PaneerId);
            Assert.Equal("Paneer (250 g)", paneer.GetProperty("itemName").GetString());
            Assert.Equal(100m, paneer.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(0.25m, paneer.GetProperty("measure").GetDecimal());
            var chaas = items.Single(i => i.GetProperty("menuItemId").GetInt32() == s.ChaasId);
            Assert.Equal("Chaas (1.5 L)", chaas.GetProperty("itemName").GetString());
            Assert.Equal(90m, chaas.GetProperty("unitPrice").GetDecimal());

            var bill = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            Assert.Equal(340m, (await Read(bill)).GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task Small_amounts_are_rounded_to_paise_and_shown_in_grams_or_ml()
        {
            var s = Build();
            using var f = s.F;
            var cashier = As(f, "cashier");
            var placed = await Order(cashier, new { menuItemId = s.PaneerId, quantity = 1, measure = 0.125m }, new { menuItemId = s.ChaasId, quantity = 1, measure = 0.2m });
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();
            var items = (await Read(await cashier.GetAsync($"/api/orders/{orderId}"))).GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(items, i => i.GetProperty("itemName").GetString() == "Paneer (125 g)" && i.GetProperty("unitPrice").GetDecimal() == 50m);
            Assert.Contains(items, i => i.GetProperty("itemName").GetString() == "Chaas (200 ml)" && i.GetProperty("unitPrice").GetDecimal() == 12m);
        }

        [Fact]
        public async Task A_weighed_item_needs_an_amount_and_a_piece_item_refuses_one()
        {
            var s = Build();
            using var f = s.F;
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(cashier, new { menuItemId = s.PaneerId, quantity = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(cashier, new { menuItemId = s.PaneerId, quantity = 1, measure = 0m })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(cashier, new { menuItemId = s.PaneerId, quantity = 1, measure = 101m })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(cashier, new { menuItemId = s.PuffId, quantity = 1, measure = 0.5m })).StatusCode);
        }

        [Fact]
        public async Task The_owner_sets_how_an_item_is_sold_and_the_pos_menu_shows_it()
        {
            var s = Build();
            using var f = s.F;
            var admin = As(f, "admin");
            var category = await Read(await admin.PostAsync("/api/admin/menu/categories", Json(new { name = "Milk", sortOrder = 1 })));
            var created = await admin.PostAsync("/api/admin/menu/items", Json(new
            {
                name = "Dahi", price = 80m, categoryId = category.GetProperty("id").GetInt32(), description = (string?)null,
                isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1, sellUnit = "kg"
            }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("kg", (await Read(created)).GetProperty("sellUnit").GetString());

            var pos = await Read(await As(f, "cashier").GetAsync("/api/menuitems"));
            Assert.Contains(pos.EnumerateArray(), i => i.GetProperty("name").GetString() == "Dahi" && i.GetProperty("sellUnit").GetString() == "kg");
            Assert.Contains(pos.EnumerateArray(), i => i.GetProperty("name").GetString() == "Puff" && i.GetProperty("sellUnit").GetString() == "piece");
        }
    }
}
