using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using POSSystem.Alerts;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class HardeningTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public HardeningTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

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
                });
            });
        }

        [Fact]
        public async Task SignIn_IsLimitedToTenTriesAMinute()
        {
            using var f = Build();
            var client = f.CreateClient();
            HttpStatusCode last = HttpStatusCode.OK;
            for (var i = 0; i < 11; i++)
            {
                var response = await client.PostAsync("/api/auth/login",
                    new StringContent("{\"email\":\"nobody@test.local\",\"password\":\"Wrong#123x\"}", Encoding.UTF8, "application/json"));
                if (i < 10) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                last = response.StatusCode;
            }
            Assert.Equal(HttpStatusCode.TooManyRequests, last);
        }

        [Fact]
        public async Task Health_AndReadiness_AreOpen()
        {
            using var f = Build();
            var client = f.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        }

        [Fact]
        public void ErrorAlerts_OnlyErrors_NotFromHttpClient_AndCapped()
        {
            var queue = new ErrorAlertQueue();
            var app = new ErrorAlertLogger("POSSystem.Services.OrderService", queue);
            var http = new ErrorAlertLogger("System.Net.Http.HttpClient.telegram-alerts", queue);

            Assert.False(app.IsEnabled(LogLevel.Warning));
            Assert.True(app.IsEnabled(LogLevel.Error));
            Assert.False(http.IsEnabled(LogLevel.Error));

            for (var i = 0; i < ErrorAlertQueue.Capacity + 5; i++)
                app.Log(LogLevel.Error, new EventId(1), $"Order {i} failed", new InvalidOperationException("boom"), (s, e) => s);

            var (items, dropped) = queue.Take(10);
            Assert.Equal(10, items.Count);
            Assert.Equal(ErrorAlertQueue.Capacity + 5 - 10, dropped);
            Assert.Contains("InvalidOperationException: boom", items[0]);

            var text = ErrorAlertSender.Compose("Anand pilot", items, dropped);
            Assert.StartsWith("⚠️ Anand pilot: 55 error(s)", text);
            Assert.Contains("and 45 more", text);
            Assert.Equal(0, queue.Take(10).Items.Count);
        }
    }
}
