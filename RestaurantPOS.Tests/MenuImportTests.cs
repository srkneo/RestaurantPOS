using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Menu;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class MenuImportTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        private readonly WebApplicationFactory<Program> _factory;

        public MenuImportTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private class FakeReader : IMenuReader
        {
            public Func<IReadOnlyList<MenuPhoto>, IReadOnlyList<MenuDraftItem>> Answer { get; set; } = _ => new[]
            {
                new MenuDraftItem("Starters", "Paneer Tikka", 220m, true, "Grilled cottage cheese"),
                new MenuDraftItem("Starters", "Chicken Lollipop", 260m, false, null),
                new MenuDraftItem("Mains", "Fries", 90m, true, null),
                new MenuDraftItem("ગુજરાતી થાળી", "ગુજરાતી થાળી", 180m, true, null),
            };
            public int Calls { get; private set; }

            public Task<IReadOnlyList<MenuDraftItem>> ReadAsync(IReadOnlyList<MenuPhoto> photos, CancellationToken ct)
            {
                Calls++;
                return Task.FromResult(Answer(photos));
            }
        }

        private class UnavailableReader : IMenuReader
        {
            public Task<IReadOnlyList<MenuDraftItem>> ReadAsync(IReadOnlyList<MenuPhoto> photos, CancellationToken ct) =>
                throw new MenuScanUnavailableException("Menu scan is not set up on this server (Gemini__ApiKey is missing).");
        }

        private WebApplicationFactory<Program> Build(IMenuReader reader)
        {
            var dbName = Guid.NewGuid().ToString();
            var f = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.RemoveAll<IMenuReader>();
                    services.AddSingleton(reader);
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                });
            });
            // Restaurant 1 already sells Fries.
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sides = new MenuCategory { Name = "Sides", SortOrder = 1 };
            db.MenuCategories.Add(sides);
            db.SaveChanges();
            db.MenuItems.Add(new MenuItem { Name = "Fries", Price = 80m, Category = "Sides", CategoryId = sides.Id, SortOrder = 1 });
            db.SaveChanges();
            return f;
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static MultipartFormDataContent Photos(params byte[][] files)
        {
            var form = new MultipartFormDataContent();
            var n = 0;
            foreach (var bytes in files)
            {
                var part = new ByteArrayContent(bytes);
                part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                form.Add(part, "photos", $"page{++n}.jpg");
            }
            return form;
        }

        private static StringContent Json(object body) => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        // ---------- Reading Gemini's answer ----------

        [Fact]
        public void GeminiAnswer_IsCleanedUp()
        {
            var menu = JsonSerializer.Serialize(new
            {
                items = new object[]
                {
                    new { category = " Starters ", name = "Paneer   Tikka (Half)", price = 120.456, isVeg = true, description = "" },
                    new { category = "Starters", name = "paneer tikka (half)", price = 120, isVeg = true, description = "" },
                    new { category = "", name = "ગુજરાતી થાળી", price = 180, isVeg = true, description = "Dal, bhaat, rotli" },
                    new { category = "Mains", name = "Chicken Curry", price = 240, isVeg = false, description = "" },
                    new { category = "Mains", name = "", price = 50, isVeg = true, description = "" },
                    new { category = "Mains", name = "Free water", price = -1, isVeg = true, description = "" },
                }
            });
            var body = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = menu } } } } } });

            var items = GeminiMenuReader.ParseResponse(body);

            Assert.Equal(3, items.Count);
            Assert.Equal(new MenuDraftItem("Starters", "Paneer Tikka (Half)", 120.46m, true, null), items[0]);
            Assert.Equal(new MenuDraftItem("Other", "ગુજરાતી થાળી", 180m, true, "Dal, bhaat, rotli"), items[1]);
            Assert.False(items[2].IsVeg);
        }

        [Fact]
        public void GeminiAnswer_WithoutContent_IsAFailure()
        {
            Assert.Throws<MenuScanFailedException>(() => GeminiMenuReader.ParseResponse("{\"candidates\":[{\"finishReason\":\"SAFETY\"}]}"));
            Assert.Throws<MenuScanFailedException>(() => GeminiMenuReader.ParseResponse("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"not json\"}]}}]}"));
        }

        private class RecordingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? Request { get; private set; }
            public string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Request = request;
                Body = await request.Content!.ReadAsStringAsync(ct);
                var menu = "{\"items\":[{\"category\":\"Mains\",\"name\":\"Dal Fry\",\"price\":150,\"isVeg\":true,\"description\":\"\"}]}";
                var answer = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = menu } } } } } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
            }
        }

        [Fact]
        public async Task GeminiReader_SendsPhotosAndKey_AndReadsTheAnswer()
        {
            var handler = new RecordingHandler();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" }).Build();
            var reader = new GeminiMenuReader(new HttpClient(handler), config);

            var items = await reader.ReadAsync(new[] { new MenuPhoto(Jpeg, "image/jpeg") }, CancellationToken.None);

            Assert.Equal("Dal Fry", Assert.Single(items).Name);
            Assert.Equal("test-key", handler.Request!.Headers.GetValues("x-goog-api-key").Single());
            Assert.Contains($"/models/{GeminiMenuReader.DefaultModel}:generateContent", handler.Request.RequestUri!.ToString());
            using var sent = JsonDocument.Parse(handler.Body!);
            var parts = sent.RootElement.GetProperty("contents")[0].GetProperty("parts");
            Assert.Equal(GeminiMenuReader.Prompt, parts[0].GetProperty("text").GetString());
            var photo = parts[1].GetProperty("inline_data");
            Assert.Equal("image/jpeg", photo.GetProperty("mime_type").GetString());
            Assert.Equal(Convert.ToBase64String(Jpeg), photo.GetProperty("data").GetString());
            Assert.Equal("application/json", sent.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        }

        [Fact]
        public async Task GeminiReader_WithoutKey_IsUnavailable()
        {
            var reader = new GeminiMenuReader(new HttpClient(new RecordingHandler()), new ConfigurationBuilder().Build());
            await Assert.ThrowsAsync<MenuScanUnavailableException>(() => reader.ReadAsync(new[] { new MenuPhoto(Jpeg, "image/jpeg") }, CancellationToken.None));
        }

        // ---------- Scan endpoint ----------

        [Fact]
        public async Task Scan_ReturnsDraft_AndMarksItemsAlreadyOnTheMenu()
        {
            using var f = Build(new FakeReader());
            var response = await As(f, "manager").PostAsync("/api/admin/menu/scan", Photos(Jpeg, Jpeg));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(4, items.GetArrayLength());
            Assert.Equal(19, doc.RootElement.GetProperty("scansLeftToday").GetInt32());
            var fries = items.EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Fries");
            Assert.True(fries.GetProperty("alreadyOnMenu").GetBoolean());
            var paneer = items.EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Paneer Tikka");
            Assert.False(paneer.GetProperty("alreadyOnMenu").GetBoolean());

            // Nothing is saved by a scan.
            using var scope = f.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().MenuItems.CountAsync());
        }

        [Fact]
        public async Task Scan_RefusesNonImages_TooManyPhotos_AndCashiers()
        {
            var reader = new FakeReader();
            using var f = Build(reader);
            var notImage = await As(f, "admin").PostAsync("/api/admin/menu/scan", Photos(Encoding.UTF8.GetBytes("%PDF-1.7 not a photo")));
            Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);
            Assert.Contains("photos", await notImage.Content.ReadAsStringAsync());

            var tooMany = await As(f, "admin").PostAsync("/api/admin/menu/scan", Photos(Jpeg, Jpeg, Jpeg, Jpeg, Jpeg));
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

            var cashier = await As(f, "cashier").PostAsync("/api/admin/menu/scan", Photos(Jpeg));
            Assert.Equal(HttpStatusCode.Forbidden, cashier.StatusCode);
            Assert.Equal(0, reader.Calls);
        }

        [Fact]
        public async Task Scan_WithoutGeminiKey_Returns503()
        {
            using var f = Build(new UnavailableReader());
            var response = await As(f, "admin").PostAsync("/api/admin/menu/scan", Photos(Jpeg));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("not set up", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Scan_IsLimitedPerRestaurantPerDay()
        {
            var reader = new FakeReader();
            using var f = Build(reader);
            for (var i = 0; i < MenuImportEndpoints.ScansPerDay; i++)
                Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PostAsync("/api/admin/menu/scan", Photos(Jpeg))).StatusCode);

            Assert.Equal(HttpStatusCode.TooManyRequests, (await As(f, "admin").PostAsync("/api/admin/menu/scan", Photos(Jpeg))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin", "2").PostAsync("/api/admin/menu/scan", Photos(Jpeg))).StatusCode);
            Assert.Equal(MenuImportEndpoints.ScansPerDay + 1, reader.Calls);
        }

        // ---------- Import endpoint ----------

        [Fact]
        public async Task Import_AddsCategoriesAndItems_SkipsWhatIsAlreadyThere()
        {
            using var f = Build(new FakeReader());
            var response = await As(f, "admin").PostAsync("/api/admin/menu/import", Json(new
            {
                taxRate = 5,
                items = new object[]
                {
                    new { category = "Starters", name = "Paneer Tikka", price = 220, isVeg = true, description = "Grilled" },
                    new { category = "starters", name = "Chicken Lollipop", price = 260, isVeg = false },
                    new { category = "Sides", name = "fries", price = 90, isVeg = true },
                    new { category = "Starters", name = "Paneer Tikka", price = 220, isVeg = true },
                    new { category = "", name = "ગુજરાતી થાળી", price = 180, isVeg = true },
                }
            }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(3, result.RootElement.GetProperty("created").GetInt32());
            Assert.Equal(2, result.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(2, result.RootElement.GetProperty("categoriesCreated").GetInt32());

            var menu = await As(f, "admin").GetStringAsync("/api/admin/menu/items");
            using var doc = JsonDocument.Parse(menu);
            var items = doc.RootElement.EnumerateArray().ToList();
            Assert.Equal(4, items.Count);
            var chicken = items.Single(i => i.GetProperty("name").GetString() == "Chicken Lollipop");
            Assert.Equal("Starters", chicken.GetProperty("category").GetString());
            Assert.False(chicken.GetProperty("isVeg").GetBoolean());
            Assert.Equal(5m, chicken.GetProperty("taxRate").GetDecimal());
            Assert.Equal("Other", items.Single(i => i.GetProperty("name").GetString() == "ગુજરાતી થાળી").GetProperty("category").GetString());

            // Restaurant 2 is not touched.
            var other = await As(f, "admin", "2").GetStringAsync("/api/admin/menu/items");
            Assert.Equal("[]", other);
        }

        [Fact]
        public async Task Import_WithABadRow_SavesNothing()
        {
            using var f = Build(new FakeReader());
            var response = await As(f, "admin").PostAsync("/api/admin/menu/import", Json(new
            {
                taxRate = 5,
                items = new object[]
                {
                    new { category = "Starters", name = "Paneer Tikka", price = 220, isVeg = true },
                    new { category = "Starters", name = "", price = 100, isVeg = true },
                }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Row 2", await response.Content.ReadAsStringAsync());

            var badRate = await As(f, "admin").PostAsync("/api/admin/menu/import", Json(new { taxRate = 7, items = new[] { new { category = "A", name = "B", price = 1, isVeg = true } } }));
            Assert.Equal(HttpStatusCode.BadRequest, badRate.StatusCode);

            using var scope = f.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().MenuItems.CountAsync());
        }

        [Fact]
        public async Task Import_IsForOwnersAndManagersOnly()
        {
            using var f = Build(new FakeReader());
            var response = await As(f, "cashier").PostAsync("/api/admin/menu/import", Json(new { taxRate = 5, items = new[] { new { category = "A", name = "B", price = 1, isVeg = true } } }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
