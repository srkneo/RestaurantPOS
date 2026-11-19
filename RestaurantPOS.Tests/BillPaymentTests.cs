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
using POSSystem.Billing;
using POSSystem.Data;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class BillPaymentTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public BillPaymentTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int PaneerId, int ColaId);

        // Restaurant 1 with a table and two items: Paneer Tikka 220 (5% GST) and Cola 60 (18% GST).
        private Setup Build(TaxMode mode, bool withGstin = true, bool pricesIncludeTax = false, string? upiId = "anandthali@okaxis")
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
            settings.TaxMode = mode;
            settings.PricesIncludeTax = pricesIncludeTax;
            settings.UpiId = upiId;
            settings.UpiName = "Anand Thali";
            outlet.Settings = settings.ToJson();
            outlet.Gstin = withGstin ? "24ABCDE1234F1Z5" : null;
            outlet.Address = "Station Road, Anand";
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

        private static async Task<int> PlaceOrder(Setup s, HttpClient client)
        {
            var response = await client.PostAsync("/api/orders", Json(new
            {
                table = s.TableId,
                items = new object[] { new { menuItemId = s.PaneerId, quantity = 2 }, new { menuItemId = s.ColaId, quantity = 1 } },
                clientOrderId = Guid.NewGuid()
            }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Read(response)).GetProperty("orderId").GetInt32();
        }

        // Order of 2 x Paneer Tikka 220 + Cola 60 = 500, no GST: bill total 500.
        private async Task<(HttpClient Cashier, int BillId)> Billed(Setup s)
        {
            var cashier = As(s.Factory, "cashier");
            var orderId = await PlaceOrder(s, cashier);
            var bill = await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { })));
            Assert.Equal(500m, bill.GetProperty("total").GetDecimal());
            return (cashier, bill.GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task PayingTheBillOfAReadyOrder_ClosesIt_AndFreesTheTable()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var cashier = As(f, "cashier");

            // Still cooking when paid: stays open.
            var cooking = await PlaceOrder(s, cashier);
            var cookingBill = await Read(await cashier.PostAsync($"/api/orders/{cooking}/bill", Json(new { })));
            var paid = await cashier.PostAsync($"/api/bills/{cookingBill.GetProperty("id").GetInt32()}/payments", Json(new { method = "cash", amount = 500, version = 1 }));
            Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
            Assert.Equal("Received", (await Read(await cashier.GetAsync($"/api/orders/{cooking}"))).GetProperty("status").GetString());

            // Ready, then paid: closed and the table is free.
            var ready = await As(f, "admin").PutAsync($"/api/orders/{cooking}/status", Json(new { status = "Ready" }));
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            var order = await Read(await cashier.GetAsync($"/api/orders/{cooking}"));
            Assert.Equal("Ready", order.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.String, order.GetProperty("readyAt").ValueKind);
            var served = await As(f, "admin").PutAsync($"/api/orders/{cooking}/status", Json(new { status = "Completed" }));
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);

            var second = await PlaceOrder(s, cashier);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "admin").PutAsync($"/api/orders/{second}/status", Json(new { status = "Ready" }))).StatusCode);
            var bill = await Read(await cashier.PostAsync($"/api/orders/{second}/bill", Json(new { })));
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments", Json(new { method = "cash", amount = 500, version = 1 }))).StatusCode);
            Assert.Equal("Completed", (await Read(await cashier.GetAsync($"/api/orders/{second}"))).GetProperty("status").GetString());
            var table = (await Read(await cashier.GetAsync("/api/tables"))).EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == s.TableId);
            Assert.Equal("Free", table.GetProperty("status").GetString());
        }

        [Fact]
        public async Task ServedDineIn_KeepsTheTable_UntilTheBillIsPaid()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var admin = As(f, "admin");
            JsonElement Table(JsonElement list) => list.EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == s.TableId);

            // Served, then paid: the table frees only with the payment.
            var first = await PlaceOrder(s, cashier);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/orders/{first}/status", Json(new { status = "Ready" }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/orders/{first}/status", Json(new { status = "Served" }))).StatusCode);
            Assert.Equal("Served", (await Read(await cashier.GetAsync($"/api/orders/{first}"))).GetProperty("status").GetString());
            Assert.Equal("Occupied", Table(await Read(await cashier.GetAsync("/api/tables"))).GetProperty("status").GetString());
            var bill = await Read(await cashier.PostAsync($"/api/orders/{first}/bill", Json(new { })));
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync($"/api/bills/{bill.GetProperty("id").GetInt32()}/payments", Json(new { method = "cash", amount = 500, version = 1 }))).StatusCode);
            Assert.Equal("Completed", (await Read(await cashier.GetAsync($"/api/orders/{first}"))).GetProperty("status").GetString());
            Assert.Equal("Free", Table(await Read(await cashier.GetAsync("/api/tables"))).GetProperty("status").GetString());

            // Paid while cooking, then served: closes on Served.
            var second = await PlaceOrder(s, cashier);
            var early = await Read(await cashier.PostAsync($"/api/orders/{second}/bill", Json(new { })));
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync($"/api/bills/{early.GetProperty("id").GetInt32()}/payments", Json(new { method = "cash", amount = 500, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/orders/{second}/status", Json(new { status = "Ready" }))).StatusCode);
            Assert.Equal("Occupied", Table(await Read(await cashier.GetAsync("/api/tables"))).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/orders/{second}/status", Json(new { status = "Served" }))).StatusCode);
            Assert.Equal("Completed", (await Read(await cashier.GetAsync($"/api/orders/{second}"))).GetProperty("status").GetString());
            Assert.Equal("Free", Table(await Read(await cashier.GetAsync("/api/tables"))).GetProperty("status").GetString());
        }

        [Fact]
        public async Task Cash_WithChange_PaysTheBill()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var (cashier, billId) = await Billed(s);

            var response = await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "cash", amount = 500, tendered = 600, version = 1 }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bill = await Read(response);

            Assert.Equal("Paid", bill.GetProperty("status").GetString());
            Assert.Equal(500m, bill.GetProperty("amountPaid").GetDecimal());
            Assert.Equal(0m, bill.GetProperty("amountDue").GetDecimal());
            Assert.Equal(JsonValueKind.String, bill.GetProperty("paidAt").ValueKind);
            var payment = bill.GetProperty("payments")[0];
            Assert.Equal("Cash", payment.GetProperty("method").GetString());
            Assert.Equal(600m, payment.GetProperty("tendered").GetDecimal());
            Assert.Equal(100m, payment.GetProperty("change").GetDecimal());

            var again = await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "cash", amount = 1, version = 2 }));
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
            Assert.Contains("already paid", await again.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task SplitPayment_UpiThenCard_PaysTheBill_WhenTheyReachTheTotal()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var (cashier, billId) = await Billed(s);

            var first = await Read(await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "UPI", amount = 300, reference = "UTR 4521", version = 1 })));
            Assert.Equal("Unpaid", first.GetProperty("status").GetString());
            Assert.Equal(200m, first.GetProperty("amountDue").GetDecimal());

            var tooMuch = await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "card", amount = 250, version = 2 }));
            Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
            Assert.Contains("200.00", await tooMuch.Content.ReadAsStringAsync());

            var second = await Read(await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "card", amount = 200, version = 2 })));
            Assert.Equal("Paid", second.GetProperty("status").GetString());
            Assert.Equal(2, second.GetProperty("payments").GetArrayLength());
            Assert.Equal("UTR 4521", second.GetProperty("payments")[0].GetProperty("reference").GetString());

            using var list = JsonDocument.Parse(await cashier.GetStringAsync("/api/bills"));
            Assert.Equal("Paid", list.RootElement[0].GetProperty("status").GetString());
        }

        [Fact]
        public async Task BadPayments_AreRefused()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var (cashier, billId) = await Billed(s);
            var url = $"/api/bills/{billId}/payments";

            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync(url, Json(new { method = "cheque", amount = 100, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync(url, Json(new { method = "1", amount = 100, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync(url, Json(new { method = "cash", amount = 0, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync(url, Json(new { method = "cash", amount = 500, tendered = 400, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync(url, Json(new { method = "upi", amount = 100, reference = new string('x', 41), version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsync(url, Json(new { method = "cash", amount = 100, version = 7 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync(url, Json(new { method = "cash", amount = 100, version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "cashier", "2").PostAsync(url, Json(new { method = "cash", amount = 100, version = 1 }))).StatusCode);

            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync($"/api/bills/{billId}/void", Json(new { reason = "Test", version = 1 }))).StatusCode);
            var onVoid = await cashier.PostAsync(url, Json(new { method = "cash", amount = 100, version = 2 }));
            Assert.Equal(HttpStatusCode.BadRequest, onVoid.StatusCode);
            Assert.Contains("void", await onVoid.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task UpiLink_AndQrCode_AreForTheAmountStillDue()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var (cashier, billId) = await Billed(s);
            await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "cash", amount = 100.5, version = 1 }));

            var upi = await Read(await cashier.GetAsync($"/api/bills/{billId}/upi"));
            var link = upi.GetProperty("link").GetString()!;
            Assert.StartsWith("upi://pay?pa=anandthali@okaxis&pn=Anand%20Thali&am=399.50&cu=INR&tn=INV%2F", link);
            Assert.Equal(399.50m, upi.GetProperty("amount").GetDecimal());

            var qr = await cashier.GetAsync($"/api/bills/{billId}/upi-qr.svg");
            Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
            Assert.Equal("image/svg+xml", qr.Content.Headers.ContentType!.MediaType);
            Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").GetAsync($"/api/bills/{billId}/upi")).StatusCode);
        }

        [Fact]
        public async Task UpiLink_NeedsAUpiId()
        {
            var s = Build(TaxMode.None, withGstin: false, upiId: null);
            using var f = s.Factory;
            var (cashier, billId) = await Billed(s);

            var response = await cashier.GetAsync($"/api/bills/{billId}/upi");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("UPI ID", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public void UpiLink_EncodesNameAndNote()
        {
            Assert.Equal("upi://pay?pa=a.b-c@ybl&pn=Shree%20Krishna%20%26%20Sons&am=1250.00&cu=INR&tn=AN1%2F26-27%2F000007",
                BillService.BuildUpiLink("a.b-c@ybl", "Shree Krishna & Sons", 1250m, "AN1/26-27/000007"));
        }

        [Fact]
        public async Task BillingSettings_KeepTheUpiId_AndCheckIt()
        {
            var s = Build(TaxMode.None, withGstin: false, upiId: null);
            using var f = s.Factory;
            var admin = As(f, "admin");

            var bad = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", upiId = "not a upi id" }));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("upiid", await bad.Content.ReadAsStringAsync());

            var saved = await Read(await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "None", pricesIncludeTax = false, billPrefix = "INV", upiId = " anandthali@okaxis ", upiName = "Anand Thali" })));
            Assert.Equal("anandthali@okaxis", saved.GetProperty("upiId").GetString());
            Assert.Equal("Anand Thali", (await Read(await admin.GetAsync("/api/admin/billing-settings"))).GetProperty("upiName").GetString());
        }
    }
}
