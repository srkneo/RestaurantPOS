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
    // Day close, day report and expenses: shift rules, roles, other restaurants and bad input.
    public class DayCloseNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DayCloseNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        [Fact]
        public void EveryRestaurantTable_HasARestaurantFilter()
        {
            using var f = Build();
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
#pragma warning disable CS0618
            var missing = db.Model.GetEntityTypes()
                .Where(t => typeof(TenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType == null && t.GetQueryFilter() == null)
                .Select(t => t.ClrType.Name).OrderBy(n => n).ToList();
#pragma warning restore CS0618
            Assert.True(missing.Count == 0, "No restaurant filter on: " + string.Join(", ", missing));
        }

        [Fact]
        public async Task Shift_OpenTwice_CloseTwice_AndMovementsWithoutAShift_AreRefused()
        {
            using var f = Build();
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "Out", amount = 50m, reason = "Milk" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 0m, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = -1m }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 1000000.01m }))).StatusCode);

            var opened = await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 2000m }));
            Assert.True(opened.IsSuccessStatusCode, $"open gave {opened.StatusCode}");
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 100m }))).StatusCode);

            foreach (var body in new object[]
            {
                new { kind = "Sideways", amount = 50m, reason = "Milk" }, new { kind = "1", amount = 50m, reason = "Milk" },
                new { kind = "Out", amount = 0m, reason = "Milk" }, new { kind = "Out", amount = -5m, reason = "Milk" },
                new { kind = "In", amount = 1000000.01m, reason = "Milk" }, new { kind = "Out", amount = 50m, reason = "  " },
                new { kind = "Out", amount = 50m, reason = new string('m', 101) },
            })
            {
                var res = await cashier.PostAsync("/api/shifts/current/movements", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }

            var current = await Read(await cashier.GetAsync("/api/shifts/current"));
            var version = current.GetProperty("shift").GetProperty("version").GetInt32();
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 2000m, version = version + 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = -1m, version }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 2000m, note = new string('n', 201), version }))).StatusCode);
            var closed = await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 2000m, version }));
            Assert.True(closed.IsSuccessStatusCode, $"close gave {closed.StatusCode}");
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 2000m, version = version + 1 }))).StatusCode);
        }

        [Fact]
        public async Task Roles_WaiterAndKitchenHaveNoTill_CashierHasNoDayReport()
        {
            using var f = Build();
            foreach (var role in new[] { "waiter", "kitchen" })
            {
                var c = As(f, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/shifts/current")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/shifts/open", Json(new { openingCash = 100m }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/expenses")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/expenses", Json(new { category = "Milk", amount = 10m, paidFrom = "Other" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/reports/day")).StatusCode);
            }
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/reports/day")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/shifts")).StatusCode);
        }

        [Fact]
        public async Task OtherRestaurant_DoesNotSeeTheShiftOrExpenses()
        {
            using var f = Build();
            var cashier = As(f, "cashier");
            Assert.True((await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 2000m }))).IsSuccessStatusCode);
            Assert.True((await cashier.PostAsync("/api/expenses", Json(new { category = "Milk", amount = 120m, paidFrom = "Till", note = "Amul" }))).IsSuccessStatusCode);

            var other = As(f, "manager", "2");
            var shift = await other.GetAsync("/api/shifts/current");
            var text = await shift.Content.ReadAsStringAsync();
            Assert.DoesNotContain("2000", text);
            Assert.DoesNotContain("Amul", await (await other.GetAsync("/api/expenses")).Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsync("/api/shifts/current/movements", Json(new { kind = "Out", amount = 50m, reason = "Steal" }))).StatusCode);
        }

        [Fact]
        public async Task Expenses_CategoryAnyCase_BadPaidFrom_Limits_AndDates()
        {
            using var f = Build();
            var cashier = As(f, "cashier");
            Assert.True((await cashier.PostAsync("/api/expenses", Json(new { category = "milk", amount = 10m, paidFrom = "other" }))).IsSuccessStatusCode);
            foreach (var body in new object[]
            {
                new { category = "Petrol", amount = 10m, paidFrom = "Other" }, new { category = "", amount = 10m, paidFrom = "Other" },
                new { category = "Milk", amount = 10m, paidFrom = "1" }, new { category = "Milk", amount = 10m, paidFrom = "Bank" },
                new { category = "Milk", amount = 0m, paidFrom = "Other" }, new { category = "Milk", amount = 1000000.01m, paidFrom = "Other" },
                new { category = "Milk", amount = 10m, paidFrom = "Till" },
            })
            {
                var res = await cashier.PostAsync("/api/expenses", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("/api/expenses?date=2026/11/20")).StatusCode);
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/reports/day?date=today")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/reports/day?date=2020-01-01")).StatusCode);
            var list = await Read(await cashier.GetAsync("/api/expenses"));
            Assert.Equal(1, list.GetProperty("expenses").GetArrayLength());
            Assert.Equal("Milk", list.GetProperty("expenses")[0].GetProperty("category").GetString());
        }
    }
}
