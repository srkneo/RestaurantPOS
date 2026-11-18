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
    // Owner reports for a date range, and the GST bill list as CSV.
    public class SalesReportTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SalesReportTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private record Setup(WebApplicationFactory<Program> Factory, int TableId, int NaanId, int TeaId);

        // No-GST restaurant: Butter Naan 40 in Breads, Tea 20 in Drinks.
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
            settings.TaxMode = TaxMode.None;
            outlet.Settings = settings.ToJson();
            var breads = new MenuCategory { Name = "Breads", SortOrder = 1 };
            var drinks = new MenuCategory { Name = "Drinks", SortOrder = 2 };
            db.MenuCategories.AddRange(breads, drinks);
            var table = new RestaurantTable { TableNumber = "R1", QRCodeString = "t1-r1", SortOrder = 95 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var naan = new MenuItem { Name = "Butter Naan", Price = 40m, Category = "Breads", CategoryId = breads.Id, TaxRate = 5m };
            var tea = new MenuItem { Name = "Tea", Price = 20m, Category = "Drinks", CategoryId = drinks.Id, TaxRate = 5m };
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

        [Fact]
        public async Task SalesReport_AddsUpByCategoryPaymentAndDay()
        {
            var s = Build();
            using var f = s.Factory;
            var naanBill = await Bill(s, s.NaanId, 2);
            await Bill(s, s.TeaId, 1);
            Assert.Equal(HttpStatusCode.OK, (await As(f, "cashier").PostAsync($"/api/bills/{naanBill.GetProperty("id").GetInt32()}/payments",
                Json(new { method = "Cash", amount = 80m, version = naanBill.GetProperty("version").GetInt32() }))).StatusCode);

            var report = await Read(await As(f, "manager").GetAsync("/api/reports/sales"));
            Assert.Equal(2, report.GetProperty("billCount").GetInt32());
            Assert.Equal(100m, report.GetProperty("total").GetDecimal());
            Assert.Equal(100m, report.GetProperty("netSales").GetDecimal());
            var categories = report.GetProperty("categories").EnumerateArray().ToList();
            Assert.Equal(80m, categories.Single(c => c.GetProperty("category").GetString() == "Breads").GetProperty("amount").GetDecimal());
            Assert.Equal(20m, categories.Single(c => c.GetProperty("category").GetString() == "Drinks").GetProperty("amount").GetDecimal());
            var cash = report.GetProperty("payments").EnumerateArray().Single(p => p.GetProperty("method").GetString() == "Cash");
            Assert.Equal(80m, cash.GetProperty("amount").GetDecimal());
            Assert.Equal(1, report.GetProperty("days").GetArrayLength());
            Assert.Equal(2, report.GetProperty("hours").EnumerateArray().Sum(h => h.GetProperty("bills").GetInt32()));
            Assert.Equal(2, report.GetProperty("staff").EnumerateArray().Sum(h => h.GetProperty("bills").GetInt32()));
        }

        [Fact]
        public async Task GstCsv_ListsEveryBill()
        {
            var s = Build();
            using var f = s.Factory;
            var first = await Bill(s, s.NaanId, 2);
            var second = await Bill(s, s.TeaId, 1);

            var response = await As(f, "admin").GetAsync("/api/reports/gst.csv");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            var csv = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invoice,Date,Status,Customer,Taxable value,CGST,SGST,Round off,Total", csv);
            Assert.Contains(first.GetProperty("invoiceNumber").GetString()!, csv);
            Assert.Contains(second.GetProperty("invoiceNumber").GetString()!, csv);
            Assert.Contains(",80.00", csv);
        }

        [Fact]
        public async Task Reports_CheckTheRange_AndAreForOwnerOrManager()
        {
            var s = Build();
            using var f = s.Factory;
            var manager = As(f, "manager");
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/reports/sales?from=2026-01-01&to=2026-12-31")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/reports/sales?from=2026-11-10&to=2026-11-01")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/reports/sales?from=yesterday")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/reports/sales?from=2026-11-01&to=2026-11-30")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/reports/sales")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "cashier").GetAsync("/api/reports/gst.csv")).StatusCode);
        }
    }
}
