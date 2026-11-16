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
    public class ApprovalAndCreditNoteTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string GoodPassword = "Staff#2026x";
        private readonly WebApplicationFactory<Program> _factory;

        public ApprovalAndCreditNoteTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int PaneerId, int ColaId);

        // Regular GST restaurant; 2 x Paneer Tikka 220 (5%) + Cola 60 (18%) = bill of 533.
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
            return (await Read(bill)).GetProperty("id").GetInt32();
        }

        private static async Task<string> AddStaff(WebApplicationFactory<Program> f, string email, string name, string role)
        {
            var response = await As(f, "admin").PostAsync("/api/staff", Json(new { email, displayName = name, role, password = GoodPassword, isDevice = false }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await Read(response)).GetProperty("id").GetString()!;
        }

        private static Task<HttpResponseMessage> SetPin(WebApplicationFactory<Program> f, string id, string pin) =>
            As(f, "admin").PutAsync($"/api/staff/{id}/pin", Json(new { pin }));

        private static async Task<string?> Code(HttpResponseMessage response) =>
            (await Read(response)).GetProperty("code").GetString();

        [Fact]
        public async Task ManagerPin_LetsCashierVoid_AndIsRecordedOnTheBill()
        {
            var s = Build();
            using var f = s.Factory;
            var meena = await AddStaff(f, "meena@test.local", "Meena", "manager");
            var ravi = await AddStaff(f, "ravi@test.local", "Ravi", "manager");
            var cashierUser = await AddStaff(f, "kiran@test.local", "Kiran", "cashier");

            Assert.Equal(HttpStatusCode.BadRequest, (await SetPin(f, meena, "12")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetPin(f, cashierUser, "4321")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await SetPin(f, meena, "4321")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetPin(f, ravi, "4321")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "manager").PutAsync($"/api/staff/{ravi}/pin", Json(new { pin = "8765" }))).StatusCode);

            var staff = await Read(await As(f, "admin").GetAsync("/api/staff"));
            var meenaRow = staff.EnumerateArray().Single(u => u.GetProperty("id").GetString() == meena);
            Assert.True(meenaRow.GetProperty("hasPin").GetBoolean());

            var cashier = As(f, "cashier");
            var billId = await BilledOrder(s, cashier);
            var noPin = await cashier.PostAsync($"/api/bills/{billId}/void", Json(new { reason = "Wrong table", version = 1 }));
            Assert.Equal(HttpStatusCode.Forbidden, noPin.StatusCode);
            Assert.Equal("pin-required", await Code(noPin));
            var wrong = await cashier.PostAsync($"/api/bills/{billId}/void", Json(new { reason = "Wrong table", version = 1, approverPin = "1111" }));
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            Assert.Equal("wrong-pin", await Code(wrong));

            var voided = await cashier.PostAsync($"/api/bills/{billId}/void", Json(new { reason = "Wrong table", version = 1, approverPin = "4321" }));
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
            var bill = await Read(voided);
            Assert.Equal("Void", bill.GetProperty("status").GetString());
            Assert.Equal("Meena", bill.GetProperty("voidApprovedBy").GetString());

            var reprint = await cashier.PostAsync("/api/approvals", Json(new { pin = "4321", action = "reprint", billId }));
            Assert.Equal(HttpStatusCode.OK, reprint.StatusCode);
            Assert.Equal("Meena", (await Read(reprint)).GetProperty("approvedBy").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/approvals", Json(new { pin = "4321", action = "dance", billId }))).StatusCode);

            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Contains(db.AuditLogs.IgnoreQueryFilters().ToList(), a => a.Action == "Approve" && a.Details == "reprint approved by Meena");
            Assert.DoesNotContain(db.AuditLogs.IgnoreQueryFilters().ToList(), a => (a.Details ?? string.Empty).Contains("AQAAAA"));
        }

        [Fact]
        public async Task FiveWrongPins_LockApprovals()
        {
            var s = Build();
            using var f = s.Factory;
            var meena = await AddStaff(f, "meena@test.local", "Meena", "manager");
            Assert.Equal(HttpStatusCode.NoContent, (await SetPin(f, meena, "4321")).StatusCode);
            var cashier = As(f, "cashier");

            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync("/api/approvals", Json(new { pin = "0000", action = "reprint", billId = 1 }))).StatusCode);
            var locked = await cashier.PostAsync("/api/approvals", Json(new { pin = "4321", action = "reprint", billId = 1 }));
            Assert.Equal((HttpStatusCode)429, locked.StatusCode);
            Assert.Equal("too-many", await Code(locked));
        }

        [Fact]
        public async Task CreditNote_CancelsAnEarlierDayBill_RefundsCashFromTheTill_AndShowsInTheDayReport()
        {
            var s = Build();
            using var f = s.Factory;
            var cashier = As(f, "cashier");
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync("/api/shifts/open", Json(new { openingCash = 1000 }))).StatusCode);
            var billId = await BilledOrder(s, cashier);
            Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync($"/api/bills/{billId}/payments", Json(new { method = "cash", amount = 533, version = 1 }))).StatusCode);

            var today = await manager.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = "Food was cold", refundMethod = "cash", version = 2 }));
            Assert.Equal(HttpStatusCode.BadRequest, today.StatusCode);
            Assert.Contains("Void it instead", await today.Content.ReadAsStringAsync());

            // Move the bill to yesterday, as if it was made on an earlier day.
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = db.Bills.IgnoreQueryFilters().Single(b => b.Id == billId);
                stored.BusinessDate = stored.BusinessDate.AddDays(-1);
                db.SaveChanges();
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = "Food was cold", refundMethod = "cash", version = 2 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = " ", refundMethod = "cash", version = 2 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = "Food was cold", version = 2 }))).StatusCode);

            var created = await manager.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = "Food was cold", refundMethod = "cash", version = 2 }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var note = await Read(created);
            Assert.StartsWith("CN/", note.GetProperty("number").GetString());
            Assert.Matches(@"^CN/\d\d-\d\d/0+1$", note.GetProperty("number").GetString());  // CN is short, so the number gets more digits
            Assert.Equal(533m, note.GetProperty("total").GetDecimal());
            Assert.Equal(16.40m, note.GetProperty("cgst").GetDecimal());
            Assert.Equal("Cash", note.GetProperty("refundMethod").GetString());
            Assert.Equal(533m, note.GetProperty("refundAmount").GetDecimal());
            Assert.Equal(billId, note.GetProperty("bill").GetProperty("id").GetInt32());
            Assert.Equal(JsonValueKind.String, note.GetProperty("bill").GetProperty("creditedAt").ValueKind);
            var noteId = note.GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = "Again", refundMethod = "cash", version = 3 }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync($"/api/bills/{billId}/void", Json(new { reason = "Late void", version = 3 }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync($"/api/credit-notes/{noteId}")).StatusCode);

            var shift = (await Read(await cashier.GetAsync("/api/shifts/current"))).GetProperty("shift");
            Assert.Equal(533m, shift.GetProperty("cashOut").GetDecimal());
            Assert.Equal(1000m, shift.GetProperty("expectedCash").GetDecimal());
            Assert.StartsWith("Refund CN/", shift.GetProperty("movements")[0].GetProperty("reason").GetString());

            var list = await Read(await manager.GetAsync("/api/credit-notes"));
            Assert.Equal(1, list.GetArrayLength());
            Assert.Equal("Food was cold", list[0].GetProperty("reason").GetString());

            var report = await Read(await manager.GetAsync("/api/reports/day"));
            Assert.Equal(1, report.GetProperty("creditNotes").GetArrayLength());
            Assert.Equal(533m, report.GetProperty("creditTotal").GetDecimal());
            Assert.Equal(16.40m, report.GetProperty("creditCgst").GetDecimal());
            Assert.Equal(-533m, report.GetProperty("netSales").GetDecimal());
        }
    }
}
