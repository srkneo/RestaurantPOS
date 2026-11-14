using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using POSSystem.Menu;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class MenuScanRetryTests
    {
        private static readonly MenuPhoto Photo = new(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg");

        // Answers with the given status codes in order; 200 returns one dish.
        private class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<int> _statuses;
            public List<string> Urls { get; } = new();

            public ScriptedHandler(params int[] statuses) => _statuses = new Queue<int>(statuses);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Urls.Add(request.RequestUri!.ToString());
                var status = _statuses.Count > 0 ? _statuses.Dequeue() : 503;
                if (status == 0) throw new HttpRequestException("no answer");
                var body = "{\"error\":{\"code\":" + status + "}}";
                if (status == 200)
                {
                    var menu = "{\"items\":[{\"category\":\"Mains\",\"name\":\"Dal Fry\",\"price\":150,\"isVeg\":true,\"description\":\"\"}]}";
                    body = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = menu } } } } } });
                }
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }

        private static GeminiMenuReader Reader(ScriptedHandler handler, string? model = null, string? fallback = null)
        {
            var settings = new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key", ["Gemini:Model"] = model, ["Gemini:FallbackModel"] = fallback };
            var reader = new GeminiMenuReader(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            reader.RetryDelay = _ => TimeSpan.Zero;
            return reader;
        }

        private static string ModelOf(string url) => url.Split("/models/")[1].Split(':')[0];

        [Fact]
        public async Task Busy_IsRetried_OnTheSameModel()
        {
            var handler = new ScriptedHandler(503, 200);
            var items = await Reader(handler).ReadAsync(new[] { Photo }, CancellationToken.None);

            Assert.Equal("Dal Fry", Assert.Single(items).Name);
            Assert.Equal(new[] { GeminiMenuReader.DefaultModel, GeminiMenuReader.DefaultModel }, handler.Urls.Select(ModelOf));
        }

        [Fact]
        public async Task StillBusy_SwitchesToTheFallbackModel()
        {
            var handler = new ScriptedHandler(503, 429, 200);
            var items = await Reader(handler, "model-a", "model-b").ReadAsync(new[] { Photo }, CancellationToken.None);

            Assert.Single(items);
            Assert.Equal(new[] { "model-a", "model-a", "model-b" }, handler.Urls.Select(ModelOf));
        }

        [Fact]
        public async Task RetiredModel_GoesStraightToTheFallback()
        {
            var handler = new ScriptedHandler(404, 200);
            await Reader(handler, "old-model", "new-model").ReadAsync(new[] { Photo }, CancellationToken.None);

            Assert.Equal(new[] { "old-model", "new-model" }, handler.Urls.Select(ModelOf));
        }

        [Fact]
        public async Task NoAnswer_IsTreatedLikeBusy()
        {
            var handler = new ScriptedHandler(0, 200);
            var items = await Reader(handler).ReadAsync(new[] { Photo }, CancellationToken.None);
            Assert.Single(items);
        }

        [Fact]
        public async Task AlwaysBusy_GivesAFriendlyMessage_AfterFourTries()
        {
            var handler = new ScriptedHandler(503, 503, 503, 503, 503);
            var ex = await Assert.ThrowsAsync<MenuScanFailedException>(() => Reader(handler).ReadAsync(new[] { Photo }, CancellationToken.None));

            Assert.Equal(GeminiMenuReader.BusyMessage, ex.Message);
            Assert.Equal(4, handler.Urls.Count);
        }

        [Fact]
        public async Task BadKey_IsNotRetried()
        {
            var handler = new ScriptedHandler(403, 200);
            var ex = await Assert.ThrowsAsync<MenuScanFailedException>(() => Reader(handler).ReadAsync(new[] { Photo }, CancellationToken.None));

            Assert.Contains("403", ex.Message);
            Assert.Single(handler.Urls);
        }

        [Fact]
        public async Task SameFallbackAsModel_IsNotTriedTwice()
        {
            var handler = new ScriptedHandler(404, 200);
            var ex = await Assert.ThrowsAsync<MenuScanFailedException>(() => Reader(handler, "only-model", "only-model").ReadAsync(new[] { Photo }, CancellationToken.None));

            Assert.Contains("no longer available", ex.Message);
            Assert.Single(handler.Urls);
        }
    }
}
