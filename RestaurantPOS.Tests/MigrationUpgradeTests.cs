using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using POSSystem.Data;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Upgrades a database that already has orders, the way a live restaurant would be upgraded.
    public class MigrationUpgradeTests : IClassFixture<PostgresFixture>
    {
        private const string BeforeBatchB = "20261114082124_AddStaffFields";
        private readonly PostgresFixture _pg;

        public MigrationUpgradeTests(PostgresFixture pg)
        {
            _pg = pg;
        }

        private bool DockerMissing()
        {
            if (_pg.Container != null) return false;
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Docker is required in CI: " + _pg.DockerError);
            return true;
        }

        [Fact]
        public async Task Upgrade_KeepsExistingOrders_AndAddsRestaurantOne()
        {
            if (DockerMissing()) return;
            var cs = new NpgsqlConnectionStringBuilder(_pg.Container!.GetConnectionString()) { Database = "upgrade_" + Guid.NewGuid().ToString("N") }.ConnectionString;
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;

            await using (var db = new AppDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(BeforeBatchB);
                await db.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""RestaurantTables"" (""TableNumber"", ""QRCodeString"") VALUES ('1', 'table-1-qr');
                    INSERT INTO ""MenuItems"" (""Name"", ""Price"", ""Category"") VALUES ('Masala Tea', 20, 'Drinks');
                    INSERT INTO ""KitchenOrders"" (""TableId"", ""Status"", ""TokenNumber"") VALUES (1, 'Received', 101);
                    INSERT INTO ""OrderItems"" (""KitchenOrderId"", ""MenuItemId"", ""ItemName"", ""UnitPrice"", ""Quantity"") VALUES (1, 1, 'Masala Tea', 20, 2);");

                await migrator.MigrateAsync();
            }

            await using var check = new AppDbContext(options);
            var order = await check.KitchenOrders.IgnoreQueryFilters().Include(o => o.Items).SingleAsync();
            Assert.Equal(101, order.TokenNumber);
            Assert.Equal(1, order.TenantId);
            Assert.Equal(1, order.OutletId);
            Assert.Single(order.Items);
            Assert.Equal(1, order.Items[0].TenantId);
            Assert.Equal(20.00m, order.Items[0].UnitPrice);

            var outlet = await check.Outlets.SingleAsync();
            Assert.Equal(1, outlet.TenantId);
            Assert.Equal("Asia/Kolkata", outlet.TimeZone);
            Assert.Equal(4, outlet.BusinessDayStartHour);
            Assert.Equal("My Restaurant", (await check.Tenants.SingleAsync()).Name);

            // New restaurants get new ids; the seeded id 1 must not clash.
            check.Tenants.Add(new Tenant { Name = "Second restaurant" });
            await check.SaveChangesAsync();
            Assert.True(await check.Tenants.AnyAsync(t => t.Id >= 2 && t.Name == "Second restaurant"));
        }

        [Fact]
        public async Task DeletingATable_WithOrders_IsRefused()
        {
            if (DockerMissing()) return;
            var cs = new NpgsqlConnectionStringBuilder(_pg.Container!.GetConnectionString()) { Database = "restrict_" + Guid.NewGuid().ToString("N") }.ConnectionString;
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;

            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            var table = new RestaurantTable { TableNumber = "7", QRCodeString = "table-7-qr" };
            db.RestaurantTables.Add(table);
            await db.SaveChangesAsync();
            db.KitchenOrders.Add(new KitchenOrder { TableId = table.Id, TokenNumber = 1, Status = OrderStatus.Received });
            await db.SaveChangesAsync();

            // Ask the database itself (nothing else tracked), as a delete from another screen would.
            db.ChangeTracker.Clear();
            db.RestaurantTables.Remove((await db.RestaurantTables.FindAsync(table.Id))!);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
