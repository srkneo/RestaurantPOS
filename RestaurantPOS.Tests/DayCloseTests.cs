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
    public class DayCloseTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DayCloseTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int PaneerId, int ColaId);

        // Regular GST restaurant: Paneer Tikka 220 (5%) and Cola 60 (18%). 2 x Paneer + 1 Cola = bill of 533 (500 + 32.80 GST, round off 0.20).
        private Setup Build()
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
            settings.TaxMode = TaxMode.Regular;
            outlet.Settings = settings.ToJson();
            outlet.Gstin = "24ABCDE1234F1Z5";
            var category = new MenuCategory { Name = "Mains", SortOrder = 1 };
            db.MenuCategories.Add(category);
            var table = new RestaurantTable { TableNumber = "5", QRCodeString = "t1-5", SortOrder = 5 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var paneer = new MenuItem { Name = "Paneer Tikka", Price = 220m, Category = "Mains", CategoryId = category.Id, TaxRate = 5m };
            var cola = new MenuItem { Name = "Cola", Price = 60m, Category = "Mains", CategoryId = category.Id, TaxRate = 18m };
            db.MenuItems.AddRange(paneer, cola);
            db.SaveChanges();
            return new Setup(f, table.Id, paneer.Id, cola.Id);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role)
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "1");
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        // Places 2 x Paneer + 1 Cola and bills it: returns the bill id (total 533).
        private static async Task<int> BilledOrder(Setup s, HttpClient client)
        {
            var order = await client.PostAsync("/api/orders", Json(new
            {
                table = s.TableId,
                items = new object[] { new { menuItemId = s.PaneerId, quantity = 2 }, new { menuItemId = s.ColaId, quantity = 1 } },
                clientOrderId = Guid.NewGuid()
            }));
            Assert.Equal(HttpStatusCode.OK, order.StatusCode);
            var orderId = (await Read(order)).GetProperty("orderId").GetInt32();
            var bill = await client.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            var json = await Read(bill);
            Assert.Equal(533m, json.GetProperty("total").GetDecimal());
            return json.GetProperty("id").GetInt32();
        }

        private static async Task Pay(HttpClient client, int billId, string method)
        {
            var response = await client.PostAsync($"/api/bills/{billId}/payments", Json(new { method, amount = 533, version = 1 }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Shift_CountsCashSalesAndMovements_AndShowsShortOnClose()
        {
            var s = Build();
            using var f = s.Factory;
            var cashier = As(f, "cashier");

            var none = await Read(await cashier.GetAsync("/api/shifts/current"));
            Assert.Equal(JsonValueKind.Null, none.GetProperty("shift").ValueKind);

            var opened = await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 1000 }));
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            var shift = await Read(opened);
            Assert.Equal("Open", shift.GetProperty("status").GetString());
            Assert.Equal(1000m, shift.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 0 }))).StatusCode);

            await Pay(cashier, await BilledOrder(s, cashier), "cash");
            await Pay(cashier, await BilledOrder(s, cashier), "upi");
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "out", amount = 200, reason = "Milk vendor" }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "In", amount = 100, reason = "Change from bank" }))).StatusCode);

            var current = (await Read(await cashier.GetAsync("/api/shifts/current"))).GetProperty("shift");
            Assert.Equal(533m, current.GetProperty("cashSales").GetDecimal());
            Assert.Equal(100m, current.GetProperty("cashIn").GetDecimal());
            Assert.Equal(200m, current.GetProperty("cashOut").GetDecimal());
            Assert.Equal(1433m, current.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(2, current.GetProperty("movements").GetArrayLength());
            var version = current.GetProperty("version").GetInt32();
            Assert.Equal(3, version);

            var stale = await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 1400, version = 1 }));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var closed = await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 1400, note = "Short, will check", version }));
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            var result = await Read(closed);
            Assert.Equal("Closed", result.GetProperty("status").GetString());
            Assert.Equal(1433m, result.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(1400m, result.GetProperty("countedCash").GetDecimal());
            Assert.Equal(-33m, result.GetProperty("difference").GetDecimal());
            Assert.Equal("Short, will check", result.GetProperty("closeNote").GetString());

            Assert.Equal(JsonValueKind.Null, (await Read(await cashier.GetAsync("/api/shifts/current"))).GetProperty("shift").ValueKind);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/shifts")).StatusCode);
            var list = await Read(await As(f, "manager").GetAsync("/api/shifts"));
            Assert.Equal(1, list.GetArrayLength());
            Assert.Equal(-33m, list[0].GetProperty("difference").GetDecimal());
        }

        [Fact]
        public async Task Shift_RejectsBadInput()
        {
            var s = Build();
            using var f = s.Factory;
            var cashier = As(f, "cashier");

            var noShift = await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "Out", amount = 50, reason = "Tea" }));
            Assert.Equal(HttpStatusCode.BadRequest, noShift.StatusCode);
            Assert.Contains("Open a shift first", await noShift.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/close", Json(new { countedCash = 0, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = -5 }))).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 500 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "sideways", amount = 50, reason = "Tea" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "1", amount = 50, reason = "Tea" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "Out", amount = 0, reason = "Tea" }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/shifts/current/movements", Json(new { kind = "Out", amount = 50, reason = "  " }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync("/api/shifts/open", Json(new { openingCash = 0 }))).StatusCode);
        }

        [Fact]
        public async Task DayReport_TotalsGstPaymentsVoidsAndShifts()
        {
            var s = Build();
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 0 }))).StatusCode);

            await Pay(cashier, await BilledOrder(s, cashier), "cash");
            await Pay(cashier, await BilledOrder(s, cashier), "upi");
            await BilledOrder(s, cashier);                                  // left unpaid
            var refunded = await BilledOrder(s, cashier);
            await Pay(cashier, refunded, "cash");
            var voided = await manager.PostAsync($"/api/bills/{refunded}/void", Json(new { reason = "Customer left, money returned", version = 2 }));
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/reports/day")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/reports/day?date=15-11-2026")).StatusCode);

            var response = await manager.GetAsync("/api/reports/day");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var r = await Read(response);

            Assert.Equal("24ABCDE1234F1Z5", r.GetProperty("gstin").GetString());
            Assert.EndsWith("/000001", r.GetProperty("firstInvoice").GetString());
            Assert.EndsWith("/000004", r.GetProperty("lastInvoice").GetString());
            Assert.Equal(3, r.GetProperty("billCount").GetInt32());
            Assert.Equal(1500m, r.GetProperty("itemsTotal").GetDecimal());
            Assert.Equal(1500m, r.GetProperty("taxableValue").GetDecimal());
            Assert.Equal(49.20m, r.GetProperty("cgst").GetDecimal());
            Assert.Equal(49.20m, r.GetProperty("sgst").GetDecimal());
            Assert.Equal(0.60m, r.GetProperty("roundOff").GetDecimal());
            Assert.Equal(1599m, r.GetProperty("total").GetDecimal());

            var payments = r.GetProperty("payments").EnumerateArray().ToDictionary(p => p.GetProperty("method").GetString()!, p => p);
            Assert.Equal(533m, payments["Cash"].GetProperty("amount").GetDecimal());
            Assert.Equal(1, payments["Cash"].GetProperty("count").GetInt32());
            Assert.Equal(533m, payments["Upi"].GetProperty("amount").GetDecimal());
            Assert.Equal(0m, payments["Card"].GetProperty("amount").GetDecimal());
            Assert.Equal(1066m, r.GetProperty("amountReceived").GetDecimal());
            Assert.Equal(1, r.GetProperty("unpaidCount").GetInt32());
            Assert.Equal(533m, r.GetProperty("amountDue").GetDecimal());

            var tax = r.GetProperty("taxSummary");
            Assert.Equal(2, tax.GetArrayLength());
            Assert.Equal(5m, tax[0].GetProperty("taxRate").GetDecimal());
            Assert.Equal(1320m, tax[0].GetProperty("taxableValue").GetDecimal());
            Assert.Equal(33m, tax[0].GetProperty("cgst").GetDecimal());
            Assert.Equal(18m, tax[1].GetProperty("taxRate").GetDecimal());
            Assert.Equal(16.20m, tax[1].GetProperty("sgst").GetDecimal());

            var items = r.GetProperty("items");
            Assert.Equal("Paneer Tikka", items[0].GetProperty("itemName").GetString());
            Assert.Equal(6, items[0].GetProperty("quantity").GetInt32());
            Assert.Equal(1320m, items[0].GetProperty("amount").GetDecimal());
            Assert.Equal(3, items[1].GetProperty("quantity").GetInt32());

            Assert.Equal(1, r.GetProperty("voidCount").GetInt32());
            Assert.Equal(533m, r.GetProperty("voidTotal").GetDecimal());
            Assert.Equal("Customer left, money returned", r.GetProperty("voidedBills")[0].GetProperty("reason").GetString());

            // The voided bill's cash went back to the customer, so the drawer expects only the first cash bill.
            var shifts = r.GetProperty("shifts");
            Assert.Equal(1, shifts.GetArrayLength());
            Assert.Equal(533m, shifts[0].GetProperty("cashSales").GetDecimal());
            Assert.Equal(533m, shifts[0].GetProperty("expectedCash").GetDecimal());
        }
    }
}
