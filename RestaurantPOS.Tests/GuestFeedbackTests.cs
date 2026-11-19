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
    // Guests rate their visit from the QR on the bill; one rating per bill; the owner sees them all.
    public class GuestFeedbackTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public GuestFeedbackTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int TeaId);

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
            var drinks = new MenuCategory { Name = "Drinks", SortOrder = 1 };
            db.MenuCategories.Add(drinks);
            var table = new RestaurantTable { TableNumber = "F1", QRCodeString = "t1-f1", SortOrder = 97 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Drinks", CategoryId = drinks.Id, TaxRate = 5m };
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

        private static async Task<JsonElement> Bill(Setup s)
        {
            var cashier = As(s.Factory, "cashier");
            var placed = await Read(await cashier.PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = s.TeaId, quantity = 2 } }
            })));
            var bill = await cashier.PostAsync($"/api/orders/{placed.GetProperty("orderId").GetInt32()}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            return await Read(bill);
        }

        [Fact]
        public async Task GuestRatesOnce_OwnerSeesTheAverage()
        {
            var s = Build();
            using var f = s.Factory;
            var bill = await Bill(s);
            var billId = bill.GetProperty("id").GetInt32();
            var cashier = As(f, "cashier");

            // Off by default: no link, no QR.
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/bills/{billId}/feedback-link")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync("/api/admin/feedback-setting", Json(new { enabled = true }))).StatusCode);
            Assert.True((await Read(await cashier.GetAsync("/api/print-settings"))).GetProperty("feedbackReady").GetBoolean());

            var token = (await Read(await cashier.GetAsync($"/api/bills/{billId}/feedback-link"))).GetProperty("token").GetString()!;
            var qr = await cashier.GetAsync($"/api/bills/{billId}/feedback-qr.svg?origin=https://pos.example.com");
            Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
            Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync($"/api/bills/{billId}/feedback-qr.svg")).StatusCode);

            var guest = f.CreateClient();
            var form = await Read(await guest.GetAsync($"/api/feedback/{token}"));
            Assert.Equal(bill.GetProperty("invoiceNumber").GetString(), form.GetProperty("invoiceNumber").GetString());
            Assert.False(form.GetProperty("alreadyGiven").GetBoolean());

            Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsync($"/api/feedback/{token}", Json(new { rating = 6 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsync($"/api/feedback/{token}", Json(new { rating = 4, comment = new string('x', 501) }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await guest.PostAsync($"/api/feedback/{token}", Json(new { rating = 5, comment = " Great masala tea " }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await guest.PostAsync($"/api/feedback/{token}", Json(new { rating = 1 }))).StatusCode);
            Assert.True((await Read(await guest.GetAsync($"/api/feedback/{token}"))).GetProperty("alreadyGiven").GetBoolean());

            var list = await Read(await As(f, "manager").GetAsync("/api/admin/feedback"));
            Assert.Equal(1, list.GetProperty("count").GetInt32());
            Assert.Equal(5m, list.GetProperty("average").GetDecimal());
            var item = list.GetProperty("items").EnumerateArray().Single();
            Assert.Equal("Great masala tea", item.GetProperty("comment").GetString());
            Assert.Equal(bill.GetProperty("invoiceNumber").GetString(), item.GetProperty("invoiceNumber").GetString());
        }

        [Fact]
        public async Task TheDayReportShowsTheDaysRatings()
        {
            var s = Build();
            using var f = s.Factory;
            var admin = As(f, "admin");
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/feedback-setting", Json(new { enabled = true }))).StatusCode);
            var guest = f.CreateClient();
            foreach (var rating in new[] { 4, 5 })
            {
                var billId = (await Bill(s)).GetProperty("id").GetInt32();
                var token = (await Read(await admin.GetAsync($"/api/bills/{billId}/feedback-link"))).GetProperty("token").GetString()!;
                Assert.Equal(HttpStatusCode.OK, (await guest.PostAsync($"/api/feedback/{token}", Json(new { rating }))).StatusCode);
            }

            var day = await Read(await As(f, "manager").GetAsync("/api/reports/day"));
            Assert.Equal(2, day.GetProperty("feedbackCount").GetInt32());
            Assert.Equal(4.5m, day.GetProperty("feedbackAverage").GetDecimal());
        }

        [Fact]
        public async Task TheSalesReportShowsTheRangesRatings()
        {
            var s = Build();
            using var f = s.Factory;
            var admin = As(f, "admin");
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/feedback-setting", Json(new { enabled = true }))).StatusCode);
            var none = await Read(await As(f, "manager").GetAsync("/api/reports/sales"));
            Assert.Equal(0, none.GetProperty("feedbackCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, none.GetProperty("feedbackAverage").ValueKind);

            var guest = f.CreateClient();
            foreach (var rating in new[] { 3, 4, 4 })
            {
                var billId = (await Bill(s)).GetProperty("id").GetInt32();
                var token = (await Read(await admin.GetAsync($"/api/bills/{billId}/feedback-link"))).GetProperty("token").GetString()!;
                Assert.Equal(HttpStatusCode.OK, (await guest.PostAsync($"/api/feedback/{token}", Json(new { rating }))).StatusCode);
            }

            var report = await Read(await As(f, "manager").GetAsync("/api/reports/sales"));
            Assert.Equal(3, report.GetProperty("feedbackCount").GetInt32());
            Assert.Equal(3.7m, report.GetProperty("feedbackAverage").GetDecimal());
        }

        [Fact]
        public async Task BadTokens_FeedbackOff_AndRoles()
        {
            var s = Build();
            using var f = s.Factory;
            var bill = await Bill(s);
            var billId = bill.GetProperty("id").GetInt32();
            var admin = As(f, "admin");
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/feedback-setting", Json(new { enabled = true }))).StatusCode);
            var token = (await Read(await admin.GetAsync($"/api/bills/{billId}/feedback-link"))).GetProperty("token").GetString()!;

            var guest = f.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/api/feedback/not-a-real-token")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/feedback/{token}x")).StatusCode);

            // Turned off: the printed QR stops working.
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/feedback-setting", Json(new { enabled = false }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/feedback/{token}")).StatusCode);
            Assert.False((await Read(await admin.GetAsync("/api/admin/feedback-setting"))).GetProperty("enabled").GetBoolean());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/admin/feedback")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/feedback-setting", Json(new { enabled = true }))).StatusCode);
            var empty = await Read(await As(f, "manager").GetAsync("/api/admin/feedback"));
            Assert.Equal(0, empty.GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKind.Null, empty.GetProperty("average").ValueKind);
        }
    }
}
