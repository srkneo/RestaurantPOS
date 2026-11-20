using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using POSSystem.Data;
using POSSystem.Logging;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class SentrySetupTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SentrySetupTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Dsn_EmptyMeansOff(string? value)
        {
            Assert.Null(SentrySetup.Dsn(Config(new() { ["SENTRY_DSN"] = value })));
        }

        [Fact]
        public void Dsn_IsTrimmed()
        {
            Assert.Equal("https://key@o1.ingest.sentry.io/2",
                SentrySetup.Dsn(Config(new() { ["SENTRY_DSN"] = "  https://key@o1.ingest.sentry.io/2 " })));
        }

        [Fact]
        public void Options_SendNoPersonalData()
        {
            var o = new SentryAspNetCoreOptions();
            SentrySetup.Configure(o, "https://key@o1.ingest.sentry.io/2", Config(new() { ["App:Version"] = "2.3.0" }));
            Assert.False(o.SendDefaultPii);
            Assert.Equal(RequestSize.None, o.MaxRequestBodySize);
            Assert.True(o.TracesSampleRate == 0);
            Assert.Equal(LogLevel.Error, o.MinimumEventLevel);
            Assert.Equal("production", o.Environment);
            Assert.Equal("restaurantpos@2.3.0", o.Release);
        }

        [Fact]
        public void Options_EnvironmentCanBeSet()
        {
            var o = new SentryAspNetCoreOptions();
            SentrySetup.Configure(o, "https://key@o1.ingest.sentry.io/2", Config(new() { ["SENTRY_ENVIRONMENT"] = "staging" }));
            Assert.Equal("staging", o.Environment);
            Assert.Equal("restaurantpos@dev", o.Release);
        }

        [Fact]
        public async Task NoDsn_SentryStaysOff()
        {
            var dbName = Guid.NewGuid().ToString();
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            }).CreateClient();

            var res = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.False(SentrySdk.IsEnabled);
        }
    }
}
