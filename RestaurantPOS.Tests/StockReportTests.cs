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
    // Stock report: what was bought (and from whom), used and wasted, and what the stock is worth now.
    public class StockReportTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public StockReportTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
        public async Task Report_ShowsPurchasesUseWasteValueAndSuppliers()
        {
            using var f = Build();
            var manager = As(f, "manager");
            var milk = (await Read(await manager.PostAsync("/api/stock", Json(new { name = "Milk", unit = "l", quantity = 10m })))).GetProperty("id").GetInt32();
            var bought = await manager.PostAsync($"/api/stock/{milk}/moves", Json(new { kind = "purchase", quantity = 20m, cost = 1100m, supplier = "Amul Dairy" }));
            Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
            await manager.PostAsync($"/api/stock/{milk}/moves", Json(new { kind = "used", quantity = 5m }));
            await manager.PostAsync($"/api/stock/{milk}/moves", Json(new { kind = "waste", quantity = 2m, supplier = "ignored" }));

            var moves = await Read(await manager.GetAsync($"/api/stock/{milk}/moves"));
            Assert.Equal("Amul Dairy", moves.EnumerateArray().Single(m => m.GetProperty("kind").GetString() == "Purchase").GetProperty("supplier").GetString());
            Assert.Equal(JsonValueKind.Null, moves.EnumerateArray().Single(m => m.GetProperty("kind").GetString() == "Waste").GetProperty("supplier").ValueKind);

            var report = await Read(await manager.GetAsync("/api/stock/report"));
            var line = report.GetProperty("items")[0];
            Assert.Equal(20m, line.GetProperty("bought").GetDecimal());
            Assert.Equal(1100m, line.GetProperty("boughtCost").GetDecimal());
            Assert.Equal(5m, line.GetProperty("used").GetDecimal());
            Assert.Equal(2m, line.GetProperty("wasted").GetDecimal());
            Assert.Equal(23m, line.GetProperty("quantity").GetDecimal());
            Assert.Equal(1265m, line.GetProperty("value").GetDecimal());
            Assert.Equal(1100m, report.GetProperty("purchaseCost").GetDecimal());
            Assert.Equal(110m, report.GetProperty("wasteValue").GetDecimal());
            Assert.Equal(1265m, report.GetProperty("stockValue").GetDecimal());
            var supplier = report.GetProperty("suppliers")[0];
            Assert.Equal("Amul Dairy", supplier.GetProperty("supplier").GetString());
            Assert.Equal(1, supplier.GetProperty("purchases").GetInt32());
        }

        [Fact]
        public async Task Report_IsForManagers_AndChecksTheDays()
        {
            using var f = Build();
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/stock/report")).StatusCode);
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/stock/report?from=2026-11-10&to=2026-11-01")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/stock/report?from=2026-01-01&to=2026-11-01")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/stock/report?from=yesterday")).StatusCode);
            var empty = await Read(await manager.GetAsync("/api/stock/report?from=2026-11-01&to=2026-11-07"));
            Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
            Assert.Equal("2026-11-07", empty.GetProperty("to").GetString());
        }
    }
}
