using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class RequestIdTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RequestIdTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private HttpClient Client()
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
            }).CreateClient();
        }

        private static async Task<string> IdFor(HttpClient client, string? sent)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            if (sent != null) req.Headers.TryAddWithoutValidation(RequestIds.Header, sent);
            var res = await client.SendAsync(req);
            Assert.True(res.Headers.TryGetValues(RequestIds.Header, out var values), "response has no X-Request-ID");
            return values!.Single();
        }

        [Fact]
        public async Task EveryResponse_HasRequestId()
        {
            var client = Client();
            var first = await IdFor(client, null);
            var second = await IdFor(client, null);
            Assert.True(RequestIds.IsSafe(first));
            Assert.NotEqual(first, second);
        }

        [Fact]
        public async Task SafeIncomingId_IsKept()
        {
            Assert.Equal("load-test.42", await IdFor(Client(), "load-test.42"));
        }

        [Theory]
        [InlineData("has space")]
        [InlineData("<script>alert(1)</script>")]
        [InlineData("a;b")]
        public async Task UnsafeIncomingId_IsReplaced(string sent)
        {
            var got = await IdFor(Client(), sent);
            Assert.NotEqual(sent, got);
            Assert.True(RequestIds.IsSafe(got));
        }

        [Fact]
        public async Task TooLongIncomingId_IsReplaced()
        {
            var sent = new string('a', 65);
            var got = await IdFor(Client(), sent);
            Assert.NotEqual(sent, got);
            Assert.True(got.Length <= 64);
        }

        [Fact]
        public void Pick_KeepsSafe_ReplacesEmpty()
        {
            Assert.Equal("abc-1", RequestIds.Pick("abc-1"));
            Assert.Equal(16, RequestIds.Pick("").Length);
            Assert.Equal(16, RequestIds.Pick(null).Length);
        }

        [Fact]
        public void ErrorResponses_CarryRequestId()
        {
            var http = new DefaultHttpContext { TraceIdentifier = "err-123" };
            var ctx = new ProblemDetailsContext { HttpContext = http, ProblemDetails = new ProblemDetails() };
            RequestIds.AddToProblem(ctx);
            Assert.Equal("err-123", ctx.ProblemDetails.Extensions["requestId"]);
        }
    }
}
