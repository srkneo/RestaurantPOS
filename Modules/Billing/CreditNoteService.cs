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
    public record CreateCreditNoteRequest(string? Reason, string? RefundMethod, string? ApproverPin, int Version);
    public record CreditNoteDto(int Id, string Number, DateTime IssuedAt, DateOnly BusinessDate, string Reason,
        decimal TaxableValue, decimal Cgst, decimal Sgst, decimal RoundOff, decimal Total, string? RefundMethod, decimal RefundAmount,
        string? ApprovedBy, BillDto Bill);
    public record CreditNoteSummaryDto(int Id, string Number, int BillId, string BillNumber, decimal Total, string Reason,
        string? RefundMethod, decimal RefundAmount);

    public class CreditNoteService
    {
        public const string Prefix = "CN";
        private readonly AppDbContext _db;
        private readonly BillService _bills;

        public CreditNoteService(AppDbContext db, BillService bills)
        {
            _db = db;
            _bills = bills;
        }

        // Cancels a whole bill of an earlier day. Money paid on it is given back; a cash refund is taken out of the open till shift.
        public async Task<CreditNote> Create(int billId, CreateCreditNoteRequest request, string approvedBy)
        {
            var bill = await _bills.GetBill(billId) ?? throw new KeyNotFoundException("Bill not found.");
            if (bill.Version != request.Version) throw new OrderConflictException("This bill was changed on another screen.");
            if (bill.Status == BillStatus.Void) throw new ArgumentException("This bill is void.");
            if (bill.CreditedAt != null || await _db.CreditNotes.AnyAsync(c => c.BillId == billId))
                throw new OrderConflictException("This bill already has a credit note.");
            var today = await _bills.Today();
            if (bill.BusinessDate >= today) throw new ArgumentException("This bill is from today. Void it instead.");
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Give a reason for the credit note.");
            if (reason.Length > 200) throw new ArgumentException("The reason can be at most 200 characters.");

            var paid = BillService.PaidOf(bill);
            PaymentMethod? refundMethod = null;
            if (paid > 0)
            {
                if (!Enum.TryParse<PaymentMethod>(request.RefundMethod, true, out var method) || int.TryParse(request.RefundMethod, out _) || method == PaymentMethod.Waived)
                    throw new ArgumentException("Choose how the money is given back: Cash, UPI or Card.");
                refundMethod = method;
            }

            var now = DateTime.UtcNow;
            var financialYear = BillNumbers.FinancialYearOf(today);
            var note = new CreditNote
            {
                BillId = bill.Id,
                FinancialYear = financialYear,
                BusinessDate = today,
                IssuedAt = now,
                Reason = reason,
                TaxableValue = bill.TaxableValue,
                Cgst = bill.Cgst,
                Sgst = bill.Sgst,
                RoundOff = bill.RoundOff,
                Total = bill.Total,
                RefundMethod = refundMethod,
                RefundAmount = paid,
                ApprovedBy = approvedBy
            };
            bill.CreditedAt = now;
            bill.Version++;

            var shift = refundMethod == PaymentMethod.Cash
                ? await _db.CashShifts.Include(s => s.Movements).FirstOrDefaultAsync(s => s.OutletId == _db.CurrentOutletId && s.ClosedAt == null)
                : null;

            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            note.Sequence = await NextNumber(financialYear);
            note.Number = BillNumbers.Format(Prefix, financialYear, note.Sequence);
            if (shift != null)
            {
                shift.Movements.Add(new CashMovement
                {
                    TenantId = _db.CurrentTenantId,
                    Kind = CashMovementKind.Out,
                    Amount = paid,
                    Reason = $"Refund {note.Number}",
                    At = now,
                    By = _db.CurrentUserId
                });
                shift.Version++;
            }
            _db.CreditNotes.Add(note);
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            return note;
        }

        public Task<CreditNote?> Get(int id) => _db.CreditNotes.FirstOrDefaultAsync(c => c.Id == id && c.OutletId == _db.CurrentOutletId);

        public async Task<List<CreditNote>> ForDay(DateOnly? businessDate)
        {
            var date = businessDate ?? await _bills.Today();
            return await _db.CreditNotes.Where(c => c.OutletId == _db.CurrentOutletId && c.BusinessDate == date)
                .OrderBy(c => c.FinancialYear).ThenBy(c => c.Sequence).ToListAsync();
        }

        public async Task<List<CreditNoteSummaryDto>> Summaries(IReadOnlyList<CreditNote> notes)
        {
            var billIds = notes.Select(c => c.BillId).ToList();
            var numbers = await _db.Bills.Where(b => billIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.InvoiceNumber);
            return notes.Select(c => new CreditNoteSummaryDto(c.Id, c.Number, c.BillId, numbers.GetValueOrDefault(c.BillId, string.Empty), c.Total,
                c.Reason, c.RefundMethod?.ToString(), c.RefundAmount)).ToList();
        }

        public async Task<CreditNoteDto> ToDto(CreditNote c)
        {
            var bill = await _bills.GetBill(c.BillId) ?? throw new KeyNotFoundException("Bill not found.");
            return new CreditNoteDto(c.Id, c.Number, c.IssuedAt, c.BusinessDate, c.Reason, c.TaxableValue, c.Cgst, c.Sgst, c.RoundOff, c.Total,
                c.RefundMethod?.ToString(), c.RefundAmount, c.ApprovedBy, BillService.ToDto(bill));
        }

        // Same gapless numbering as bills, in its own series.
        private async Task<int> NextNumber(int financialYear)
        {
            var tenantId = _db.CurrentTenantId;
            var outletId = _db.CurrentOutletId;
            if (_db.Database.IsRelational())
            {
                var numbers = await _db.Database.SqlQuery<int>($@"
                    INSERT INTO ""CreditNoteCounters"" (""TenantId"", ""OutletId"", ""FinancialYear"", ""LastNumber"")
                    VALUES ({tenantId}, {outletId}, {financialYear}, 1)
                    ON CONFLICT (""TenantId"", ""OutletId"", ""FinancialYear"")
                    DO UPDATE SET ""LastNumber"" = ""CreditNoteCounters"".""LastNumber"" + 1
                    RETURNING ""LastNumber"" AS ""Value""").ToListAsync();
                return numbers.Single();
            }

            var max = await _db.CreditNotes.Where(c => c.OutletId == outletId && c.FinancialYear == financialYear).Select(c => (int?)c.Sequence).MaxAsync();
            return (max ?? 0) + 1;
        }
    }
}
