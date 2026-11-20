using System;
using System.Globalization;
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
using POSSystem.Menu;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Time-based menu: a category served only at certain hours is hidden on the POS and refused by the server outside them.
    public class MenuHoursTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MenuHoursTests(WebApplicationFactory<Program> factory) => _factory = factory;

        [Fact]
        public void Hours_WorkAcrossMidnight_AndEmptyMeansAllDay()
        {
            var t = (string s) => TimeOnly.Parse(s, CultureInfo.InvariantCulture);
            Assert.True(MenuHours.IsOpen(t("07:00"), t("11:00"), t("07:00")));
            Assert.False(MenuHours.IsOpen(t("07:00"), t("11:00"), t("11:00")));
            Assert.False(MenuHours.IsOpen(t("07:00"), t("11:00"), t("06:59")));
            Assert.True(MenuHours.IsOpen(t("22:00"), t("02:00"), t("23:30")));
            Assert.True(MenuHours.IsOpen(t("22:00"), t("02:00"), t("01:00")));
            Assert.False(MenuHours.IsOpen(t("22:00"), t("02:00"), t("12:00")));
            Assert.True(MenuHours.IsOpen(null, null, t("03:00")));
        }

        private WebApplicationFactory<Program> Build(out int tableId)
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
            var table = new RestaurantTable { TableNumber = "H1", QRCodeString = "t1-h1", SortOrder = 80 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            tableId = table.Id;
            return f;
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

        // India time now plus some hours, as "HH:mm".
        private static string IndiaIn(double hours) =>
            TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessClock.FindZone("Asia/Kolkata")))
                .AddMinutes(hours * 60).ToString("HH:mm", CultureInfo.InvariantCulture);

        [Fact]
        public async Task CategoryOutsideItsHours_IsHidden_AndCannotBeOrdered()
        {
            using var f = Build(out var tableId);
            var admin = As(f, "admin");
            // Worked out once: calling IndiaIn again a moment later can land in the next minute.
            var from = IndiaIn(2);
            var created = await admin.PostAsync("/api/admin/menu/categories",
                Json(new { name = "Breakfast", sortOrder = 1, isActive = true, availableFrom = from, availableTo = IndiaIn(3) }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var category = await Read(created);
            var categoryId = category.GetProperty("id").GetInt32();
            Assert.Equal(from, category.GetProperty("availableFrom").GetString());

            var item = await admin.PostAsync("/api/admin/menu/items", Json(new
            {
                name = "Poha", price = 30m, categoryId, description = (string?)null, isVeg = true, isAvailable = true, taxRate = 5m, sortOrder = 1
            }));
            Assert.Equal(HttpStatusCode.Created, item.StatusCode);
            var pohaId = (await Read(item)).GetProperty("id").GetInt32();
            var cashier = As(f, "cashier");

            Assert.DoesNotContain("Poha", await cashier.GetStringAsync("/api/menuitems"));
            var refused = await cashier.PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = pohaId, quantity = 1 } }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("is served only", await refused.Content.ReadAsStringAsync());

            // Serving now: the item is back and can be ordered.
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/admin/menu/categories/{categoryId}",
                Json(new { name = "Breakfast", sortOrder = 1, isActive = true, availableFrom = IndiaIn(-1), availableTo = IndiaIn(1) }))).StatusCode);
            Assert.Contains("Poha", await cashier.GetStringAsync("/api/menuitems"));
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/orders", Json(new
            {
                table = tableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = pohaId, quantity = 1 } }
            }))).StatusCode);
        }

        [Fact]
        public async Task Hours_AreChecked()
        {
            using var f = Build(out _);
            var admin = As(f, "admin");
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/menu/categories",
                Json(new { name = "Lunch", sortOrder = 2, isActive = true, availableFrom = "25:00", availableTo = "15:00" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/menu/categories",
                Json(new { name = "Lunch", sortOrder = 2, isActive = true, availableFrom = "12:00" }))).StatusCode);
            var allDay = await Read(await admin.PostAsync("/api/admin/menu/categories", Json(new { name = "Lunch", sortOrder = 2, isActive = true })));
            Assert.Equal(JsonValueKind.Null, allDay.GetProperty("availableFrom").ValueKind);
        }
    }
}
