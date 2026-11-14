using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class KeyStorageTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public KeyStorageTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task SignInKeys_AreKeptInTheConfiguredFolder()
        {
            var keys = Path.Combine(Path.GetTempPath(), "pos-keys-" + Guid.NewGuid().ToString("N"));
            var dbName = Guid.NewGuid().ToString();
            try
            {
                using var f = _factory.WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("Admin:Email", "admin@test.local");
                    builder.UseSetting("Admin:Password", "Test-Passw0rd!");
                    builder.UseSetting("DataProtection:KeysPath", keys);
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                        services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    });
                });

                var login = await f.CreateClient().PostAsync("/api/auth/login",
                    new StringContent("{\"email\":\"admin@test.local\",\"password\":\"Test-Passw0rd!\"}", Encoding.UTF8, "application/json"));

                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                Assert.True(Directory.Exists(keys), "The key folder was not created.");
                Assert.NotEmpty(Directory.GetFiles(keys, "*.xml"));
            }
            finally
            {
                if (Directory.Exists(keys)) Directory.Delete(keys, true);
            }
        }
    }
}
