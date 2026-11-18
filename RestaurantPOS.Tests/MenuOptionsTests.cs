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
    // Sizes (variants) and extras (add-ons): set by the owner, shown on the POS, priced by the server on every order.
    public class MenuOptionsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MenuOptionsTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int PizzaId, int TeaId);

        // No-GST restaurant with a table, Pizza 250 and Tea 20.
        private Setup Build()
        {
            var dbName = Guid.NewGuid().ToString();
            var f = _factory.WithWebHostBuilder(builder =>
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
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outlet = db.Outlets.Single(o => o.Id == 1);
            var settings = outlet.GetSettings();
            settings.TaxMode = TaxMode.None;
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Mains", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "7", QRCodeString = "t1-7", SortOrder = 7 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var pizza = new MenuItem { Name = "Pizza", Price = 250m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.AddRange(pizza, tea);
            db.SaveChanges();
            return new Setup(f, table.Id, pizza.Id, tea.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static readonly object PizzaOptions = new
        {
            variants = new object[] { new { name = "Regular", price = 250m }, new { name = "Large", price = 350m } },
            addOns = new object[] { new { name = "Extra cheese", price = 30m }, new { name = "Olives", price = 20m } }
        };

        // Saves the pizza's sizes and extras and returns (regular, large, cheese, olives) ids.
        private static async Task<(int Regular, int Large, int Cheese, int Olives)> SavePizzaOptions(Setup s)
        {
            var res = await As(s.Factory, "admin").PutAsync($"/api/admin/menu/items/{s.PizzaId}/options", Json(PizzaOptions));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var o = await Read(res);
            return (o.GetProperty("variants")[0].GetProperty("id").GetInt32(), o.GetProperty("variants")[1].GetProperty("id").GetInt32(),
                o.GetProperty("addOns")[0].GetProperty("id").GetInt32(), o.GetProperty("addOns")[1].GetProperty("id").GetInt32());
        }

        private static Task<HttpResponseMessage> Order(Setup s, object line) =>
            As(s.Factory, "cashier").PostAsync("/api/orders", Json(new { table = s.TableId, items = new[] { line }, clientOrderId = Guid.NewGuid() }));

        [Fact]
        public async Task OwnerSetsSizesAndExtras_AndThePosMenuShowsThem()
        {
            var s = Build();
            using var f = s.Factory;
            await SavePizzaOptions(s);

            var menu = await Read(await As(f, "cashier").GetAsync("/api/menuitems"));
            var pizza = menu.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Pizza");
            Assert.Equal(new[] { "Regular", "Large" }, pizza.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("name").GetString()));
            Assert.Equal(350m, pizza.GetProperty("variants")[1].GetProperty("price").GetDecimal());
            Assert.Equal(2, pizza.GetProperty("addOns").GetArrayLength());
            var tea = menu.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Tea");
            Assert.Equal(0, tea.GetProperty("variants").GetArrayLength());
        }

        [Fact]
        public async Task Order_WithASizeAndExtras_IsPricedByTheServer_AndBilledWithTheirNames()
        {
            var s = Build();
            using var f = s.Factory;
            var o = await SavePizzaOptions(s);

            // The till's own price is ignored.
            var placed = await Order(s, new { menuItemId = s.PizzaId, quantity = 2, variantId = o.Large, addOnIds = new[] { o.Olives, o.Cheese }, unitPrice = 1m });
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
            var orderId = (await Read(placed)).GetProperty("orderId").GetInt32();

            var bill = await Read(await As(f, "cashier").PostAsync($"/api/orders/{orderId}/bill", Json(new { })));
            var line = bill.GetProperty("lines")[0];
            Assert.Equal("Pizza (Large) + Extra cheese, Olives", line.GetProperty("itemName").GetString());
            Assert.Equal(400m, line.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(800m, bill.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task Order_IsRefused_WithoutASize_WithAnotherItemsExtra_OrASoldOutSize()
        {
            var s = Build();
            using var f = s.Factory;
            var o = await SavePizzaOptions(s);

            Assert.Equal(HttpStatusCode.BadRequest, (await Order(s, new { menuItemId = s.PizzaId, quantity = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(s, new { menuItemId = s.TeaId, quantity = 1, addOnIds = new[] { o.Cheese } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(s, new { menuItemId = s.TeaId, quantity = 1, variantId = o.Large })).StatusCode);

            var soldOut = new
            {
                variants = new object[] { new { id = o.Regular, name = "Regular", price = 250m }, new { id = o.Large, name = "Large", price = 350m, isAvailable = false } },
                addOns = Array.Empty<object>()
            };
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/admin/menu/items/{s.PizzaId}/options", Json(soldOut))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(s, new { menuItemId = s.PizzaId, quantity = 1, variantId = o.Large })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Order(s, new { menuItemId = s.PizzaId, quantity = 1, variantId = o.Regular })).StatusCode);

            // A plain item orders as before.
            Assert.Equal(HttpStatusCode.OK, (await Order(s, new { menuItemId = s.TeaId, quantity = 3 })).StatusCode);
        }

        [Fact]
        public async Task Saving_ChecksTheForm_AndOnlyOwnerOrManagerMaySave()
        {
            var s = Build();
            using var f = s.Factory;
            var bad = new
            {
                variants = new object[] { new { name = "Half", price = 120m }, new { name = "half", price = 200m } },
                addOns = new object[] { new { name = " ", price = 10m }, new { name = "Cheese", price = -5m } }
            };
            var res = await As(f, "admin").PutAsync($"/api/admin/menu/items/{s.PizzaId}/options", Json(bad));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var errors = (await Read(res)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("variants", out _));
            Assert.True(errors.TryGetProperty("addOns", out _));

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync($"/api/admin/menu/items/{s.PizzaId}/options", Json(PizzaOptions))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "admin").PutAsync("/api/admin/menu/items/9999/options", Json(PizzaOptions))).StatusCode);
        }

        [Fact]
        public async Task RemovingASize_KeepsPastBillsUnchanged()
        {
            var s = Build();
            using var f = s.Factory;
            var o = await SavePizzaOptions(s);
            var placed = await Read(await Order(s, new { menuItemId = s.PizzaId, quantity = 1, variantId = o.Large }));

            var onlyRegular = new { variants = new object[] { new { id = o.Regular, name = "Regular", price = 250m } }, addOns = Array.Empty<object>() };
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/admin/menu/items/{s.PizzaId}/options", Json(onlyRegular))).StatusCode);

            var bill = await Read(await As(f, "cashier").PostAsync($"/api/orders/{placed.GetProperty("orderId").GetInt32()}/bill", Json(new { })));
            Assert.Equal("Pizza (Large)", bill.GetProperty("lines")[0].GetProperty("itemName").GetString());
            Assert.Equal(350m, bill.GetProperty("total").GetDecimal());
        }
    }
}
