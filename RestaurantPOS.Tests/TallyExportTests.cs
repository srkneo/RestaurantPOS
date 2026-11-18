using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
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
    // Day sales as Tally vouchers for the accountant: every voucher balances.
    public class TallyExportTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TallyExportTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int NaanId, int TeaId);

        // Regular GST restaurant: Butter Naan 40 and Tea 20, both at 5%.
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
            outlet.Gstin = "24ABCDE1234F1Z5"; // regular GST bills need a GSTIN
            outlet.Settings = settings.ToJson();
            var food = new MenuCategory { Name = "Food", SortOrder = 1 };
            db.MenuCategories.Add(food);
            var table = new RestaurantTable { TableNumber = "X1", QRCodeString = "t1-x1", SortOrder = 96 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var naan = new MenuItem { Name = "Butter Naan", Price = 40m, Category = "Food", CategoryId = food.Id, TaxRate = 5m };
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Food", CategoryId = food.Id, TaxRate = 5m };
            db.MenuItems.AddRange(naan, tea);
            db.SaveChanges();
            return new Setup(f, table.Id, naan.Id, tea.Id);
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

        private static async Task<JsonElement> Bill(Setup s, int itemId, int quantity)
        {
            var cashier = As(s.Factory, "cashier");
            var placed = await Read(await cashier.PostAsync("/api/orders", Json(new
            {
                table = s.TableId, clientOrderId = Guid.NewGuid(), items = new[] { new { menuItemId = itemId, quantity } }
            })));
            var bill = await cashier.PostAsync($"/api/orders/{placed.GetProperty("orderId").GetInt32()}/bill", Json(new { }));
            Assert.Equal(HttpStatusCode.Created, bill.StatusCode);
            return await Read(bill);
        }

        private static decimal Amount(XElement voucher, string ledger) =>
            voucher.Elements("ALLLEDGERENTRIES.LIST").Where(e => (string?)e.Element("LEDGERNAME") == ledger)
                .Sum(e => decimal.Parse((string)e.Element("AMOUNT")!, CultureInfo.InvariantCulture));

        [Fact]
        public async Task OneBalancedSalesVoucherPerDay()
        {
            var s = Build();
            using var f = s.Factory;
            var naanBill = await Bill(s, s.NaanId, 2);
            var teaBill = await Bill(s, s.TeaId, 1);
            var naanTotal = naanBill.GetProperty("total").GetDecimal();
            Assert.Equal(HttpStatusCode.OK, (await As(f, "cashier").PostAsync($"/api/bills/{naanBill.GetProperty("id").GetInt32()}/payments",
                Json(new { method = "Cash", amount = naanTotal, version = naanBill.GetProperty("version").GetInt32() }))).StatusCode);

            var response = await As(f, "manager").GetAsync("/api/reports/tally.xml");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
            var voucher = Assert.Single(xml.Descendants("VOUCHER"));
            Assert.Equal("Sales", (string?)voucher.Attribute("VCHTYPE"));
            Assert.StartsWith("POS-", (string?)voucher.Element("VOUCHERNUMBER"));

            var amounts = voucher.Elements("ALLLEDGERENTRIES.LIST").Select(e => decimal.Parse((string)e.Element("AMOUNT")!, CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(0m, amounts.Sum());
            Assert.Equal(-naanTotal, Amount(voucher, "Cash"));
            Assert.Equal(-teaBill.GetProperty("total").GetDecimal(), Amount(voucher, "POS Unpaid Bills"));
            Assert.Equal(naanBill.GetProperty("cgst").GetDecimal() + teaBill.GetProperty("cgst").GetDecimal(), Amount(voucher, "Output CGST"));
            Assert.True(Amount(voucher, "Sales") > 0);
            Assert.All(voucher.Elements("ALLLEDGERENTRIES.LIST"), e =>
                Assert.Equal(decimal.Parse((string)e.Element("AMOUNT")!, CultureInfo.InvariantCulture) < 0 ? "Yes" : "No", (string?)e.Element("ISDEEMEDPOSITIVE")));
        }

        [Fact]
        public async Task CreditNote_IsACreditNoteVoucherOnTheDayItIsIssued()
        {
            var s = Build();
            using var f = s.Factory;
            var bill = await Bill(s, s.NaanId, 2);
            var billId = bill.GetProperty("id").GetInt32();
            var today = DateOnly.ParseExact(bill.GetProperty("businessDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            // Move the bill to yesterday, as if it was made on an earlier day (credit notes are for earlier days).
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stored = db.Bills.IgnoreQueryFilters().Single(b => b.Id == billId);
                stored.BusinessDate = stored.BusinessDate.AddDays(-1);
                db.SaveChanges();
            }
            var manager = As(f, "manager");
            var created = await manager.PostAsync($"/api/bills/{billId}/credit-note", Json(new { reason = "Wrong table", version = bill.GetProperty("version").GetInt32() }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var from = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var xml = XDocument.Parse(await (await manager.GetAsync($"/api/reports/tally.xml?from={from}&to={to}")).Content.ReadAsStringAsync());
            var sale = Assert.Single(xml.Descendants("VOUCHER").Where(v => (string?)v.Attribute("VCHTYPE") == "Sales"));
            var credit = Assert.Single(xml.Descendants("VOUCHER").Where(v => (string?)v.Attribute("VCHTYPE") == "Credit Note"));
            var total = bill.GetProperty("total").GetDecimal();
            Assert.Equal(-total, Amount(sale, "POS Unpaid Bills"));
            Assert.Equal(total, Amount(credit, "POS Unpaid Bills"));
            Assert.Equal(-bill.GetProperty("cgst").GetDecimal(), Amount(credit, "Output CGST"));
            Assert.Equal(0m, credit.Elements("ALLLEDGERENTRIES.LIST").Sum(e => decimal.Parse((string)e.Element("AMOUNT")!, CultureInfo.InvariantCulture)));
            Assert.Contains(bill.GetProperty("invoiceNumber").GetString()!, (string?)credit.Element("NARRATION"));
            Assert.Equal(to.Replace("-", ""), (string?)credit.Element("DATE"));
        }

        [Fact]
        public async Task NoBills_EmptyFile_AndOnlyManagers()
        {
            var s = Build();
            using var f = s.Factory;
            var xml = XDocument.Parse(await (await As(f, "admin").GetAsync("/api/reports/tally.xml")).Content.ReadAsStringAsync());
            Assert.Empty(xml.Descendants("VOUCHER"));
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/reports/tally.xml")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await As(f, "admin").GetAsync("/api/reports/tally.xml?from=bad")).StatusCode);
        }
    }
}
