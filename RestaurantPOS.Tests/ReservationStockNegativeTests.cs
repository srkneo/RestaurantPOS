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
using POSSystem.Models;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Reservations and stock pages, server side: wrong steps, unknown ids, other restaurants, roles and limits.
    public class ReservationStockNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ReservationStockNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Table, int Thali, TimeZoneInfo Zone) Build()
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
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "7", QRCodeString = "t1-7", SortOrder = 7 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            var zone = BusinessClock.FindZone(db.Outlets.Single(o => o.Id == 1).TimeZone);
            return (f, table.Id, thali.Id, zone);
        }

        private static (string Date, string Time) LocalIn(TimeZoneInfo zone, int minutes)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMinutes(minutes), zone);
            return (local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture));
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

        private static async Task<int> Book(HttpClient c, TimeZoneInfo zone, int minutes, int? tableId = null, string name = "Joshi")
        {
            var at = LocalIn(zone, minutes);
            var res = await c.PostAsync("/api/reservations", Json(new { customerName = name, people = 4, date = at.Date, time = at.Time, tableId }));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            return (await Read(res)).GetProperty("id").GetInt32();
        }

        private static Task<HttpResponseMessage> Status(HttpClient c, int id, string status) =>
            c.PostAsync($"/api/reservations/{id}/status", Json(new { status }));

        [Fact]
        public async Task Reservation_StatusSteps_AreChecked()
        {
            var (f, _, _, zone) = Build();
            using var factory = f;
            var waiter = As(f, "waiter");
            var id = await Book(waiter, zone, 180);
            Assert.Equal(HttpStatusCode.BadRequest, (await Status(waiter, id, "Booked")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Status(waiter, id, "2")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Status(waiter, id, "Eaten")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Status(waiter, id, "")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Status(waiter, id, "seated")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Status(waiter, id, "Cancelled")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Status(waiter, id, "NoShow")).StatusCode);

            var at = LocalIn(zone, 200);
            Assert.Equal(HttpStatusCode.BadRequest, (await waiter.PutAsync($"/api/reservations/{id}", Json(new { customerName = "Joshi", people = 5, date = at.Date, time = at.Time }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Status(waiter, id, "Booked")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await waiter.PutAsync($"/api/reservations/{id}", Json(new { customerName = "Joshi", people = 5, date = at.Date, time = at.Time }))).StatusCode);
        }

        [Fact]
        public async Task Reservation_BadInput_UnknownIds_OtherRestaurant_AndKitchen()
        {
            var (f, table, _, zone) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var at = LocalIn(zone, 180);
            foreach (var body in new object[]
            {
                new { customerName = new string('n', 61), people = 2, date = at.Date, time = at.Time },
                new { customerName = "Big", people = 101, date = at.Date, time = at.Time },
                new { customerName = "Note", people = 2, date = at.Date, time = at.Time, note = new string('x', 201) },
                new { customerName = "Date", people = 2, date = "21-11-2026", time = at.Time },
                new { customerName = "Time", people = 2, date = at.Date, time = "8pm" },
                new { customerName = "Far", people = 2, date = DateTime.UtcNow.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), time = "20:00" },
            })
            {
                var res = await cashier.PostAsync("/api/reservations", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("/api/reservations?date=tomorrow")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/reservations/upcoming?minutes=-50")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/reservations/upcoming?minutes=999999")).StatusCode);

            var id = await Book(cashier, zone, 180, table);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PutAsync("/api/reservations/99999", Json(new { customerName = "X", people = 2, date = at.Date, time = at.Time }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Status(cashier, 99999, "Seated")).StatusCode);

            var other = As(f, "manager", "2");
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/reservations/{id}", Json(new { customerName = "Hacked", people = 2, date = at.Date, time = at.Time }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Status(other, id, "Cancelled")).StatusCode);
            Assert.DoesNotContain("Joshi", await (await other.GetAsync("/api/reservations/upcoming?minutes=600")).Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync("/api/reservations", Json(new { customerName = "K", people = 2, date = at.Date, time = at.Time }))).StatusCode);
            var mine = await Read(await cashier.GetAsync($"/api/reservations?date={at.Date}"));
            Assert.Equal("Booked", mine.EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id).GetProperty("status").GetString());
        }

        private static async Task<JsonElement> AddStock(HttpClient c, string name, string unit = "kg", decimal quantity = 10m)
        {
            var res = await c.PostAsync("/api/stock", Json(new { name, unit, lowLevel = 2m, quantity }));
            Assert.True(res.IsSuccessStatusCode, $"add stock gave {res.StatusCode}");
            return await Read(res);
        }

        [Fact]
        public async Task Stock_BadItems_AndBadMoves_AreRefused()
        {
            var (f, _, _, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            foreach (var body in new object[]
            {
                new { name = "", unit = "kg", lowLevel = 1m, quantity = 1m },
                new { name = new string('a', 61), unit = "kg", lowLevel = 1m, quantity = 1m },
                new { name = "Rice", unit = "sack", lowLevel = 1m, quantity = 1m },
                new { name = "Rice", unit = "kg", lowLevel = -1m, quantity = 1m },
                new { name = "Rice", unit = "kg", lowLevel = 1m, quantity = -1m },
                new { name = "Rice", unit = "kg", lowLevel = 1m, quantity = 1000000.5m },
            })
            {
                var res = await manager.PostAsync("/api/stock", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            var rice = await AddStock(manager, "Rice");
            var id = rice.GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/stock", Json(new { name = " rice ", unit = "kg", lowLevel = 1m, quantity = 1m }))).StatusCode);

            foreach (var body in new object[]
            {
                new { kind = "Stolen", quantity = 1m }, new { kind = "1", quantity = 1m }, new { kind = "Purchase", quantity = 0m },
                new { kind = "Used", quantity = -1m }, new { kind = "Count", quantity = -1m }, new { kind = "Used", quantity = 10.5m },
                new { kind = "Purchase", quantity = 1000001m }, new { kind = "Purchase", quantity = 1m, cost = -1m },
                new { kind = "Waste", quantity = 1m, note = new string('n', 101) },
                new { kind = "Purchase", quantity = 1m, supplier = new string('s', 61) },
            })
            {
                var res = await manager.PostAsync($"/api/stock/{id}/moves", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(10m, (await Read(await manager.GetAsync("/api/stock"))).EnumerateArray().Single().GetProperty("quantity").GetDecimal());
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsync("/api/stock/99999/moves", Json(new { kind = "Purchase", quantity = 1m }))).StatusCode);
            foreach (var take in new[] { "0", "-5", "100000" })
                Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/stock/{id}/moves?take={take}")).StatusCode);
        }

        [Fact]
        public async Task Stock_RemovedItem_TakesNoMoves_AndItsNameCanBeUsedAgain()
        {
            var (f, _, _, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            var id = (await AddStock(manager, "Oil", "l")).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/stock/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"/api/stock/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "Purchase", quantity = 1m }))).StatusCode);
            Assert.Equal(0, (await Read(await manager.GetAsync("/api/stock"))).GetArrayLength());
            await AddStock(manager, "Oil", "l");
        }

        [Fact]
        public async Task StockReport_Dates_AreChecked()
        {
            var (f, _, _, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/stock/report?from=2026-11-20&to=2026-11-01")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/stock/report?from=2026-01-01&to=2026-12-31")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/stock/report?from=yesterday")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/stock/report?from=2026-11-01&to=2026-11-30")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/stock/report")).StatusCode);
        }

        [Fact]
        public async Task Stock_Roles_AndOtherRestaurant()
        {
            var (f, _, _, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            var id = (await AddStock(manager, "Paneer")).GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "waiter").GetAsync("/api/stock")).StatusCode);
            var kitchen = As(f, "kitchen");
            Assert.Equal(HttpStatusCode.OK, (await kitchen.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "Used", quantity = 1m }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.PostAsync("/api/stock", Json(new { name = "Ghee", unit = "kg", lowLevel = 1m, quantity = 1m }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.DeleteAsync($"/api/stock/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/stock/report")).StatusCode);

            var other = As(f, "manager", "2");
            Assert.DoesNotContain("Paneer", await (await other.GetAsync("/api/stock")).Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "Waste", quantity = 5m }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/stock/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/stock/{id}/moves")).StatusCode);
            Assert.Equal(9m, (await Read(await manager.GetAsync("/api/stock"))).EnumerateArray().Single().GetProperty("quantity").GetDecimal());
        }

        [Fact]
        public async Task Recipe_BadLines_AndOtherRestaurant_AreRefused()
        {
            var (f, _, thali, _) = Build();
            using var factory = f;
            var manager = As(f, "manager");
            var rice = (await AddStock(manager, "Rice")).GetProperty("id").GetInt32();
            foreach (var lines in new object[]
            {
                new[] { new { stockItemId = rice, quantity = 0m } },
                new[] { new { stockItemId = rice, quantity = 1001m } },
                new[] { new { stockItemId = rice, quantity = 0.2m }, new { stockItemId = rice, quantity = 0.1m } },
                new[] { new { stockItemId = 99999, quantity = 0.2m } },
                Enumerable.Range(0, 31).Select(i => new { stockItemId = rice + i, quantity = 0.1m }).ToArray(),
            })
            {
                var res = await manager.PutAsync($"/api/menu/{thali}/recipe", Json(new { lines }));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(lines)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsync("/api/menu/99999/recipe", Json(new { lines = new[] { new { stockItemId = rice, quantity = 0.2m } } }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "manager", "2").PutAsync($"/api/menu/{thali}/recipe", Json(new { lines = new[] { new { stockItemId = rice, quantity = 0.2m } } }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PutAsync($"/api/menu/{thali}/recipe", Json(new { lines = new[] { new { stockItemId = rice, quantity = 0.2m } } }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/menu/{thali}/recipe", Json(new { lines = new[] { new { stockItemId = rice, quantity = 0.2m } } }))).StatusCode);
        }
    }
}
