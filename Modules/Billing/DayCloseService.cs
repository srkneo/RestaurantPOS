using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;

namespace POSSystem.Billing
{
    public record CashMovementDto(int Id, string Kind, decimal Amount, string Reason, DateTime At);
    public record ShiftDto(int Id, string Status, DateOnly BusinessDate, DateTime OpenedAt, decimal OpeningCash, decimal CashSales,
        decimal CashIn, decimal CashOut, decimal ExpectedCash, DateTime? ClosedAt, decimal? CountedCash, decimal? Difference,
        string? CloseNote, int Version, IReadOnlyList<CashMovementDto> Movements);
    public record CurrentShiftDto(ShiftDto? Shift);
    public record OpenShiftRequest(decimal OpeningCash);
    public record CashMovementRequest(string? Kind, decimal Amount, string? Reason);
    public record CloseShiftRequest(decimal CountedCash, string? Note, int Version);

    public record PaymentTotalDto(string Method, int Count, decimal Amount);
    public record ItemSalesDto(string ItemName, int Quantity, decimal Amount);
    public record VoidedBillDto(int Id, string InvoiceNumber, decimal Total, string? Reason);
    public record DayReportDto(DateOnly BusinessDate, string RestaurantName, string OutletName, string? Gstin, DateTime GeneratedAt,
        string? FirstInvoice, string? LastInvoice, int BillCount,
        decimal ItemsTotal, decimal Discount, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal RoundOff, decimal Total,
        IReadOnlyList<PaymentTotalDto> Payments, decimal AmountReceived, int UnpaidCount, decimal AmountDue,
        IReadOnlyList<TaxSummaryRow> TaxSummary, IReadOnlyList<ItemSalesDto> Items,
        int VoidCount, decimal VoidTotal, IReadOnlyList<VoidedBillDto> VoidedBills, IReadOnlyList<ShiftDto> Shifts,
        IReadOnlyList<CreditNoteSummaryDto> CreditNotes, decimal CreditTaxable, decimal CreditCgst, decimal CreditSgst, decimal CreditTotal,
        decimal NetSales);

    // Till shifts (open with float, cash in/out, close with count) and the end-of-day (Z) report.
    public class DayCloseService
    {
        public const decimal MaxCash = 1_000_000m;
        private readonly AppDbContext _db;
        private readonly BillService _bills;

        public DayCloseService(AppDbContext db, BillService bills)
        {
            _db = db;
            _bills = bills;
        }

        public Task<CashShift?> Current() =>
            _db.CashShifts.Include(s => s.Movements).FirstOrDefaultAsync(s => s.OutletId == _db.CurrentOutletId && s.ClosedAt == null);

        public async Task<CashShift> Open(OpenShiftRequest request)
        {
            if (await Current() != null) throw new OrderConflictException("A shift is already open. Close it first.");
            var cash = Cash(request.OpeningCash, "Enter the cash in the drawer, from 0 to 10,00,000.");

            var shift = new CashShift
            {
                BusinessDate = await _bills.Today(),
                OpenedAt = DateTime.UtcNow,
                OpenedBy = _db.CurrentUserId,
                OpeningCash = cash
            };
            _db.CashShifts.Add(shift);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // Two tills opened at the same moment: the unique index lets only one through.
                throw new OrderConflictException("A shift is already open. Close it first.");
            }
            return shift;
        }

        public async Task<CashShift> AddMovement(CashMovementRequest request)
        {
            var shift = await Current() ?? throw new ArgumentException("Open a shift first.");
            if (!Enum.TryParse<CashMovementKind>(request.Kind, true, out var kind) || int.TryParse(request.Kind, out _))
                throw new ArgumentException("Choose cash in or cash out.");
            var amount = Cash(request.Amount, "Enter an amount above 0, up to 10,00,000.");
            if (amount == 0) throw new ArgumentException("Enter an amount above 0, up to 10,00,000.");
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Give a reason, for example \"Milk vendor\".");
            if (reason.Length > 100) throw new ArgumentException("The reason can be at most 100 characters.");

            shift.Movements.Add(new CashMovement
            {
                TenantId = _db.CurrentTenantId,
                Kind = kind,
                Amount = amount,
                Reason = reason,
                At = DateTime.UtcNow,
                By = _db.CurrentUserId
            });
            shift.Version++;
            await _db.SaveChangesAsync();
            return shift;
        }

        public async Task<CashShift> Close(CloseShiftRequest request)
        {
            var shift = await Current() ?? throw new ArgumentException("No shift is open.");
            if (shift.Version != request.Version) throw new OrderConflictException("This shift was changed on another screen. Check the amounts again.");
            var counted = Cash(request.CountedCash, "Enter the cash counted, from 0 to 10,00,000.");
            var note = request.Note?.Trim();
            if (note is { Length: > 200 }) throw new ArgumentException("The note can be at most 200 characters.");

            var now = DateTime.UtcNow;
            var sales = await CashSalesBetween(shift.OpenedAt, now);
            shift.CashSales = sales;
            shift.ExpectedCash = ExpectedOf(shift, sales);
            shift.CountedCash = counted;
            shift.ClosedAt = now;
            shift.ClosedBy = _db.CurrentUserId;
            shift.CloseNote = string.IsNullOrEmpty(note) ? null : note;
            shift.Version++;
            await _db.SaveChangesAsync();
            return shift;
        }

        public async Task<List<CashShift>> GetShifts(DateOnly? businessDate)
        {
            var date = businessDate ?? await _bills.Today();
            return await _db.CashShifts.Include(s => s.Movements)
                .Where(s => s.OutletId == _db.CurrentOutletId && s.BusinessDate == date)
                .OrderBy(s => s.OpenedAt).ToListAsync();
        }

        // An open shift shows the cash taken so far; a closed one shows what was kept when it closed.
        public async Task<ShiftDto> Describe(CashShift s) =>
            ToDto(s, s.ClosedAt == null ? await CashSalesBetween(s.OpenedAt, null) : s.CashSales ?? 0m);

        // Cash received on bills that are not void (a bill voided after payment means the cash was given back).
        private async Task<decimal> CashSalesBetween(DateTime start, DateTime? end)
        {
            var outletId = _db.CurrentOutletId;
            var query = from p in _db.BillPayments
                        join b in _db.Bills on p.BillId equals b.Id
                        where b.OutletId == outletId && b.Status != BillStatus.Void && p.Method == PaymentMethod.Cash && p.ReceivedAt >= start
                        select new { p.ReceivedAt, p.Amount };
            if (end is { } until) query = query.Where(x => x.ReceivedAt < until);
            return await query.SumAsync(x => x.Amount);
        }

        public static decimal CashInOf(CashShift s) => s.Movements.Where(m => m.Kind == CashMovementKind.In).Sum(m => m.Amount);
        public static decimal CashOutOf(CashShift s) => s.Movements.Where(m => m.Kind == CashMovementKind.Out).Sum(m => m.Amount);
        public static decimal ExpectedOf(CashShift s, decimal cashSales) => s.OpeningCash + cashSales + CashInOf(s) - CashOutOf(s);

        public static ShiftDto ToDto(CashShift s, decimal cashSales)
        {
            var expected = s.ExpectedCash ?? ExpectedOf(s, cashSales);
            return new ShiftDto(s.Id, s.ClosedAt == null ? "Open" : "Closed", s.BusinessDate, s.OpenedAt, s.OpeningCash, cashSales,
                CashInOf(s), CashOutOf(s), expected, s.ClosedAt, s.CountedCash, s.CountedCash.HasValue ? s.CountedCash.Value - expected : null,
                s.CloseNote, s.Version,
                s.Movements.OrderBy(m => m.Id).Select(m => new CashMovementDto(m.Id, m.Kind.ToString(), m.Amount, m.Reason, m.At)).ToList());
        }

        private static decimal Cash(decimal value, string message)
        {
            var cash = BillCalculator.Paise(value);
            if (cash < 0 || cash > MaxCash) throw new ArgumentException(message);
            return cash;
        }

        // End-of-day (Z) report for one business day: sales, GST per rate, money by method, void bills and shifts.
        public async Task<DayReportDto> DayReport(DateOnly? businessDate)
        {
            var date = businessDate ?? await _bills.Today();
            var outlet = await _db.Outlets.FirstAsync(o => o.Id == _db.CurrentOutletId && o.TenantId == _db.CurrentTenantId);
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _db.CurrentTenantId);
            var all = await _db.Bills.Include(b => b.Lines).Include(b => b.Payments)
                .Where(b => b.OutletId == _db.CurrentOutletId && b.BusinessDate == date)
                .OrderBy(b => b.FinancialYear).ThenBy(b => b.Sequence).ToListAsync();
            var bills = all.Where(b => b.Status != BillStatus.Void).ToList();
            var voided = all.Where(b => b.Status == BillStatus.Void).ToList();

            var payments = bills.SelectMany(b => b.Payments).ToList();
            var byMethod = Enum.GetValues<PaymentMethod>()
                .Select(m => new PaymentTotalDto(m.ToString(), payments.Count(p => p.Method == m), payments.Where(p => p.Method == m).Sum(p => p.Amount)))
                .ToList();
            var unpaid = bills.Where(b => b.Status != BillStatus.Paid).ToList();
            var items = bills.SelectMany(b => b.Lines)
                .GroupBy(l => l.ItemName)
                .Select(g => new ItemSalesDto(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.Amount)))
                .OrderByDescending(i => i.Amount).ThenBy(i => i.ItemName).ToList();

            // Credit notes issued this day (they may cancel bills of earlier days).
            var credits = await _db.CreditNotes.Where(c => c.OutletId == _db.CurrentOutletId && c.BusinessDate == date)
                .OrderBy(c => c.FinancialYear).ThenBy(c => c.Sequence).ToListAsync();
            var creditBillIds = credits.Select(c => c.BillId).ToList();
            var creditBillNumbers = await _db.Bills.Where(b => creditBillIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.InvoiceNumber);
            var creditList = credits.Select(c => new CreditNoteSummaryDto(c.Id, c.Number, c.BillId, creditBillNumbers.GetValueOrDefault(c.BillId, string.Empty),
                c.Total, c.Reason, c.RefundMethod?.ToString(), c.RefundAmount)).ToList();

            var shifts = new List<ShiftDto>();
            foreach (var shift in await GetShifts(date)) shifts.Add(await Describe(shift));

            return new DayReportDto(date, tenant?.Name ?? string.Empty, outlet.Name, outlet.Gstin, DateTime.UtcNow,
                all.FirstOrDefault()?.InvoiceNumber, all.LastOrDefault()?.InvoiceNumber, bills.Count,
                bills.Sum(b => b.ItemsTotal), bills.Sum(b => b.Discount), bills.Sum(b => b.TaxableValue), bills.Sum(b => b.Cgst),
                bills.Sum(b => b.Sgst), bills.Sum(b => b.RoundOff), bills.Sum(b => b.Total),
                byMethod, payments.Sum(p => p.Amount), unpaid.Count, unpaid.Sum(b => BillService.DueOf(b)),
                BillCalculator.TaxSummary(bills.Where(b => b.TaxMode == TaxMode.Regular).SelectMany(b => b.Lines)), items,
                voided.Count, voided.Sum(b => b.Total), voided.Select(b => new VoidedBillDto(b.Id, b.InvoiceNumber, b.Total, b.VoidReason)).ToList(),
                shifts, creditList, credits.Sum(c => c.TaxableValue), credits.Sum(c => c.Cgst), credits.Sum(c => c.Sgst),
                credits.Sum(c => c.Total), bills.Sum(b => b.Total) - credits.Sum(c => c.Total));
        }
    }
}
