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
    // QR table ordering: secret token per table, guest menu and orders onto the table's running tab, owner switch.
    public class QrOrderingTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public QrOrderingTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int TeaId);

        private Setup Build(bool qrOn)
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
            settings.QrOrderingEnabled = qrOn;
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Drinks", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "Q1", QRCodeString = "t1-q1", SortOrder = 100 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var tea = new MenuItem { Name = "Masala Tea", Price = 20m, Category = "Drinks", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(tea);
            db.SaveChanges();
            return new Setup(f, table.Id, tea.Id);
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

        private static async Task<string> TokenOf(Setup s) =>
            (await Read(await As(s.Factory, "admin").GetAsync("/api/admin/tables/qr"))).EnumerateArray()
                .Single(t => t.GetProperty("id").GetInt32() == s.TableId).GetProperty("token").GetString()!;

        private static Task<HttpResponseMessage> Order(Setup s, string token, int quantity) =>
            s.Factory.CreateClient().PostAsync($"/api/qr/{token}/order",
                Json(new { items = new[] { new { menuItemId = s.TeaId, quantity } }, clientOrderId = Guid.NewGuid() }));

        [Fact]
        public async Task Guest_SeesTheMenu_AndOrdersOntoTheTablesRunningTab()
        {
            var s = Build(qrOn: true);
            using var f = s.Factory;
            var token = await TokenOf(s);
            Assert.True(token.Length >= 20);

            var menu = await Read(await f.CreateClient().GetAsync($"/api/qr/{token}"));
            Assert.Equal("Q1", menu.GetProperty("tableNumber").GetString());
            Assert.True(menu.GetProperty("ordering").GetBoolean());
            Assert.Contains(menu.GetProperty("items").EnumerateArray(), i => i.GetProperty("name").GetString() == "Masala Tea");

            var first = await Order(s, token, 2);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var token1 = (await Read(first)).GetProperty("tokenNumber").GetInt32();
            var second = await Read(await Order(s, token, 1));
            Assert.Equal(token1, second.GetProperty("tokenNumber").GetInt32());
            Assert.Equal(2, second.GetProperty("round").GetInt32());

            var board = (await Read(await As(f, "cashier").GetAsync("/api/tables"))).EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == s.TableId);
            Assert.Equal("Occupied", board.GetProperty("status").GetString());
            var order = await Read(await As(f, "cashier").GetAsync($"/api/orders/{board.GetProperty("orderId").GetInt32()}"));
            Assert.Equal("QR (guest)", order.GetProperty("takenBy").GetString());

            Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().PostAsync($"/api/qr/{token}/call", Json(new { kind = "bill" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await f.CreateClient().PostAsync($"/api/qr/{token}/call", Json(new { kind = "dance" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Order(s, token, 31)).StatusCode);
        }

        [Fact]
        public async Task TableQrImage_IsAnSvgOfTheGuestLink()
        {
            var s = Build(qrOn: true);
            using var f = s.Factory;
            var image = await As(f, "manager").GetAsync($"/api/admin/tables/{s.TableId}/qr.svg?origin=https%3A%2F%2Fpos.example.com");
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/svg+xml", image.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<svg", await image.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "manager").GetAsync($"/api/admin/tables/{s.TableId}/qr.svg?origin=javascript:alert(1)")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync($"/api/admin/tables/{s.TableId}/qr.svg?origin=https%3A%2F%2Fpos.example.com")).StatusCode);
        }

        [Fact]
        public async Task QrOrdering_IsOffUntilTheOwnerTurnsItOn()
        {
            var s = Build(qrOn: false);
            using var f = s.Factory;
            var token = await TokenOf(s);
            Assert.False((await Read(await f.CreateClient().GetAsync($"/api/qr/{token}"))).GetProperty("ordering").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await Order(s, token, 1)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync("/api/admin/tables/qr-setting", Json(new { enabled = true }))).StatusCode);
            Assert.True((await Read(await As(f, "manager").GetAsync("/api/admin/tables/qr-setting"))).GetProperty("enabled").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await Order(s, token, 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync("/api/admin/tables/qr-setting", Json(new { enabled = false }))).StatusCode);
        }

        [Fact]
        public async Task OldOrWrongTokens_FindNothing_AndABilledTableTakesNoMore()
        {
            var s = Build(qrOn: true);
            using var f = s.Factory;
            var token = await TokenOf(s);
            Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClient().GetAsync("/api/qr/not-a-real-token")).StatusCode);

            var placed = await Read(await Order(s, token, 1));
            var orderId = (await Read(await As(f, "cashier").GetAsync("/api/tables"))).EnumerateArray()
                .Single(t => t.GetProperty("id").GetInt32() == s.TableId).GetProperty("orderId").GetInt32();
            Assert.Equal(HttpStatusCode.Created, (await As(f, "cashier").PostAsync($"/api/orders/{orderId}/bill", Json(new { }))).StatusCode);
            var billed = await Order(s, token, 1);
            Assert.Equal(HttpStatusCode.Conflict, billed.StatusCode);
            Assert.Equal("billed", (await Read(billed)).GetProperty("code").GetString());

            var renewed = await Read(await As(f, "admin").PostAsync($"/api/admin/tables/{s.TableId}/qr", Json(new { })));
            Assert.NotEqual(token, renewed.GetProperty("token").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClient().GetAsync($"/api/qr/{token}")).StatusCode);
            Assert.True(placed.GetProperty("tokenNumber").GetInt32() > 0);
        }
    }
}
