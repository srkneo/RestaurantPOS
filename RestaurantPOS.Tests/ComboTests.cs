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
    // Combos: one bill line at the combo price, every part listed for the kitchen.
    public class ComboTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ComboTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int MealId, int BurgerId, int FriesId, int ColaId);

        // No-GST restaurant: Burger 100, Fries 50, Cola 40 and Burger Meal 160.
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
            var category = new MenuCategory { Name = "Combos", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "C1", QRCodeString = "t1-c1", SortOrder = 90 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            MenuItem Item(string name, decimal price) => new() { Name = name, Price = price, Category = "Combos", CategoryId = category.Id, TaxRate = 5m };
            var meal = Item("Burger Meal", 160m);
            var burger = Item("Burger", 100m);
            var fries = Item("Fries", 50m);
            var cola = Item("Cola", 40m);
            db.MenuItems.AddRange(meal, burger, fries, cola);
            db.SaveChanges();
            return new Setup(f, table.Id, meal.Id, burger.Id, fries.Id, cola.Id);
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

        private static Task<HttpResponseMessage> SaveCombo(WebApplicationFactory<Program> f, int comboId, params (int Item, int Quantity)[] parts) =>
            As(f, "admin").PutAsync($"/api/admin/menu/items/{comboId}/combo",
                Json(new { parts = parts.Select(p => new { partItemId = p.Item, quantity = p.Quantity }).ToArray() }));

        [Fact]
        public async Task Combo_ListsItsParts_ForTheKitchen_AndBillsOneLine()
        {
            var s = Build();
            using var f = s.Factory;
            Assert.Equal(HttpStatusCode.OK, (await SaveCombo(f, s.MealId, (s.BurgerId, 1), (s.FriesId, 1), (s.ColaId, 1))).StatusCode);
            Assert.Equal(3, (await Read(await As(f, "admin").GetAsync($"/api/admin/menu/items/{s.MealId}/combo"))).GetArrayLength());

            var cashier = As(f, "cashier");
            var placed = await Read(await cashier.PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = s.MealId, quantity = 2 } }
            })));
            var orderId = placed.GetProperty("orderId").GetInt32();
            var line = (await Read(await As(f, "kitchen").GetAsync($"/api/orders/{orderId}"))).GetProperty("items")[0];
            Assert.Equal("1 x Burger, 1 x Fries, 1 x Cola", line.GetProperty("includes").GetString());

            var bill = await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { })));
            Assert.Equal(320m, bill.GetProperty("total").GetDecimal());
            Assert.Equal(1, bill.GetProperty("lines").GetArrayLength());
            Assert.Equal("Burger Meal", bill.GetProperty("lines")[0].GetProperty("itemName").GetString());
        }

        [Fact]
        public async Task Combo_PartsShowOnThePosAndQrMenu()
        {
            var s = Build();
            using var f = s.Factory;
            Assert.Equal(HttpStatusCode.OK, (await SaveCombo(f, s.MealId, (s.BurgerId, 1), (s.FriesId, 2))).StatusCode);

            var menu = await Read(await As(f, "cashier").GetAsync("/api/menuitems"));
            var meal = menu.EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == s.MealId);
            Assert.Equal("1 x Burger, 2 x Fries", meal.GetProperty("includes").GetString());
            var cola = menu.EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == s.ColaId);
            Assert.Equal(JsonValueKind.Null, cola.GetProperty("includes").ValueKind);
        }

        [Fact]
        public async Task Combo_IsChecked_AndOnlyForOwnerOrManager()
        {
            var s = Build();
            using var f = s.Factory;
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveCombo(f, s.MealId, (s.MealId, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveCombo(f, s.MealId, (99999, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveCombo(f, s.MealId, (s.FriesId, 0))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveCombo(f, s.MealId, (s.FriesId, 1), (s.FriesId, 2))).StatusCode);

            // No combo inside a combo.
            Assert.Equal(HttpStatusCode.OK, (await SaveCombo(f, s.FriesId, (s.ColaId, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveCombo(f, s.MealId, (s.FriesId, 1))).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync($"/api/admin/menu/items/{s.MealId}/combo",
                Json(new { parts = Array.Empty<object>() }))).StatusCode);
            // An empty list makes it a normal item again.
            Assert.Equal(HttpStatusCode.OK, (await SaveCombo(f, s.FriesId)).StatusCode);
            Assert.Equal(0, (await Read(await As(f, "admin").GetAsync($"/api/admin/menu/items/{s.FriesId}/combo"))).GetArrayLength());
        }
    }
}
