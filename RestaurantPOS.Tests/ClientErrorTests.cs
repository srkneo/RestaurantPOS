using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using POSSystem.Data;
using POSSystem.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class ClientErrorTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ClientErrorTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private sealed class CaptureProvider : ILoggerProvider
        {
            public readonly ConcurrentQueue<string> Lines = new();
            public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
            public void Dispose() { }

            private sealed class Capture : ILogger
            {
                private readonly CaptureProvider _p;
                private readonly string _cat;
                public Capture(CaptureProvider p, string cat) { _p = p; _cat = cat; }
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    if (_cat == "BrowserErrors") _p.Lines.Enqueue(logLevel + " " + formatter(state, exception));
                }
            }
        }

        private (HttpClient client, CaptureProvider logs) Build()
        {
            var dbName = Guid.NewGuid().ToString();
            var logs = new CaptureProvider();
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(l => l.AddProvider(logs));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                });
            }).CreateClient();
            return (client, logs);
        }

        [Fact]
        public async Task Report_IsLogged()
        {
            var (client, logs) = Build();
            var res = await client.PostAsJsonAsync("/api/client-errors",
                new { message = "Cannot read properties of undefined", stack = "at POSMenu.jsx:10", url = "/pos", source = "error" });
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
            var line = Assert.Single(logs.Lines);
            Assert.StartsWith("Warning", line);
            Assert.Contains("Cannot read properties of undefined", line);
            Assert.Contains("/pos", line);
            Assert.Contains("POSMenu.jsx:10", line);
        }

        [Fact]
        public async Task EmptyMessage_IsRejected()
        {
            var (client, logs) = Build();
            var res = await client.PostAsJsonAsync("/api/client-errors", new { message = "  ", url = "/pos" });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Empty(logs.Lines);
        }

        [Fact]
        public async Task LongMessage_IsCut()
        {
            var (client, logs) = Build();
            var res = await client.PostAsJsonAsync("/api/client-errors", new { message = new string('m', 3000), url = "/pos" });
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
            var line = Assert.Single(logs.Lines);
            Assert.Contains(new string('m', ClientErrors.MaxMessage), line);
            Assert.DoesNotContain(new string('m', ClientErrors.MaxMessage + 1), line);
        }

        [Fact]
        public async Task FakeLogLines_AreFlattened()
        {
            var (client, logs) = Build();
            await client.PostAsJsonAsync("/api/client-errors", new { message = "real\r\nERROR fake line\u001b[31m", url = "/pos\nx" });
            var line = Assert.Single(logs.Lines);
            var firstLine = line.Split('\n')[0];
            Assert.Contains("real  ERROR fake line", firstLine);
            // char check: a string search ignores control characters under ICU and "finds" them at position 0.
            Assert.False(line.Contains('\u001b'), "escape character left in the log line");
        }

        [Fact]
        public void Cut_RemovesControlCharacters_KeepsStackLines()
        {
            Assert.Equal("a b", ClientErrors.Cut("a\tb", 10));
            Assert.Equal("ab", ClientErrors.Cut("abcdef", 2));
            Assert.Equal("", ClientErrors.Cut(null, 5));
            Assert.Equal("l1\nl2", ClientErrors.Cut("l1\nl2", 50, keepNewLines: true));
        }
    }
}
