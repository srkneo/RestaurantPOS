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
    public class BillApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public BillApiTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int PaneerId, int ColaId);

        // Restaurant 1 with a table and two items: Paneer Tikka 220 (5% GST) and Cola 60 (18% GST).
        private Setup Build(TaxMode mode, bool withGstin = true, bool pricesIncludeTax = false)
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

        [Fact]
        public async Task GstBill_HasInvoiceNumber_TaxPerRate_AndRoundedTotal()
        {
            var s = Build(TaxMode.Regular);
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var orderId = await PlaceOrder(s, cashier);

            var response = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var bill = await Read(response);

            Assert.Matches(@"^INV/\d\d-\d\d/000001$", bill.GetProperty("invoiceNumber").GetString());
            Assert.Equal("Tax Invoice", bill.GetProperty("title").GetString());
            Assert.Equal("Unpaid", bill.GetProperty("status").GetString());
            Assert.Equal("Table 5", bill.GetProperty("orderLabel").GetString());
            Assert.Equal(500m, bill.GetProperty("taxableValue").GetDecimal());
            Assert.Equal(16.40m, bill.GetProperty("cgst").GetDecimal());
            Assert.Equal(16.40m, bill.GetProperty("sgst").GetDecimal());
            Assert.Equal(0.20m, bill.GetProperty("roundOff").GetDecimal());
            Assert.Equal(533m, bill.GetProperty("total").GetDecimal());
            Assert.Equal("24ABCDE1234F1Z5", bill.GetProperty("store").GetProperty("gstin").GetString());
            Assert.Equal(2, bill.GetProperty("lines").GetArrayLength());
            var summary = bill.GetProperty("taxSummary");
            Assert.Equal(2, summary.GetArrayLength());
            Assert.Equal(5m, summary[0].GetProperty("taxRate").GetDecimal());
            Assert.Equal(440m, summary[0].GetProperty("taxableValue").GetDecimal());
            Assert.Equal(18m, summary[1].GetProperty("taxRate").GetDecimal());
        }

        [Fact]
        public async Task CompositionBill_IsABillOfSupply_WithTheRequiredLine_AndNoTax()
        {
            var s = Build(TaxMode.Composition);
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var orderId = await PlaceOrder(s, cashier);

            var bill = await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountPercent = 10 })));

            Assert.Equal("Bill of Supply", bill.GetProperty("title").GetString());
            Assert.Equal(BillService.CompositionNote, bill.GetProperty("taxNote").GetString());
            Assert.Equal(50m, bill.GetProperty("discount").GetDecimal());
            Assert.Equal(0m, bill.GetProperty("cgst").GetDecimal());
            Assert.Equal(450m, bill.GetProperty("total").GetDecimal());
            Assert.Equal(0, bill.GetProperty("taxSummary").GetArrayLength());
        }

        [Fact]
        public async Task GstModes_NeedAGstin()
        {
            var s = Build(TaxMode.Regular, withGstin: false);
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var orderId = await PlaceOrder(s, cashier);

            var response = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("GSTIN", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task OneBillPerOrder_TheOrderIsLocked_AndVoidAllowsANewBillWithTheNextNumber()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var orderId = await PlaceOrder(s, cashier);
            var first = await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { })));
            var firstId = first.GetProperty("id").GetInt32();
            Assert.Equal("Bill", first.GetProperty("title").GetString());

            var again = await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            var againBody = await Read(again);
            Assert.Equal("already-billed", againBody.GetProperty("code").GetString());
            Assert.Equal(firstId, againBody.GetProperty("billId").GetInt32());

            using var order = JsonDocument.Parse(await cashier.GetStringAsync($"/api/orders/{orderId}"));
            var version = order.RootElement.GetProperty("version").GetInt32();
            var edit = await cashier.PostAsync($"/api/orders/{orderId}/items", Json(new { version, items = new[] { new { menuItemId = s.ColaId, quantity = 1 } } }));
            Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
            Assert.Equal("billed", (await Read(edit)).GetProperty("code").GetString());

            var cashierVoid = await cashier.PostAsync($"/api/bills/{firstId}/void", Json(new { reason = "Wrong table", version = 1 }));
            Assert.Equal(HttpStatusCode.Forbidden, cashierVoid.StatusCode);

            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{firstId}/void", Json(new { reason = " ", version = 1 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync($"/api/bills/{firstId}/void", Json(new { reason = "Wrong table", version = 9 }))).StatusCode);
            var voided = await Read(await manager.PostAsync($"/api/bills/{firstId}/void", Json(new { reason = "Wrong table", version = 1 })));
            Assert.Equal("Void", voided.GetProperty("status").GetString());
            Assert.Equal("Wrong table", voided.GetProperty("voidReason").GetString());

            var second = await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountAmount = 40 })));
            Assert.EndsWith("/000002", second.GetProperty("invoiceNumber").GetString());
            Assert.Equal(460m, second.GetProperty("total").GetDecimal());

            using var list = JsonDocument.Parse(await cashier.GetStringAsync("/api/bills"));
            Assert.Equal(2, list.RootElement.GetArrayLength());
            Assert.Equal("Unpaid", list.RootElement[0].GetProperty("status").GetString());
            Assert.Equal("Void", list.RootElement[1].GetProperty("status").GetString());

            var current = await Read(await cashier.GetAsync($"/api/orders/{orderId}/bill"));
            Assert.Equal(second.GetProperty("id").GetInt32(), current.GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task BadDiscounts_CancelledOrders_KitchenAndOtherRestaurants_AreRefused()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var orderId = await PlaceOrder(s, cashier);

            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountPercent = 120 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountAmount = 501 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { discountPercent = 5, discountAmount = 5 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").PostAsync($"/api/orders/{orderId}/bill", Json(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.PostAsync("/api/orders/9999/bill", Json(new { }))).StatusCode);

            var billId = (await Read(await cashier.PostAsync($"/api/orders/{orderId}/bill", Json(new { })))).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.NotFound, (await As(f, "admin", "2").GetAsync($"/api/bills/{billId}")).StatusCode);

            var other = await PlaceOrder(s, cashier);
            using var o = JsonDocument.Parse(await cashier.GetStringAsync($"/api/orders/{other}"));
            var cancel = await cashier.PostAsync($"/api/orders/{other}/cancel", Json(new { reason = "Customer left", version = o.RootElement.GetProperty("version").GetInt32() }));
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync($"/api/orders/{other}/bill", Json(new { }))).StatusCode);
        }

        [Fact]
        public async Task BillingSettings_OwnerOnly_AndValidated()
        {
            var s = Build(TaxMode.None, withGstin: false);
            using var f = s.Factory;
            var admin = As(f, "admin");

            var current = await Read(await admin.GetAsync("/api/admin/billing-settings"));
            Assert.Equal("None", current.GetProperty("taxMode").GetString());
            Assert.Equal("INV", current.GetProperty("billPrefix").GetString());

            var bad = await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "Sometimes", pricesIncludeTax = true, billPrefix = "TOO-LONG" }));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var badBody = await bad.Content.ReadAsStringAsync();
            Assert.Contains("taxmode", badBody);
            Assert.Contains("billprefix", badBody);

            var saved = await Read(await admin.PutAsync("/api/admin/billing-settings", Json(new { taxMode = "composition", pricesIncludeTax = true, billPrefix = "an1" })));
            Assert.Equal("Composition", saved.GetProperty("taxMode").GetString());
            Assert.True(saved.GetProperty("pricesIncludeTax").GetBoolean());
            Assert.Equal("AN1", saved.GetProperty("billPrefix").GetString());

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").GetAsync("/api/admin/billing-settings")).StatusCode);

            // Other outlet settings are kept when billing settings change.
            using var scope = f.Services.CreateScope();
            var outlet = scope.ServiceProvider.GetRequiredService<AppDbContext>().Outlets.Single(o => o.Id == 1);
            Assert.Equal(99, outlet.GetSettings().MaxItemQuantity);
        }
    }
}
