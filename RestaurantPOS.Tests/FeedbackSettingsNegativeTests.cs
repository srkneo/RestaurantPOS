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
    // Guest feedback and the owner's settings pages: bad tokens and input, one rating per bill, safe CSV, limits and roles.
    public class FeedbackSettingsNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public FeedbackSettingsNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Thali) Build(bool feedbackOn = true)
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
            settings.FeedbackOnBill = feedbackOn;
            outlet.Settings = settings.ToJson();
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return (f, thali.Id);
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

        private static async Task<JsonElement> Bill(HttpClient cashier, int thali)
        {
            var placed = await Read(await cashier.PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = thali, quantity = 1 } }
            })));
            var res = await cashier.PostAsync($"/api/orders/{placed.GetProperty("orderId").GetInt32()}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            return await Read(res);
        }

        private static async Task<string> Token(HttpClient cashier, int billId)
        {
            var res = await cashier.GetAsync($"/api/bills/{billId}/feedback-link");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return (await Read(res)).GetProperty("token").GetString()!;
        }

        private static Task<HttpResponseMessage> Rate(HttpClient guest, string token, int rating, string? comment = null) =>
            guest.PostAsync($"/api/feedback/{Uri.EscapeDataString(token)}", Json(new { rating, comment }));

        [Fact]
        public async Task Feedback_BadTokens_AndVoidBills_AreNotFound()
        {
            var (f, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var guest = f.CreateClient();
            var bill = await Bill(cashier, thali);
            var token = await Token(cashier, bill.GetProperty("id").GetInt32());

            Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/feedback/{Uri.EscapeDataString(token)}")).StatusCode);
            foreach (var bad in new[] { "abc", token[..^2] + "zz", new string('a', 401), "MTox" })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/feedback/{Uri.EscapeDataString(bad)}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await Rate(guest, bad, 5)).StatusCode);
            }

            var voided = await As(f, "manager").PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/void",
                Json(new { reason = "Wrong", version = bill.GetProperty("version").GetInt32() }));
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/feedback/{Uri.EscapeDataString(token)}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Rate(guest, token, 5)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/bills/99999/feedback-link")).StatusCode);
        }

        [Fact]
        public async Task Feedback_RatingLimits_OnePerBill_AndTurnedOff()
        {
            var (f, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var guest = f.CreateClient();
            var bill = await Bill(cashier, thali);
            var token = await Token(cashier, bill.GetProperty("id").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await Rate(guest, token, 0)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Rate(guest, token, 6)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Rate(guest, token, -1)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Rate(guest, token, 5, new string('c', 501))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Rate(guest, token, 4, "Good dal")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Rate(guest, token, 1, "Changed my mind")).StatusCode);

            var list = await Read(await As(f, "manager").GetAsync("/api/admin/feedback"));
            Assert.Equal(1, list.GetProperty("count").GetInt32());
            Assert.Equal(4m, list.GetProperty("average").GetDecimal());

            Assert.True((await As(f, "admin").PutAsync("/api/admin/feedback-setting", Json(new { enabled = false }))).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/feedback/{Uri.EscapeDataString(token)}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/feedback-link")).StatusCode);
        }

        [Fact]
        public async Task FeedbackCsv_KeepsFormulasAsText_AndListIsForManagers()
        {
            var (f, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var guest = f.CreateClient();
            var comments = new[] { "=HYPERLINK(\"http://evil\",\"x\")", "+SUM(1,2)", "-2+3", "@cmd", "Nice, \"tasty\"" };
            foreach (var comment in comments)
            {
                var bill = await Bill(cashier, thali);
                Assert.Equal(HttpStatusCode.OK, (await Rate(guest, await Token(cashier, bill.GetProperty("id").GetInt32()), 5, comment)).StatusCode);
            }
            var csv = await (await As(f, "manager").GetAsync("/api/admin/feedback.csv")).Content.ReadAsStringAsync();
            Assert.Contains("'=HYPERLINK", csv);
            Assert.Contains("'+SUM", csv);
            Assert.Contains("'-2+3", csv);
            Assert.Contains("'@cmd", csv);
            Assert.Contains("\"Nice, \"\"tasty\"\"\"", csv);
            Assert.DoesNotContain(",=HYPERLINK", csv);

            foreach (var role in new[] { "cashier", "waiter", "kitchen" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).GetAsync("/api/admin/feedback")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await As(f, role).GetAsync("/api/admin/feedback.csv")).StatusCode);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync("/api/admin/feedback-setting", Json(new { enabled = false }))).StatusCode);
        }

        [Fact]
        public async Task Settings_Limits_AreChecked()
        {
            var (f, _) = Build();
            using var factory = f;
            var admin = As(f, "admin");
            foreach (var limit in new[] { -1m, 100.5m })
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/admin/discount-limit", Json(new { cashierMaxDiscountPercent = limit }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/discount-limit", Json(new { cashierMaxDiscountPercent = (decimal?)null }))).StatusCode);

            foreach (var url in new[] { "http://g.page/r/abc", "javascript:alert(1)", "g.page/r/abc", "https://g.page/" + new string('a', 300) })
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/admin/review-link", Json(new { reviewUrl = url }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/review-link", Json(new { reviewUrl = "" }))).StatusCode);

            object Billing(string prefix = "INV", int paper = 80, string language = "en", decimal? smallChange = 5m, string? upiName = null, string taxMode = "None") =>
                new { taxMode, pricesIncludeTax = false, billPrefix = prefix, upiId = (string?)null, upiName, paperWidth = paper, printLanguage = language, smallChangeLimit = smallChange };
            foreach (var body in new[]
            {
                Billing(prefix: ""), Billing(prefix: "INVO"), Billing(prefix: "I-1"), Billing(paper: 70), Billing(language: "fr"),
                Billing(smallChange: 21m), Billing(smallChange: -1m), Billing(upiName: new string('u', 51)), Billing(taxMode: "Flying"),
            })
            {
                var res = await admin.PutAsync("/api/admin/billing-settings", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/admin/billing-settings", Json(Billing(prefix: "ab1")))).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/settings/preset", Json(new { preset = "bar" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/settings/preset", Json(new { preset = (string?)null }))).StatusCode);
        }

        [Fact]
        public async Task OwnerSettings_AreOwnerOnly()
        {
            var (f, _) = Build();
            using var factory = f;
            foreach (var role in new[] { "manager", "cashier", "waiter", "kitchen" })
            {
                var c = As(f, role);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync("/api/admin/discount-limit", Json(new { cashierMaxDiscountPercent = 50m }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync("/api/admin/review-link", Json(new { reviewUrl = "https://g.page/r/x" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync("/api/admin/approval-rules", Json(new { reprintNeedsPin = false }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/settings/preset", Json(new { preset = "cafe" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/billing-settings")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/store")).StatusCode);
            }
        }
    }
}
