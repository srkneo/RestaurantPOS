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
using Xunit;

namespace RestaurantPOS.Tests
{
    // Expenses at day close: from the till they come out of the shift's expected cash; all show in the reports.
    public class ExpenseTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ExpenseTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private WebApplicationFactory<Program> Build() => TestBusinessDay.AwayFromDayStart(BuildFactory());

        private WebApplicationFactory<Program> BuildFactory()
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

        [Fact]
        public async Task TillExpense_LeavesTheShiftsCash_AndAllExpensesAreReported()
        {
            using var f = Build();
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 1000m }))).StatusCode);

            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync("/api/expenses", Json(new { category = "Milk", amount = 120m, paidFrom = "Till", note = "Amul" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync("/api/expenses", Json(new { category = "rent", amount = 5000m, paidFrom = "Other" }))).StatusCode);

            var shift = (await Read(await cashier.GetAsync("/api/shifts/current"))).GetProperty("shift");
            Assert.Equal(120m, shift.GetProperty("cashOut").GetDecimal());
            Assert.Equal(880m, shift.GetProperty("expectedCash").GetDecimal());

            var list = await Read(await cashier.GetAsync("/api/expenses"));
            Assert.Equal(5120m, list.GetProperty("total").GetDecimal());
            Assert.Equal(120m, list.GetProperty("fromTill").GetDecimal());
            Assert.Equal("Rent", list.GetProperty("expenses")[1].GetProperty("category").GetString());

            var report = await Read(await As(f, "manager").GetAsync("/api/reports/sales"));
            Assert.Equal(5120m, report.GetProperty("expenseTotal").GetDecimal());
            Assert.Equal(2, report.GetProperty("expenses").GetArrayLength());

            var z = await Read(await As(f, "manager").GetAsync("/api/reports/day"));
            Assert.Equal(5120m, z.GetProperty("expenseTotal").GetDecimal());
            Assert.Equal(120m, z.GetProperty("expenseFromTill").GetDecimal());
        }

        [Fact]
        public async Task Expenses_AreChecked()
        {
            using var f = Build();
            var cashier = As(f, "cashier");
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/expenses", Json(new { category = "Party", amount = 10m, paidFrom = "Other" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/expenses", Json(new { category = "Gas", amount = 0m, paidFrom = "Other" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/expenses", Json(new { category = "Gas", amount = 10m, paidFrom = "Pocket" }))).StatusCode);
            // From the till needs an open shift.
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/expenses", Json(new { category = "Gas", amount = 10m, paidFrom = "Till" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "waiter").PostAsync("/api/expenses", Json(new { category = "Gas", amount = 10m, paidFrom = "Other" }))).StatusCode);
        }
    }
}
