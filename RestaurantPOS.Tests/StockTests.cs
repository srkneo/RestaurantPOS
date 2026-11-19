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
    // Stock per outlet: purchases add, used and waste take away, a count sets the real quantity; nothing goes below zero.
    public class StockTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public StockTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
        public async Task Purchase_Use_Waste_Count_KeepTheQuantityAndHistory()
        {
            using var f = Build();
            var manager = As(f, "manager");
            var created = await manager.PostAsync("/api/stock", Json(new { name = "Milk", unit = "L", lowLevel = 5m, quantity = 10m }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var milk = await Read(created);
            var id = milk.GetProperty("id").GetInt32();
            Assert.Equal("l", milk.GetProperty("unit").GetString());
            Assert.Equal(10m, milk.GetProperty("quantity").GetDecimal());

            var cashier = As(f, "cashier");
            var bought = await Read(await cashier.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "purchase", quantity = 20m, cost = 1100m, note = "Amul" })));
            Assert.Equal(30m, bought.GetProperty("quantity").GetDecimal());
            Assert.Equal(55m, bought.GetProperty("costPerUnit").GetDecimal());

            var used = await Read(await As(f, "kitchen").PostAsync($"/api/stock/{id}/moves", Json(new { kind = "used", quantity = 12.5m })));
            Assert.Equal(17.5m, used.GetProperty("quantity").GetDecimal());

            var wasted = await Read(await cashier.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "waste", quantity = 13m, note = "Spoilt" })));
            Assert.Equal(4.5m, wasted.GetProperty("quantity").GetDecimal());
            Assert.True(wasted.GetProperty("isLow").GetBoolean());

            var counted = await Read(await manager.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "count", quantity = 4m })));
            Assert.Equal(4m, counted.GetProperty("quantity").GetDecimal());

            var moves = await Read(await manager.GetAsync($"/api/stock/{id}/moves"));
            Assert.Equal(5, moves.GetArrayLength());
            Assert.Equal("Count", moves[0].GetProperty("kind").GetString());
            Assert.Equal(-0.5m, moves[0].GetProperty("change").GetDecimal());
            Assert.Equal("Opening stock", moves[4].GetProperty("note").GetString());
        }

        [Fact]
        public async Task Stock_NeverGoesBelowZero_AndFormsGetFieldErrors()
        {
            using var f = Build();
            var manager = As(f, "manager");
            var id = (await Read(await manager.PostAsync("/api/stock", Json(new { name = "Paneer", unit = "kg", lowLevel = 1m, quantity = 2m })))).GetProperty("id").GetInt32();

            var tooMuch = await manager.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "used", quantity = 3m }));
            Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
            Assert.Contains("Only 2 kg in stock.", await tooMuch.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "stolen", quantity = 1m }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "purchase", quantity = 0m }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/stock", Json(new { name = "paneer", unit = "kg" }))).StatusCode);
            var badUnit = await manager.PostAsync("/api/stock", Json(new { name = "Oil", unit = "barrel" }));
            Assert.Equal(HttpStatusCode.BadRequest, badUnit.StatusCode);
            Assert.Contains("unit", (await badUnit.Content.ReadAsStringAsync()).ToLowerInvariant());
        }

        [Fact]
        public async Task OnlyManagersChangeTheList_AndRestaurantsNeverSeeEachOthersStock()
        {
            using var f = Build();
            var manager = As(f, "manager");
            var id = (await Read(await manager.PostAsync("/api/stock", Json(new { name = "Sugar", unit = "kg" })))).GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").PostAsync("/api/stock", Json(new { name = "Tea", unit = "kg" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "waiter").GetAsync("/api/stock")).StatusCode);

            var other = As(f, "manager", "2");
            Assert.Equal(0, (await Read(await other.GetAsync("/api/stock"))).GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/stock/{id}/moves", Json(new { kind = "purchase", quantity = 1m }))).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/stock/{id}", Json(new { name = "Sugar (fine)", unit = "kg", lowLevel = 2m }))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/stock/{id}")).StatusCode);
            Assert.Equal(0, (await Read(await manager.GetAsync("/api/stock"))).GetArrayLength());
        }
    }
}
