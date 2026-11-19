using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Two screens save different outlet settings at the same moment: both changes stay.
    public class SettingsMergeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SettingsMergeTests(WebApplicationFactory<Program> factory)
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
        public void Merge_KeepsTheOtherScreensChange()
        {
            var original = "{\"reprintNeedsPin\":true,\"qrOrderingEnabled\":false,\"upiId\":\"a@okaxis\"}";
            var mine = "{\"reprintNeedsPin\":false,\"qrOrderingEnabled\":false,\"upiId\":\"a@okaxis\"}";
            var theirs = "{\"reprintNeedsPin\":true,\"qrOrderingEnabled\":true,\"upiId\":\"b@okaxis\"}";

            var merged = JsonNode.Parse(AppDbContext.MergeSettings(original, mine, theirs))!;

            Assert.False(merged["reprintNeedsPin"]!.GetValue<bool>());
            Assert.True(merged["qrOrderingEnabled"]!.GetValue<bool>());
            Assert.Equal("b@okaxis", merged["upiId"]!.GetValue<string>());
        }

        [Fact]
        public void Merge_OfBrokenJson_KeepsThisSave()
        {
            Assert.Equal("{\"a\":1}", AppDbContext.MergeSettings("not json", "{\"a\":1}", "{}"));
        }

        [Fact]
        public async Task TwoSavesOfDifferentSettings_BothStay()
        {
            using var f = Build();
            using var first = f.Services.CreateScope();
            using var second = f.Services.CreateScope();
            var db1 = first.ServiceProvider.GetRequiredService<AppDbContext>();
            var db2 = second.ServiceProvider.GetRequiredService<AppDbContext>();

            // Both screens read the settings before either saves.
            var outlet1 = db1.Outlets.Single(o => o.Id == 1);
            var outlet2 = db2.Outlets.Single(o => o.Id == 1);
            var reprint = outlet1.GetSettings();
            reprint.ReprintNeedsPin = false;
            outlet1.Settings = reprint.ToJson();
            var qr = outlet2.GetSettings();
            qr.QrOrderingEnabled = true;
            outlet2.Settings = qr.ToJson();

            await db1.SaveChangesAsync();
            await db2.SaveChangesAsync();

            using var check = f.Services.CreateScope();
            var saved = check.ServiceProvider.GetRequiredService<AppDbContext>().Outlets.AsNoTracking().Single(o => o.Id == 1).GetSettings();
            Assert.False(saved.ReprintNeedsPin);
            Assert.True(saved.QrOrderingEnabled);
        }
    }
}
