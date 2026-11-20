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
    // Bill pop-up, server side: what a till can get wrong when billing, taking money, voiding and adding tips.
    public class BillNegativeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public BillNegativeTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int Table, int Thali) Build(Action<OutletSettings>? change = null)
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
            var outlet = db.Outlets.Single(o => o.Id == 1);
            var settings = outlet.GetSettings();
            settings.TaxMode = TaxMode.None;
            change?.Invoke(settings);
            outlet.Settings = settings.ToJson();
            outlet.Gstin = null;
            var category = new MenuCategory { Name = "Thali", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "1", QRCodeString = "t1-1", SortOrder = 1 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var thali = new MenuItem { Name = "Gujarati Thali", Price = 199m, Category = "Thali", CategoryId = category.Id, TaxRate = 5m };
            db.MenuItems.Add(thali);
            db.SaveChanges();
            return (f, table.Id, thali.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        // Takeaway order of two thalis (398); returns the order id.
        private static async Task<int> Order(HttpClient cashier, int thali)
        {
            var res = await cashier.PostAsync("/api/orders", Json(new
            {
                orderType = "Takeaway", customerName = "Ravi", clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = thali, quantity = 2 } }
            }));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return (await Read(res)).GetProperty("orderId").GetInt32();
        }

        private static async Task<JsonElement> MakeBill(HttpClient cashier, int orderId)
        {
            var res = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            return await Read(res);
        }

        private static Task<HttpResponseMessage> Pay(HttpClient c, JsonElement bill, object extra) =>
            c.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments", Json(extra));

        [Fact]
        public async Task BadDiscounts_AreRefused()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var order = await Order(cashier, thali);
            foreach (var body in new object[]
            {
                new { discountPercent = -1m }, new { discountPercent = 100.01m }, new { discountAmount = -5m },
                new { discountAmount = 398.01m }, new { discountPercent = 10m, discountAmount = 10m },
            })
            {
                var res = await cashier.PostAsync($"/api/orders/{order}/bill", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/orders/{order}/bill")).StatusCode);

            // A full discount makes a zero bill, never a negative one.
            var free = await cashier.PostAsync($"/api/orders/{order}/bill", Json(new { discountPercent = 100m }));
            Assert.Equal(HttpStatusCode.Created, free.StatusCode);
            Assert.Equal(0m, (await Read(free)).GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task SecondBill_CancelledOrder_UnknownOrder_AndGstWithoutGstin_AreRefused()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var order = await Order(cashier, thali);
            var bill = await MakeBill(cashier, order);
            var again = await cashier.PostAsync($"/api/orders/{order}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(bill.GetProperty("id").GetInt32(), (await Read(again)).GetProperty("billId").GetInt32());

            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/orders/99999/bill", Json(new { }))).StatusCode);

            var other = await Order(cashier, thali);
            var version = (await Read(await cashier.GetAsync($"/api/orders/{other}"))).GetProperty("version").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync($"/api/orders/{other}/cancel", Json(new { reason = "Left", version }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{other}/bill", Json(new { }))).StatusCode);

            var (g, _, gThali) = Build(s => s.TaxMode = TaxMode.Regular);
            using var gst = g;
            var gCashier = As(g, "cashier");
            var gOrder = await Order(gCashier, gThali);
            Assert.Equal(HttpStatusCode.BadRequest, (await gCashier.PostAsync($"/api/orders/{gOrder}/bill", Json(new { }))).StatusCode);
        }

        [Fact]
        public async Task BadPayments_AreRefused_AndNothingIsRecorded()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await MakeBill(cashier, await Order(cashier, thali));
            var v = bill.GetProperty("version").GetInt32();
            foreach (var body in new object[]
            {
                new { method = "Cash", amount = 0m, version = v },
                new { method = "Cash", amount = -10m, version = v },
                new { method = "Cash", amount = 0.001m, version = v },
                new { method = "Cash", amount = 398.01m, version = v },
                new { method = "Upi", amount = 500m, version = v },
                new { method = "Cheque", amount = 100m, version = v },
                new { method = "0", amount = 100m, version = v },
                new { method = "", amount = 100m, version = v },
                new { method = "Cash", amount = 398m, tendered = 300m, version = v },
                new { method = "Upi", amount = 100m, reference = new string('r', 41), version = v },
                new { method = "Waived", amount = 3m, version = v },
            })
            {
                var res = await Pay(cashier, bill, body);
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.Conflict, (await Pay(cashier, bill, new { method = "Cash", amount = 100m, version = v + 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/bills/99999/payments", Json(new { method = "Cash", amount = 100m, version = 1 }))).StatusCode);

            var now = await Read(await cashier.GetAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}"));
            Assert.Equal(0m, now.GetProperty("amountPaid").GetDecimal());
            Assert.Equal(v, now.GetProperty("version").GetInt32());
        }

        [Fact]
        public async Task PaidBill_TakesNoMoreMoney_AndCashGivesChange()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await MakeBill(cashier, await Order(cashier, thali));
            var paid = await Pay(cashier, bill, new { method = "Cash", amount = 398m, tendered = 500m, version = bill.GetProperty("version").GetInt32() });
            Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
            var body = await Read(paid);
            Assert.Equal("Paid", body.GetProperty("status").GetString());
            Assert.Equal(102m, body.GetProperty("payments")[0].GetProperty("change").GetDecimal());
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, body, new { method = "Cash", amount = 1m, version = body.GetProperty("version").GetInt32() })).StatusCode);
        }

        [Fact]
        public async Task Void_NeedsAReason_TheRightVersion_AManager_AndHappensOnce()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var manager = As(f, "manager");
            var order = await Order(cashier, thali);
            var bill = await MakeBill(cashier, order);
            var id = bill.GetProperty("id").GetInt32();
            var v = bill.GetProperty("version").GetInt32();

            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{id}/void", Json(new { reason = " ", version = v }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{id}/void", Json(new { reason = new string('x', 201), version = v }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync($"/api/bills/{id}/void", Json(new { reason = "Wrong items", version = v + 1 }))).StatusCode);
            var cashierVoid = await cashier.PostAsync($"/api/bills/{id}/void", Json(new { reason = "Wrong items", version = v }));
            Assert.True((int)cashierVoid.StatusCode is >= 400 and < 500, $"cashier void without PIN gave {cashierVoid.StatusCode}");
            var wrongPin = await cashier.PostAsync($"/api/bills/{id}/void", Json(new { reason = "Wrong items", version = v, approverPin = "0000" }));
            Assert.True((int)wrongPin.StatusCode is >= 400 and < 500, $"cashier void with a wrong PIN gave {wrongPin.StatusCode}");

            var voided = await manager.PostAsync($"/api/bills/{id}/void", Json(new { reason = "Wrong items", version = v }));
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
            var after = await Read(voided);
            Assert.Equal("Void", after.GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{id}/void", Json(new { reason = "Again", version = after.GetProperty("version").GetInt32() }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Pay(cashier, after, new { method = "Cash", amount = 10m, version = after.GetProperty("version").GetInt32() })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/bills/{id}/tips", Json(new { method = "Cash", amount = 20m }))).StatusCode);

            // After a void the order can be billed again, with a new number.
            var fresh = await MakeBill(cashier, order);
            Assert.NotEqual(bill.GetProperty("invoiceNumber").GetString(), fresh.GetProperty("invoiceNumber").GetString());
        }

        [Fact]
        public async Task BadTips_AreRefused()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await MakeBill(cashier, await Order(cashier, thali));
            var id = bill.GetProperty("id").GetInt32();
            foreach (var body in new object[]
            {
                new { method = "Cash", amount = 0m }, new { method = "Cash", amount = -5m }, new { method = "Cash", amount = 100000.01m },
                new { method = "Waived", amount = 5m }, new { method = "Bitcoin", amount = 5m },
                new { method = "Cash", amount = 5m, waiterName = new string('w', 101) },
            })
            {
                var res = await cashier.PostAsync($"/api/bills/{id}/tips", Json(body));
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} gave {res.StatusCode}");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/bills/99999/tips", Json(new { method = "Cash", amount = 5m }))).StatusCode);
        }

        [Fact]
        public async Task BillsList_BadDate_UnknownBill_OtherRestaurant_AndWaiter()
        {
            var (f, _, thali) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var bill = await MakeBill(cashier, await Order(cashier, thali));
            var id = bill.GetProperty("id").GetInt32();
            var v = bill.GetProperty("version").GetInt32();
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync("/api/bills?date=yesterday")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/bills/99999")).StatusCode);

            var other = As(f, "manager", "2");
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/bills/{id}")).StatusCode);
            var otherList = await other.GetAsync("/api/bills");
            Assert.DoesNotContain(bill.GetProperty("invoiceNumber").GetString()!, await otherList.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/bills/{id}/payments", Json(new { method = "Cash", amount = 10m, version = v }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/bills/{id}/void", Json(new { reason = "x", version = v }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/bills/{id}/tips", Json(new { method = "Cash", amount = 5m }))).StatusCode);

            var waiter = As(f, "waiter");
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync("/api/bills")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await waiter.PostAsync($"/api/bills/{id}/payments", Json(new { method = "Cash", amount = 10m, version = v }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").GetAsync($"/api/bills/{id}")).StatusCode);

            var mine = await Read(await cashier.GetAsync($"/api/bills/{id}"));
            Assert.Equal("Unpaid", mine.GetProperty("status").GetString());
            Assert.Equal(0m, mine.GetProperty("amountPaid").GetDecimal());
        }
    }
}
