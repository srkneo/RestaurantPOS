using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Trace;
using POSSystem.Data;
using POSSystem.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class TelemetrySetupTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TelemetrySetupTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("  ", false)]
        [InlineData("https://otlp-gateway-prod-ap-southeast-1.grafana.net/otlp", true)]
        public void Enabled_OnlyWithEndpoint(string? endpoint, bool expected)
        {
            Assert.Equal(expected, TelemetrySetup.Enabled(Config(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint })));
        }

        [Theory]
        [InlineData(null, 0.1)]
        [InlineData("abc", 0.1)]
        [InlineData("NaN", 0.1)]
        [InlineData("0.5", 0.5)]
        [InlineData("1", 1.0)]
        [InlineData("7", 1.0)]
        [InlineData("-2", 0.0)]
        [InlineData("0", 0.0)]
        public void TraceRatio_IsSafe(string? raw, double expected)
        {
            Assert.Equal(expected, TelemetrySetup.TraceRatio(Config(new() { ["TELEMETRY_TRACE_RATIO"] = raw })));
        }

        [Theory]
        [InlineData("/health", true)]
        [InlineData("/health/ready", true)]
        [InlineData("/orderHub/negotiate", true)]
        [InlineData("/api/orders", false)]
        [InlineData("/api/bills/5", false)]
        public void Noise_IsLeftOut(string path, bool noise)
        {
            Assert.Equal(noise, TelemetrySetup.IsNoise(new PathString(path)));
        }

        [Fact]
        public async Task NoEndpoint_TelemetryStaysOff()
        {
            var dbName = Guid.NewGuid().ToString();
            var app = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            });
            var res = await app.CreateClient().GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Null(app.Services.GetService<TracerProvider>());
        }
    }
}
