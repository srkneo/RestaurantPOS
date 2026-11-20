using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Billing
{
    // A tip for the waiter of a table. Kept apart from the bill: it is not in the bill total, the GST or the invoice.
    public class Tip : TenantEntity
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public int TokenNumber { get; set; }
        public DateOnly BusinessDate { get; set; }
        public PaymentMethod Method { get; set; }
        public decimal Amount { get; set; }
        [MaxLength(100)] public string WaiterName { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public string? ReceivedBy { get; set; }
    }

    public record TipRequest(string? Method, decimal Amount, string? WaiterName);
    public record TipDto(int Id, int BillId, int TokenNumber, string Method, decimal Amount, string WaiterName, DateTime ReceivedAt);
    public record TipsByWaiterDto(string WaiterName, int Count, decimal Amount, decimal Cash);
    public record TipReportDto(DateOnly BusinessDate, decimal Total, IReadOnlyList<TipsByWaiterDto> Waiters, IReadOnlyList<TipDto> Tips);

    public class TipService
    {
        public const decimal MaxTip = 100_000m;
        // Tips with no waiter go to the shared pool of the shift.
        public const string SharedPool = "Shared";
        private readonly AppDbContext _db;
        private readonly BillService _bills;

        public TipService(AppDbContext db, BillService bills)
        {
            _db = db;
            _bills = bills;
        }

        public async Task<Tip> Add(int billId, TipRequest request)
        {
            var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == billId) ?? throw new KeyNotFoundException("Bill not found.");
            if (bill.Status == BillStatus.Void) throw new ArgumentException("This bill is void.");
            if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method) || int.TryParse(request.Method, out _) || method is PaymentMethod.Waived or PaymentMethod.Points)
                throw new ArgumentException("Choose Cash, UPI or Card.");
            var amount = BillCalculator.Paise(request.Amount);
            if (amount <= 0 || amount > MaxTip) throw new ArgumentException("Enter a tip above 0, up to 1,00,000.");

            var waiter = request.WaiterName?.Trim();
            if (string.IsNullOrEmpty(waiter))
            {
                waiter = await _db.KitchenOrders.Where(o => o.Id == bill.KitchenOrderId).Select(o => o.TakenBy).FirstOrDefaultAsync();
            }
            if (string.IsNullOrWhiteSpace(waiter)) waiter = SharedPool;
            if (waiter.Length > 100) throw new ArgumentException("The waiter name can be at most 100 characters.");

            var tip = new Tip
            {
                BillId = bill.Id,
                TokenNumber = bill.TokenNumber,
                BusinessDate = bill.BusinessDate,
                Method = method,
                Amount = amount,
                WaiterName = waiter,
                ReceivedAt = DateTime.UtcNow,
                ReceivedBy = _db.CurrentUserId
            };
            _db.Tips.Add(tip);

            // A cash tip goes into the drawer, so the open shift expects that cash too (as a cash-in line).
            if (method == PaymentMethod.Cash)
            {
                var shift = await _db.CashShifts.Include(s => s.Movements)
                    .FirstOrDefaultAsync(s => s.OutletId == _db.CurrentOutletId && s.ClosedAt == null);
                if (shift != null)
                {
                    var reason = $"Tip for {waiter} (token {bill.TokenNumber})";
                    shift.Movements.Add(new CashMovement
                    {
                        TenantId = _db.CurrentTenantId,
                        Kind = CashMovementKind.In,
                        Amount = amount,
                        Reason = reason.Length > 100 ? reason[..100] : reason,
                        At = DateTime.UtcNow,
                        By = _db.CurrentUserId
                    });
                    shift.Version++;
                }
            }
            await _db.SaveChangesAsync();
            return tip;
        }

        public async Task<TipReportDto> Report(DateOnly? date)
        {
            var day = date ?? await _bills.Today();
            var tips = await _db.Tips.Where(t => t.OutletId == _db.CurrentOutletId && t.BusinessDate == day)
                .OrderBy(t => t.ReceivedAt).ToListAsync();
            var waiters = tips.GroupBy(t => t.WaiterName)
                .Select(g => new TipsByWaiterDto(g.Key, g.Count(), g.Sum(t => t.Amount), g.Where(t => t.Method == PaymentMethod.Cash).Sum(t => t.Amount)))
                .OrderByDescending(w => w.Amount).ToList();
            return new TipReportDto(day, tips.Sum(t => t.Amount), waiters, tips.Select(ToDto).ToList());
        }

        public static TipDto ToDto(Tip t) => new(t.Id, t.BillId, t.TokenNumber, t.Method.ToString(), t.Amount, t.WaiterName, t.ReceivedAt);
    }

    public static class TipEndpoints
    {
        private static readonly string[] Till = { "cashier", "manager", "admin" };

        public static WebApplication MapTipEndpoints(this WebApplication app)
        {
            app.MapPost("/api/bills/{id:int}/tips", async (int id, TipRequest request, TipService tips) =>
            {
                try
                {
                    var tip = await tips.Add(id, request);
                    return Results.Created($"/api/tips/{tip.Id}", TipService.ToDto(tip));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            })
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Tips")
                .WithName("AddTip").WithSummary("Record a tip for the waiter of this bill (not part of the bill or GST)");

            app.MapGet("/api/tips", async (string? date, TipService tips) =>
            {
                DateOnly? day = null;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed)) return Results.BadRequest(new { message = "Use date=YYYY-MM-DD." });
                    day = parsed;
                }
                return Results.Ok(await tips.Report(day));
            })
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Tips")
                .WithName("GetTips").WithSummary("Tips of one business day, per waiter");

            return app;
        }
    }
}
