using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Billing
{
    public record DaySalesDto(DateOnly Date, int Bills, decimal Total);
    public record CategorySalesDto(string Category, int Quantity, decimal Amount);
    public record HourSalesDto(int Hour, int Bills, decimal Total);
    public record StaffSalesDto(string? Name, int Bills, decimal Total);
    public record SalesReportDto(DateOnly From, DateOnly To, int BillCount, decimal ItemsTotal, decimal Discount, decimal TaxableValue,
        decimal Cgst, decimal Sgst, decimal Total, decimal CreditTotal, decimal NetSales,
        IReadOnlyList<DaySalesDto> Days, IReadOnlyList<ItemSalesDto> Items, IReadOnlyList<CategorySalesDto> Categories,
        IReadOnlyList<PaymentTotalDto> Payments, IReadOnlyList<HourSalesDto> Hours, IReadOnlyList<StaffSalesDto> Staff,
        IReadOnlyList<TaxSummaryRow> TaxSummary, IReadOnlyList<ExpenseTotalDto>? Expenses = null, decimal ExpenseTotal = 0m,
        int FeedbackCount = 0, decimal? FeedbackAverage = null);

    // Owner reports (v1.9) for a range of business days: sales by day, item, category, payment, hour and staff,
    // GST per rate, and a GST bill list for the accountant as CSV. Read-only: nothing here changes a bill.
    public static class SalesReports
    {
        public const int MaxDays = 92;
        private static readonly string[] Managers = { "manager", "admin" };

        public static WebApplication MapReportEndpoints(this WebApplication app)
        {
            app.MapGet("/api/reports/sales", async (string? from, string? to, AppDbContext db, BillService bills) =>
            {
                var range = await Range(from, to, bills);
                if (range.Error != null) return Results.BadRequest(new { message = range.Error });
                return Results.Ok(await Build(db, range.From, range.To));
            })
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithTags("Reports")
                .WithName("GetSalesReport").WithSummary("Sales for a range of business days: by day, item, category, payment, hour and staff");

            app.MapGet("/api/reports/gst.csv", async (string? from, string? to, AppDbContext db, BillService bills) =>
            {
                var range = await Range(from, to, bills);
                if (range.Error != null) return Results.BadRequest(new { message = range.Error });
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(await GstCsv(db, range.From, range.To))).ToArray();
                return Results.File(bytes, "text/csv", $"gst-bills-{range.From:yyyy-MM-dd}-to-{range.To:yyyy-MM-dd}.csv");
            })
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithTags("Reports")
                .WithName("GetGstCsv").WithSummary("Every bill of a date range with taxable value, CGST and SGST, as CSV for the accountant");

            app.MapGet("/api/reports/sales.csv", async (string? from, string? to, AppDbContext db, BillService bills) =>
            {
                var range = await Range(from, to, bills);
                if (range.Error != null) return Results.BadRequest(new { message = range.Error });
                var report = await Build(db, range.From, range.To);
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(SalesCsv(report))).ToArray();
                return Results.File(bytes, "text/csv", $"sales-{range.From:yyyy-MM-dd}-to-{range.To:yyyy-MM-dd}.csv");
            })
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithTags("Reports")
                .WithName("GetSalesCsv").WithSummary("The sales report of a date range as CSV: days, categories, items, payments, hours, staff, expenses");

            app.MapGet("/api/reports/tally.xml", async (string? from, string? to, AppDbContext db, BillService bills) =>
            {
                var range = await Range(from, to, bills);
                if (range.Error != null) return Results.BadRequest(new { message = range.Error });
                var bytes = TallyExport.Bytes(await TallyExport.Xml(db, range.From, range.To));
                return Results.File(bytes, "application/xml", $"tally-sales-{range.From:yyyy-MM-dd}-to-{range.To:yyyy-MM-dd}.xml");
            })
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithTags("Reports")
                .WithName("GetTallyXml").WithSummary("Day sales of a date range as Tally vouchers (XML for Tally Import Data)");

            return app;
        }

        // from/to as yyyy-MM-dd; one of them alone means that single day; neither means today. At most 92 days.
        private static async Task<(DateOnly From, DateOnly To, string? Error)> Range(string? from, string? to, BillService bills)
        {
            var today = await bills.Today();
            DateOnly start = today, end = today;
            var hasFrom = !string.IsNullOrWhiteSpace(from);
            var hasTo = !string.IsNullOrWhiteSpace(to);
            if (hasFrom && !DateOnly.TryParseExact(from, "yyyy-MM-dd", out start)) return (today, today, "Use from=YYYY-MM-DD.");
            if (hasTo && !DateOnly.TryParseExact(to, "yyyy-MM-dd", out end)) return (today, today, "Use to=YYYY-MM-DD.");
            if (hasFrom && !hasTo) end = start;
            if (hasTo && !hasFrom) start = end;
            if (end < start) return (today, today, "The end date is before the start date.");
            if (end.DayNumber - start.DayNumber + 1 > MaxDays) return (today, today, $"Choose at most {MaxDays} days.");
            return (start, end, null);
        }

        public static async Task<SalesReportDto> Build(AppDbContext db, DateOnly from, DateOnly to)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId);
            var zone = BusinessClock.FindZone(outlet?.TimeZone);
            var bills = await db.Bills.Include(b => b.Lines).Include(b => b.Payments).AsNoTracking()
                .Where(b => b.OutletId == db.CurrentOutletId && b.BusinessDate >= from && b.BusinessDate <= to && b.Status != BillStatus.Void)
                .ToListAsync();

            var orderIds = bills.Select(b => b.KitchenOrderId).Distinct().ToList();
            var orders = await db.KitchenOrders.Include(o => o.Items).AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToListAsync();
            var menuIds = orders.SelectMany(o => o.Items).Select(i => i.MenuItemId).Distinct().ToList();
            var categoryOf = await db.MenuItems.AsNoTracking().Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Category);
            var takenBy = orders.ToDictionary(o => o.Id, o => o.TakenBy);

            var days = bills.GroupBy(b => b.BusinessDate).OrderBy(g => g.Key)
                .Select(g => new DaySalesDto(g.Key, g.Count(), g.Sum(b => b.Total))).ToList();
            var items = bills.SelectMany(b => b.Lines).GroupBy(l => l.ItemName)
                .Select(g => new ItemSalesDto(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Amount)))
                .OrderByDescending(i => i.Amount).ThenBy(i => i.ItemName).ToList();
            // Category from the menu item behind each order line (amounts before discount, like the item list).
            var categories = orders.SelectMany(o => o.Items)
                .GroupBy(i => categoryOf.TryGetValue(i.MenuItemId, out var c) && !string.IsNullOrWhiteSpace(c) ? c : "Other")
                .Select(g => new CategorySalesDto(g.Key, g.Sum(i => i.Quantity), g.Sum(i => BillCalculator.Paise(i.Quantity * i.UnitPrice))))
                .OrderByDescending(c => c.Amount).ThenBy(c => c.Category).ToList();
            var payments = bills.SelectMany(b => b.Payments).ToList();
            var byMethod = Enum.GetValues<PaymentMethod>()
                .Select(m => new PaymentTotalDto(m.ToString(), payments.Count(p => p.Method == m), payments.Where(p => p.Method == m).Sum(p => p.Amount)))
                .Where(p => (p.Method != nameof(PaymentMethod.Waived) && p.Method != nameof(PaymentMethod.Points)) || p.Count > 0)
                .ToList();
            var hours = bills
                .GroupBy(b => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.IssuedAt, DateTimeKind.Utc), zone).Hour)
                .OrderBy(g => g.Key).Select(g => new HourSalesDto(g.Key, g.Count(), g.Sum(b => b.Total))).ToList();
            var staff = bills.GroupBy(b => takenBy.TryGetValue(b.KitchenOrderId, out var name) ? name : null)
                .Select(g => new StaffSalesDto(g.Key, g.Count(), g.Sum(b => b.Total)))
                .OrderByDescending(s => s.Total).ToList();

            var creditTotal = await db.CreditNotes.Where(c => c.OutletId == db.CurrentOutletId && c.BusinessDate >= from && c.BusinessDate <= to)
                .SumAsync(c => c.Total);
            var expenses = await ExpenseService.Totals(db, from, to);
            // Guest ratings given for the range's bills (feedback QR).
            var billIds = bills.Select(b => b.Id).ToList();
            var ratings = await db.GuestFeedbacks.Where(f => billIds.Contains(f.BillId)).Select(f => f.Rating).ToListAsync();
            decimal? feedbackAverage = ratings.Count == 0 ? null : Math.Round((decimal)ratings.Average(), 1);
            var total = bills.Sum(b => b.Total);
            return new SalesReportDto(from, to, bills.Count, bills.Sum(b => b.ItemsTotal), bills.Sum(b => b.Discount), bills.Sum(b => b.TaxableValue),
                bills.Sum(b => b.Cgst), bills.Sum(b => b.Sgst), total, creditTotal, total - creditTotal,
                days, items, categories, byMethod, hours, staff,
                BillCalculator.TaxSummary(bills.Where(b => b.TaxMode == TaxMode.Regular).SelectMany(b => b.Lines)),
                expenses, expenses.Sum(e => e.Amount), ratings.Count, feedbackAverage);
        }

        public static async Task<string> GstCsv(AppDbContext db, DateOnly from, DateOnly to)
        {
            var bills = await db.Bills.AsNoTracking()
                .Where(b => b.OutletId == db.CurrentOutletId && b.BusinessDate >= from && b.BusinessDate <= to)
                .OrderBy(b => b.FinancialYear).ThenBy(b => b.Sequence).ToListAsync();
            var csv = new StringBuilder("Invoice,Date,Status,Customer,Taxable value,CGST,SGST,Round off,Total\r\n");
            foreach (var b in bills)
            {
                var csvLine = string.Join(",", Cell(b.InvoiceNumber), b.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    b.Status.ToString(), Cell(b.CustomerName), Money(b.TaxableValue), Money(b.Cgst), Money(b.Sgst), Money(b.RoundOff), Money(b.Total));
                csv.Append(csvLine).Append("\r\n");
            }
            return csv.ToString();
        }

        // One row per figure, so it opens straight in Excel: Section, Name, Count, Amount.
        public static string SalesCsv(SalesReportDto r)
        {
            var csv = new StringBuilder("Section,Name,Count,Amount\r\n");
            void Row(string section, string? name, decimal count, decimal amount) =>
                csv.Append(string.Join(",", section, Cell(name), count.ToString(CultureInfo.InvariantCulture), Money(amount))).Append("\r\n");
            Row("Total", "Bills", r.BillCount, r.Total);
            Row("Total", "Net sales", r.BillCount, r.NetSales);
            foreach (var d in r.Days) Row("Day", d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Bills, d.Total);
            foreach (var c in r.Categories) Row("Category", c.Category, c.Quantity, c.Amount);
            foreach (var i in r.Items) Row("Item", i.ItemName, i.Quantity, i.Amount);
            foreach (var p in r.Payments) Row("Payment", p.Method, p.Count, p.Amount);
            foreach (var h in r.Hours) Row("Hour", $"{h.Hour:00}:00", h.Bills, h.Total);
            foreach (var s in r.Staff) Row("Staff", s.Name ?? "Not recorded", s.Bills, s.Total);
            foreach (var e in r.Expenses ?? new List<ExpenseTotalDto>()) Row("Expense", e.Category, e.Count, e.Amount);
            return csv.ToString();
        }

        private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        // Quotes text with commas or quotes, and stops spreadsheet formulas (a name starting with = + - @ stays text).
        private static string Cell(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var value = "=+-@".Contains(text[0]) ? "'" + text : text;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
