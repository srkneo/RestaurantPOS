using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Self-signup gives a 30-day trial; the platform owner records payments; 7 days after the paid-up date the
    // restaurant can still see everything but takes no new orders or bills.
    public class SubscriptionTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string PlatformEmail = "owner@pos.local";
        private const string PlatformPassword = "Platform#2026x";
        private const string OwnerPassword = "Owner#2026xy";
        private readonly WebApplicationFactory<Program> _factory;

        public SubscriptionTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private WebApplicationFactory<Program> Build()
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Platform:Email", PlatformEmail);
                builder.UseSetting("Platform:Password", PlatformPassword);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            });
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
        {
            var client = f.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/login", Json(new { email, password }))).StatusCode);
            return client;
        }

        private static object Signup(string email, string phone = "98250 12345") => new
        {
            restaurantName = "Patel Cafe", ownerName = "Kiran Patel", ownerEmail = email, ownerPhone = phone, ownerPassword = OwnerPassword
        };

        [Fact]
        public async Task Signup_Trial_Payment_AndSuspension()
        {
            using var f = Build();
            var anon = f.CreateClient();
            var plans = await Read(await anon.GetAsync("/api/signup/plans"));
            Assert.Equal(30, plans.GetProperty("trialDays").GetInt32());
            Assert.Equal(1000m, plans.GetProperty("monthly").GetDecimal());
            Assert.Equal(10000m, plans.GetProperty("yearly").GetDecimal());

            var badPhone = await anon.PostAsync("/api/signup", Json(Signup("kiran@cafe.local", "123")));
            Assert.Equal(HttpStatusCode.BadRequest, badPhone.StatusCode);
            Assert.Contains("ownerphone", await badPhone.Content.ReadAsStringAsync());

            var signup = await anon.PostAsync("/api/signup", Json(Signup("kiran@cafe.local")));
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
            var today = Subscription.Today();
            var trialUntil = today.AddDays(30);
            Assert.Equal(trialUntil.ToString("yyyy-MM-dd"), (await Read(signup)).GetProperty("trialUntil").GetString());

            var again = await anon.PostAsync("/api/signup", Json(Signup("kiran@cafe.local")));
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
            Assert.Contains("owneremail", await again.Content.ReadAsStringAsync());

            var owner = await SignedIn(f, "kiran@cafe.local", OwnerPassword);
            var sub = await Read(await owner.GetAsync("/api/subscription"));
            Assert.Equal("trial", sub.GetProperty("state").GetString());
            Assert.Equal(30, sub.GetProperty("daysLeft").GetInt32());
            Assert.Equal(10, (await Read(await owner.GetAsync("/api/tables"))).GetArrayLength());

            var platform = await SignedIn(f, PlatformEmail, PlatformPassword);
            var row = (await Read(await platform.GetAsync("/api/platform/tenants"))).EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Patel Cafe");
            var id = row.GetProperty("id").GetInt32();
            Assert.Equal("trial", row.GetProperty("state").GetString());
            Assert.Equal("98250 12345", row.GetProperty("ownerPhone").GetString());
            Assert.Equal("none", (await Read(await platform.GetAsync("/api/platform/tenants"))).EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == 1).GetProperty("state").GetString());

            // A monthly payment during the trial starts the day after the trial.
            var paid = await platform.PostAsync($"/api/platform/tenants/{id}/payments", Json(new { plan = "monthly", reference = "UPI 4521" }));
            Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
            var expected = trialUntil.AddDays(1).AddMonths(1).AddDays(-1);
            Assert.Equal(expected.ToString("yyyy-MM-dd"), (await Read(paid)).GetProperty("paidUntil").GetString());
            var history = await Read(await platform.GetAsync($"/api/platform/tenants/{id}/payments"));
            Assert.Equal(1000m, history[0].GetProperty("amount").GetDecimal());
            Assert.Equal("UPI 4521", history[0].GetProperty("reference").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsync($"/api/platform/tenants/{id}/payments", Json(new { plan = "weekly" }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/platform/tenants/{id}/payments", Json(new { plan = "monthly" }))).StatusCode);

            // Overdue (within 7 days): a warning only, orders still go through to their normal checks.
            var overdue = await platform.PutAsync($"/api/platform/tenants/{id}/subscription", Json(new { plan = "monthly", paidUntil = today.AddDays(-3).ToString("yyyy-MM-dd") }));
            Assert.Equal("overdue", (await Read(overdue)).GetProperty("state").GetString());
            Assert.NotEqual((HttpStatusCode)402, (await owner.PostAsync("/api/orders", Json(new { orderType = "Takeaway", items = Array.Empty<object>() }))).StatusCode);

            // Suspended: new orders stop, everything stays viewable.
            var suspended = await platform.PutAsync($"/api/platform/tenants/{id}/subscription", Json(new { plan = "monthly", paidUntil = today.AddDays(-10).ToString("yyyy-MM-dd") }));
            Assert.Equal("suspended", (await Read(suspended)).GetProperty("state").GetString());
            var blocked = await owner.PostAsync("/api/orders", Json(new { orderType = "Takeaway", items = Array.Empty<object>() }));
            Assert.Equal((HttpStatusCode)402, blocked.StatusCode);
            Assert.Contains("subscription-ended", await blocked.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/tables")).StatusCode);
            Assert.Equal("suspended", (await Read(await owner.GetAsync("/api/subscription"))).GetProperty("state").GetString());

            // Free use: no date.
            var free = await platform.PutAsync($"/api/platform/tenants/{id}/subscription", Json(new { plan = (string?)null, paidUntil = (string?)null }));
            Assert.Equal("none", (await Read(free)).GetProperty("state").GetString());
        }
    }
}
