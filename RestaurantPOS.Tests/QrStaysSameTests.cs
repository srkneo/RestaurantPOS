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
    // The manager prints the table QR codes once and sticks them on the tables, so a code must never change by itself.
    public class QrStaysSameTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public QrStaysSameTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Table) Build()
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
            var table = new RestaurantTable { TableNumber = "Q3", QRCodeString = "t1-q3", SortOrder = 3 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            return (f, table.Id);
        }

        private static HttpClient Manager(WebApplicationFactory<Program> f)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", "manager");
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static async Task<string> Token(HttpClient manager, int table)
        {
            using var doc = JsonDocument.Parse(await (await manager.GetAsync("/api/admin/tables/qr")).Content.ReadAsStringAsync());
            return doc.RootElement.EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == table).GetProperty("token").GetString()!;
        }

        private static async Task<string> Image(HttpClient manager, int table) =>
            await (await manager.GetAsync($"/api/admin/tables/{table}/qr.svg?origin=https%3A%2F%2Fpos.example.com")).Content.ReadAsStringAsync();

        [Fact]
        public async Task QrCode_StaysTheSame_UntilChangeQr()
        {
            var (f, table) = Build();
            using var factory = f;
            var manager = Manager(f);

            // First the image (as when the sheet's pictures load), then the sheet, many times.
            var firstImage = await Image(manager, table);
            var token = await Token(manager, table);
            for (var i = 0; i < 5; i++)
            {
                Assert.Equal(token, await Token(manager, table));
                Assert.Equal(firstImage, await Image(manager, table));
            }

            // Editing the table (rename, sort, not in use and back) keeps the code.
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/admin/tables/{table}", new StringContent(
                JsonSerializer.Serialize(new { tableNumber = "Q3A", isActive = false, sortOrder = 9 }), Encoding.UTF8, "application/json"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync($"/api/admin/tables/{table}", new StringContent(
                JsonSerializer.Serialize(new { tableNumber = "Q3", isActive = true, sortOrder = 3 }), Encoding.UTF8, "application/json"))).StatusCode);
            Assert.Equal(token, await Token(manager, table));
            Assert.Equal(firstImage, await Image(manager, table));

            // Turning QR ordering off and on keeps the code too.
            await manager.PutAsync("/api/admin/tables/qr-setting", new StringContent("{\"enabled\":false}", Encoding.UTF8, "application/json"));
            await manager.PutAsync("/api/admin/tables/qr-setting", new StringContent("{\"enabled\":true}", Encoding.UTF8, "application/json"));
            Assert.Equal(token, await Token(manager, table));

            // Only Change QR gives a new code, and then it stays the same again.
            await manager.PostAsync($"/api/admin/tables/{table}/qr", new StringContent("{}", Encoding.UTF8, "application/json"));
            var changed = await Token(manager, table);
            Assert.NotEqual(token, changed);
            Assert.NotEqual(firstImage, await Image(manager, table));
            Assert.Equal(changed, await Token(manager, table));
        }

        [Fact]
        public async Task QrCode_IsMadeOnce_WhenThePicturesLoadTogether()
        {
            var (f, table) = Build();
            using var factory = f;
            var manager = Manager(f);
            var images = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Image(manager, table)));
            var token = await Token(manager, table);
            Assert.All(images, svg => Assert.Equal(images[0], svg));
            Assert.Equal(token, await Token(manager, table));
        }
    }
}
