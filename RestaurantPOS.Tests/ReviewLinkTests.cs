using System;
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
    // The owner pastes the Google review link once; bills then print it as a QR code.
    public class ReviewLinkTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ReviewLinkTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage res) =>
            JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            return client;
        }

        [Fact]
        public async Task OwnerSetsTheLink_BillsGetAReviewQr()
        {
            var f = Build();
            var owner = f.CreateClient();

            Assert.Equal(JsonValueKind.Null, (await Read(await owner.GetAsync("/api/admin/review-link"))).GetProperty("reviewUrl").ValueKind);
            Assert.False((await Read(await owner.GetAsync("/api/print-settings"))).GetProperty("reviewReady").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/review-qr.svg")).StatusCode);

            var saved = await owner.PutAsync("/api/admin/review-link", Json(new { reviewUrl = " https://g.page/r/anand-thali/review " }));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal("https://g.page/r/anand-thali/review", (await Read(saved)).GetProperty("reviewUrl").GetString());

            var cashier = As(f, "cashier");
            Assert.True((await Read(await cashier.GetAsync("/api/print-settings"))).GetProperty("reviewReady").GetBoolean());
            var qr = await cashier.GetAsync("/api/review-qr.svg");
            Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
            Assert.Equal("image/svg+xml", qr.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());

            // Empty clears it.
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync("/api/admin/review-link", Json(new { reviewUrl = "" }))).StatusCode);
            Assert.False((await Read(await owner.GetAsync("/api/print-settings"))).GetProperty("reviewReady").GetBoolean());
        }

        [Fact]
        public async Task OnlyFullHttpsLinks_AndOnlyTheOwner()
        {
            var f = Build();
            var owner = f.CreateClient();
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync("/api/admin/review-link", Json(new { reviewUrl = "g.page/r/abc" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync("/api/admin/review-link", Json(new { reviewUrl = "http://g.page/r/abc" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync("/api/admin/review-link", Json(new { reviewUrl = "https://g.page/" + new string('a', 300) }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/review-link", Json(new { reviewUrl = "https://g.page/r/abc" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/admin/review-link")).StatusCode);
        }
    }
}
