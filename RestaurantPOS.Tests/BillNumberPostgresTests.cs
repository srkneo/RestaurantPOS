using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using POSSystem.Billing;
using POSSystem.Data;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Real PostgreSQL: many tills billing at the same moment still get invoice numbers 1, 2, 3 ... with no gaps or repeats.
    [Collection("Postgres")]   // one real-database test class at a time, so Docker is not overloaded
    public class BillNumberPostgresTests : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly PostgresFixture _pg;
        private readonly WebApplicationFactory<Program> _factory;

        public BillNumberPostgresTests(PostgresFixture pg, WebApplicationFactory<Program> factory)
        {
            _pg = pg;
            _factory = factory;
        }

        [Fact]
        public async Task ConcurrentBills_GetConsecutiveUniqueNumbers()
        {
            if (_pg.Container == null)
            {
                Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")), "Docker is required in CI: " + _pg.DockerError);
                return;
            }

            var cs = new NpgsqlConnectionStringBuilder(_pg.Container.GetConnectionString()) { Database = "pos_" + Guid.NewGuid().ToString("N") }.ConnectionString;
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));
                });
            });

            const int count = 12;
            int[] orderIds;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.False(db.Database.HasPendingModelChanges(), "The model has changes that are not in a migration.");
                var item = await db.MenuItems.FirstAsync();
                var table = await db.RestaurantTables.FirstAsync();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var orders = Enumerable.Range(1, count).Select(i => new KitchenOrder
                {
                    TableId = table.Id,
                    TokenNumber = 1000 + i,
                    BusinessDate = today,
                    Items = { new OrderItem { MenuItemId = item.Id, ItemName = item.Name, UnitPrice = item.Price, Quantity = 1 } }
                }).ToList();
                db.KitchenOrders.AddRange(orders);
                await db.SaveChangesAsync();
                orderIds = orders.Select(o => o.Id).ToArray();
            }

            var bills = await Task.WhenAll(orderIds.Select(id => Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<BillService>();
                return await service.CreateBill(id, new CreateBillRequest(null, null));
            })));

            Assert.Equal(Enumerable.Range(1, count), bills.Select(b => b.Sequence).OrderBy(n => n));
            Assert.Equal(count, bills.Select(b => b.InvoiceNumber).Distinct().Count());

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(count, await db.BillCounters.Select(c => c.LastNumber).SingleAsync());
            }
        }
    }
}
