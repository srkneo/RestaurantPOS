using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // v1.7 cloud-to-shop menu sync, shop PC side: the cloud's menu is copied once, without duplicates,
    // removed items are hidden (not deleted) and the shop's own sold-out switch is kept.
    public class MenuSyncShopTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MenuSyncShopTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private class FakeMenuCloud : IMenuCloud
        {
            public MenuSnapshot? Next;
            public int Calls;
            public Task<(MenuSnapshot? Snapshot, string? Error)> GetMenu(MenuSyncRequest request, CancellationToken ct)
            {
                Calls++;
                (MenuSnapshot?, string?) result = Next is null ? (null, "offline") : (Next, null);
                return Task.FromResult(result);
            }
        }

        private WebApplicationFactory<Program> Build(FakeMenuCloud? cloud = null, bool shopPc = false)
        {
            var dbName = Guid.NewGuid().ToString();
            return _factory.WithWebHostBuilder(builder =>
            {
                if (shopPc) builder.UseSetting("Licence:Required", "true");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "Test"; o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                    if (cloud is not null)
                    {
                        services.RemoveAll(typeof(IMenuCloud));
                        services.AddSingleton<IMenuCloud>(cloud);
                    }
                });
            });
        }

        // Cloud ids are far from the shop's own ids on purpose, so a missing id mapping would show up.
        private static MenuSnapshot Snapshot(decimal thaliPrice = 180m, bool withPaneer = true, string version = "v1")
        {
            var items = new List<SyncItem> { new(5002, "Cloud Thali", thaliPrice, 701, "Cloud Mains", null, null, true, true, 5m, 2) };
            if (withPaneer) items.Insert(0, new SyncItem(5001, "Cloud Paneer", 220.5m, 701, "Cloud Mains", 801, "Spicy", true, true, 5m, 1));
            var variants = withPaneer ? new List<SyncOption> { new(6001, 5001, "Full", 300m, 1, true) } : new List<SyncOption>();
            var addOns = withPaneer ? new List<SyncOption> { new(7001, 5001, "Extra cheese", 30m, 1, true) } : new List<SyncOption>();
            var combos = withPaneer ? new List<SyncComboPart> { new(5002, 5001, 1) } : new List<SyncComboPart>();
            return new MenuSnapshot(true, version,
                new List<SyncCategory> { new(701, "Cloud Mains", 1, true, "11:00", null) },
                new List<SyncStation> { new(801, "Cloud Tandoor", 1, true) },
                items, variants, addOns, combos);
        }

        private static async Task Apply(WebApplicationFactory<Program> f, MenuSnapshot s)
        {
            using var scope = f.Services.CreateScope();
            await MenuSyncApplier.Apply(scope.ServiceProvider.GetRequiredService<AppDbContext>(), s, CancellationToken.None);
        }

        private static T Read<T>(WebApplicationFactory<Program> f, Func<AppDbContext, T> read)
        {
            using var scope = f.Services.CreateScope();
            return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        [Fact]
        public async Task The_cloud_menu_is_copied_with_every_link_pointing_at_the_local_rows()
        {
            using var f = Build();
            await Apply(f, Snapshot());

            Read(f, db =>
            {
                var paneer = db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5001);
                var thali = db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5002);
                var category = db.MenuCategories.IgnoreQueryFilters().Single(x => x.CloudId == 701);
                var station = db.KitchenStations.IgnoreQueryFilters().Single(x => x.CloudId == 801);
                Assert.Equal(220.5m, paneer.Price);
                Assert.Equal(category.Id, paneer.CategoryId);
                Assert.Equal(station.Id, paneer.StationId);
                Assert.Equal(new TimeOnly(11, 0), category.AvailableFrom);
                Assert.Equal(paneer.Id, db.ItemVariants.IgnoreQueryFilters().Single(x => x.CloudId == 6001).MenuItemId);
                Assert.Equal(paneer.Id, db.ItemAddOns.IgnoreQueryFilters().Single(x => x.CloudId == 7001).MenuItemId);
                var part = db.ComboParts.IgnoreQueryFilters().Single(x => x.ComboItemId == thali.Id);
                Assert.Equal(paneer.Id, part.PartItemId);
                return 0;
            });
        }

        [Fact]
        public async Task Applying_again_adds_nothing_updates_prices_and_keeps_the_shops_sold_out_switch()
        {
            using var f = Build();
            await Apply(f, Snapshot());
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5002).IsAvailable = false;
                await db.SaveChangesAsync();
            }

            await Apply(f, Snapshot(thaliPrice: 199m, version: "v2"));
            await Apply(f, Snapshot(thaliPrice: 199m, version: "v2"));

            Read(f, db =>
            {
                Assert.Single(db.MenuItems.IgnoreQueryFilters().Where(x => x.CloudId == 5002));
                Assert.Single(db.MenuCategories.IgnoreQueryFilters().Where(x => x.CloudId == 701));
                Assert.Single(db.ItemVariants.IgnoreQueryFilters().Where(x => x.CloudId == 6001));
                Assert.Single(db.ComboParts.IgnoreQueryFilters());
                var thali = db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5002);
                Assert.Equal(199m, thali.Price);
                Assert.False(thali.IsAvailable);
                return 0;
            });
        }

        [Fact]
        public async Task Items_the_cloud_removed_and_the_shops_own_items_are_hidden_not_deleted()
        {
            using var f = Build();
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.MenuItems.Add(new MenuItem { Name = "Shop Own Item", Price = 40m });
                await db.SaveChangesAsync();
            }
            await Apply(f, Snapshot());
            await Apply(f, Snapshot(withPaneer: false, version: "v2"));

            Read(f, db =>
            {
                Assert.True(db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5001).IsDeleted);
                Assert.True(db.MenuItems.IgnoreQueryFilters().Single(x => x.Name == "Shop Own Item").IsDeleted);
                Assert.False(db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5002).IsDeleted);
                Assert.False(db.ItemAddOns.IgnoreQueryFilters().Single(x => x.CloudId == 7001).IsAvailable);
                Assert.Empty(db.ComboParts.IgnoreQueryFilters());
                return 0;
            });
        }

        [Fact]
        public async Task A_sync_applies_a_new_version_once_and_unlinking_gives_the_menu_back_to_the_shop()
        {
            var cloud = new FakeMenuCloud { Next = Snapshot() };
            using var f = Build(cloud, shopPc: true);
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LicenceRecords.Add(new LicenceRecord { Id = 1, DeviceId = "device-1", LicenceKey = "POS-AAAAA-BBBBB-CCCCC-DDDDD", LastSeenUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var sync = f.Services.GetRequiredService<MenuSyncService>();

            var first = await sync.SyncOnce(CancellationToken.None);
            Assert.True(first.ManagedByCloud);
            Assert.Equal("v1", first.Version);

            // Same version again: nothing is applied, so a change made here in between stays.
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5002).Price = 1m;
                await db.SaveChangesAsync();
            }
            await sync.SyncOnce(CancellationToken.None);
            Assert.Equal(1m, Read(f, db => db.MenuItems.IgnoreQueryFilters().Single(x => x.CloudId == 5002).Price));

            cloud.Next = MenuSyncServer.NotLinked();
            var unlinked = await sync.SyncOnce(CancellationToken.None);
            Assert.False(unlinked.ManagedByCloud);

            cloud.Next = null;
            var offline = await sync.SyncOnce(CancellationToken.None);
            Assert.Equal("offline", offline.Error);
        }
    }
}
