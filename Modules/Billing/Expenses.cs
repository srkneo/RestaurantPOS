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
    public enum ExpensePaidFrom { Till, Other }

    // Money the restaurant spends (milk, vegetables, gas, rent...). Paid from the till = a cash-out line in the open shift.
    public class Expense : TenantEntity
    {
        public int Id { get; set; }
        public DateOnly BusinessDate { get; set; }
        [MaxLength(40)] public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public ExpensePaidFrom PaidFrom { get; set; }
        [MaxLength(100)] public string? Note { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
        public string? By { get; set; }
    }

    public record ExpenseRequest(string? Category, decimal Amount, string? PaidFrom, string? Note);
    public record ExpenseDto(int Id, DateOnly BusinessDate, string Category, decimal Amount, string PaidFrom, string? Note, DateTime At);
    public record ExpenseTotalDto(string Category, int Count, decimal Amount);
    public record ExpenseListDto(DateOnly BusinessDate, decimal Total, decimal FromTill, IReadOnlyList<ExpenseDto> Expenses);

    public class ExpenseService
    {
        public static readonly string[] Categories = { "Vegetables", "Groceries", "Milk", "Gas", "Salary", "Rent", "Electricity", "Repairs", "Other" };
        public const decimal MaxExpense = 1_000_000m;
        private readonly AppDbContext _db;
        private readonly BillService _bills;

        public ExpenseService(AppDbContext db, BillService bills)
        {
            _db = db;
            _bills = bills;
        }

        public async Task<Expense> Add(ExpenseRequest request)
        {
            var category = Categories.FirstOrDefault(c => string.Equals(c, request.Category?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Choose a category from the list.");
            if (!Enum.TryParse<ExpensePaidFrom>(request.PaidFrom, true, out var paidFrom) || int.TryParse(request.PaidFrom, out _))
                throw new ArgumentException("Choose paid from the till or from elsewhere.");
            var amount = BillCalculator.Paise(request.Amount);
            if (amount <= 0 || amount > MaxExpense) throw new ArgumentException("Enter an amount above 0, up to 10,00,000.");
            var note = request.Note?.Trim();
            if (note is { Length: > 100 }) throw new ArgumentException("The note can be at most 100 characters.");

            CashShift? shift = null;
            if (paidFrom == ExpensePaidFrom.Till)
            {
                shift = await _db.CashShifts.Include(s => s.Movements)
                    .FirstOrDefaultAsync(s => s.OutletId == _db.CurrentOutletId && s.ClosedAt == null)
                    ?? throw new ArgumentException("Open a till shift first, or choose paid from elsewhere.");
            }

            var expense = new Expense
            {
                BusinessDate = await _bills.Today(),
                Category = category,
                Amount = amount,
                PaidFrom = paidFrom,
                Note = string.IsNullOrEmpty(note) ? null : note,
                At = DateTime.UtcNow,
                By = _db.CurrentUserId
            };
            _db.Expenses.Add(expense);
            if (shift != null)
            {
                var reason = string.IsNullOrEmpty(expense.Note) ? $"Expense: {category}" : $"Expense: {category} - {expense.Note}";
                shift.Movements.Add(new CashMovement
                {
                    TenantId = _db.CurrentTenantId,
                    Kind = CashMovementKind.Out,
                    Amount = amount,
                    Reason = reason.Length > 100 ? reason[..100] : reason,
                    At = DateTime.UtcNow,
                    By = _db.CurrentUserId
                });
                shift.Version++;
            }
            await _db.SaveChangesAsync();
            return expense;
        }

        public async Task<ExpenseListDto> List(DateOnly? date)
        {
            var day = date ?? await _bills.Today();
            var list = await _db.Expenses.Where(e => e.OutletId == _db.CurrentOutletId && e.BusinessDate == day).OrderBy(e => e.At).ToListAsync();
            return new ExpenseListDto(day, list.Sum(e => e.Amount), list.Where(e => e.PaidFrom == ExpensePaidFrom.Till).Sum(e => e.Amount),
                list.Select(ToDto).ToList());
        }

        // Expenses per category for a range of days (for the reports).
        public static async Task<List<ExpenseTotalDto>> Totals(AppDbContext db, DateOnly from, DateOnly to) =>
            (await db.Expenses.Where(e => e.OutletId == db.CurrentOutletId && e.BusinessDate >= from && e.BusinessDate <= to).ToListAsync())
                .GroupBy(e => e.Category).Select(g => new ExpenseTotalDto(g.Key, g.Count(), g.Sum(e => e.Amount)))
                .OrderByDescending(e => e.Amount).ToList();

        public static ExpenseDto ToDto(Expense e) => new(e.Id, e.BusinessDate, e.Category, e.Amount, e.PaidFrom.ToString(), e.Note, e.At);
    }

    public static class ExpenseEndpoints
    {
        private static readonly string[] Till = { "cashier", "manager", "admin" };

        public static WebApplication MapExpenseEndpoints(this WebApplication app)
        {
            app.MapPost("/api/expenses", async (ExpenseRequest request, ExpenseService expenses) =>
            {
                try
                {
                    var expense = await expenses.Add(request);
                    return Results.Created($"/api/expenses/{expense.Id}", ExpenseService.ToDto(expense));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            })
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Expenses")
                .WithName("AddExpense").WithSummary("Record money spent (from the till = cash out of the open shift)");

            app.MapGet("/api/expenses", async (string? date, ExpenseService expenses) =>
            {
                DateOnly? day = null;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed)) return Results.BadRequest(new { message = "Use date=YYYY-MM-DD." });
                    day = parsed;
                }
                return Results.Ok(await expenses.List(day));
            })
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Expenses")
                .WithName("GetExpenses").WithSummary("Expenses of one business day (today by default)");

            return app;
        }
    }
}
