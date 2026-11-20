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

namespace POSSystem.Loyalty
{
    // A regular guest who agreed at the counter to be remembered for loyalty points. Nothing is stored without that consent,
    // and Forget removes the guest and their points history (DPDP Act: consent and erasure). Shared by all outlets of a restaurant.
    public class Customer : TenantEntity
    {
        public int Id { get; set; }
        [MaxLength(60)] public string Name { get; set; } = string.Empty;
        [MaxLength(15)] public string Phone { get; set; } = string.Empty;
        public DateTime ConsentAt { get; set; }
        [MaxLength(256)] public string? ConsentBy { get; set; }
        public int Points { get; set; }
        public int Visits { get; set; }
        public decimal Spent { get; set; }
        public DateTime? LastVisitAt { get; set; }
    }

    public enum LoyaltyKind { Earn, Redeem, Reverse, Adjust }

    // One change to a guest's points (earned on a bill, spent, reversed or corrected by hand).
    public class LoyaltyMove : TenantEntity
    {
        public long Id { get; set; }
        public int CustomerId { get; set; }
        public int? BillId { get; set; }
        public LoyaltyKind Kind { get; set; }
        public int Points { get; set; }
        public int BalanceAfter { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
        [MaxLength(256)] public string? By { get; set; }
        [MaxLength(100)] public string? Note { get; set; }
    }

    public record CustomerRequest(string? Name, string? Phone, bool? Consent);
    public record CustomerDto(int Id, string Name, string Phone, int Points, int Visits, decimal Spent, DateTime? LastVisitAt, DateTime ConsentAt);
    public record LoyaltyMoveDto(long Id, string Kind, int Points, int BalanceAfter, DateTime At, string? Note);
    public record LoyaltySettingsDto(bool Enabled, decimal RupeesPerPoint, decimal PointValue);

    public class LoyaltyException : Exception
    {
        public string Field { get; }
        public LoyaltyException(string field, string message) : base(message) => Field = field;
    }

    public static class LoyaltyRules
    {
        // Indian mobile numbers are kept as their last 10 digits (+91 98250 12345 and 09825012345 are the same guest).
        public static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal)) digits = digits[2..];
            else if (digits.Length == 11 && digits.StartsWith("0", StringComparison.Ordinal)) digits = digits[1..];
            return digits.Length is >= 6 and <= 15 ? digits : null;
        }

        public static int PointsFor(decimal total, decimal rupeesPerPoint) =>
            rupeesPerPoint <= 0 || total <= 0 ? 0 : (int)Math.Floor(total / rupeesPerPoint);

        // Called when a bill becomes paid: a guest who agreed to loyalty gets a visit and points. Saved together with the bill.
        public static async Task EarnAsync(AppDbContext db, Bill bill)
        {
            var phone = NormalizePhone(bill.CustomerPhone);
            if (phone == null) return;
            var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == bill.OutletId);
            var settings = outlet?.GetSettings();
            if (settings is not { LoyaltyEnabled: true }) return;
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Phone == phone);
            if (customer == null) return;
            var now = DateTime.UtcNow;
            customer.Visits++;
            customer.Spent += bill.Total;
            customer.LastVisitAt = now;
            customer.UpdatedAt = now;
            var points = PointsFor(bill.Total, settings.LoyaltyRupeesPerPoint);
            if (points <= 0) return;
            customer.Points += points;
            db.LoyaltyMoves.Add(new LoyaltyMove
            {
                CustomerId = customer.Id, BillId = bill.Id, Kind = LoyaltyKind.Earn, Points = points, BalanceAfter = customer.Points,
                At = now, By = db.CurrentUserId, Note = bill.InvoiceNumber
            });
        }
    }

    public class CustomerService
    {
        public const int MaxList = 200;
        private readonly AppDbContext _db;
        public CustomerService(AppDbContext db) => _db = db;

        private static CustomerDto ToDto(Customer c) => new(c.Id, c.Name, c.Phone, c.Points, c.Visits, c.Spent, c.LastVisitAt, c.ConsentAt);

        public async Task<List<CustomerDto>> List(string? search)
        {
            var q = _db.Customers.AsQueryable();
            var text = search?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var lower = text.ToLower();
                var digits = new string(text.Where(char.IsDigit).ToArray());
                q = digits.Length >= 3
                    ? q.Where(c => c.Phone.Contains(digits) || c.Name.ToLower().Contains(lower))
                    : q.Where(c => c.Name.ToLower().Contains(lower));
            }
            var list = await q.OrderByDescending(c => c.LastVisitAt ?? c.ConsentAt).ThenBy(c => c.Name).Take(MaxList).ToListAsync();
            return list.Select(ToDto).ToList();
        }

        public async Task<CustomerDto?> ByPhone(string? phone)
        {
            var p = LoyaltyRules.NormalizePhone(phone);
            if (p == null) return null;
            var c = await _db.Customers.FirstOrDefaultAsync(x => x.Phone == p);
            return c == null ? null : ToDto(c);
        }

        private async Task<(string Name, string Phone)> Check(CustomerRequest r, int? id)
        {
            var name = r.Name?.Trim() ?? "";
            if (name.Length is 0 or > 60) throw new LoyaltyException("name", "Enter the guest's name (up to 60 characters).");
            var phone = LoyaltyRules.NormalizePhone(r.Phone) ?? throw new LoyaltyException("phone", "Enter the guest's mobile number.");
            if (await _db.Customers.AnyAsync(c => c.Phone == phone && c.Id != (id ?? 0)))
                throw new LoyaltyException("phone", "A guest with this mobile number is already in the list.");
            return (name, phone);
        }

        public async Task<CustomerDto> Add(CustomerRequest r)
        {
            if (r.Consent != true) throw new LoyaltyException("consent", "Ask the guest first: they must agree to be remembered for loyalty points.");
            var (name, phone) = await Check(r, null);
            var customer = new Customer { Name = name, Phone = phone, ConsentAt = DateTime.UtcNow, ConsentBy = _db.CurrentUserId };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
            return ToDto(customer);
        }

        public async Task<CustomerDto?> Update(int id, CustomerRequest r)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id);
            if (customer == null) return null;
            var (name, phone) = await Check(r, id);
            customer.Name = name;
            customer.Phone = phone;
            await _db.SaveChangesAsync();
            return ToDto(customer);
        }

        // The guest asked to be forgotten: their details and points history are deleted. Bills keep what was printed on them.
        public async Task<bool> Forget(int id)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id);
            if (customer == null) return false;
            _db.LoyaltyMoves.RemoveRange(await _db.LoyaltyMoves.Where(m => m.CustomerId == id).ToListAsync());
            _db.Customers.Remove(customer);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<LoyaltyMoveDto>?> Moves(int id)
        {
            if (!await _db.Customers.AnyAsync(x => x.Id == id)) return null;
            var moves = await _db.LoyaltyMoves.Where(m => m.CustomerId == id).OrderByDescending(m => m.At).ThenByDescending(m => m.Id)
                .Take(50).ToListAsync();
            return moves.Select(m => new LoyaltyMoveDto(m.Id, m.Kind.ToString(), m.Points, m.BalanceAfter, m.At, m.Note)).ToList();
        }

        private Task<Outlet> MyOutlet() =>
            _db.Outlets.FirstAsync(o => o.Id == _db.CurrentOutletId && o.TenantId == _db.CurrentTenantId);

        public async Task<LoyaltySettingsDto> GetSettings()
        {
            var s = (await MyOutlet()).GetSettings();
            return new LoyaltySettingsDto(s.LoyaltyEnabled, s.LoyaltyRupeesPerPoint, s.LoyaltyPointValue);
        }

        public async Task<LoyaltySettingsDto> SaveSettings(LoyaltySettingsDto dto)
        {
            if (dto.RupeesPerPoint is < 1m or > 10000m) throw new LoyaltyException("rupeesPerPoint", "Enter 1 to 10000 rupees for one point.");
            if (dto.PointValue is < 0m or > 100m) throw new LoyaltyException("pointValue", "Enter 0 to 100 rupees for one point.");
            var outlet = await MyOutlet();
            var s = outlet.GetSettings();
            s.LoyaltyEnabled = dto.Enabled;
            s.LoyaltyRupeesPerPoint = Math.Round(dto.RupeesPerPoint, 0, MidpointRounding.AwayFromZero);
            s.LoyaltyPointValue = Math.Round(dto.PointValue, 2, MidpointRounding.AwayFromZero);
            outlet.Settings = s.ToJson();
            await _db.SaveChangesAsync();
            return new LoyaltySettingsDto(s.LoyaltyEnabled, s.LoyaltyRupeesPerPoint, s.LoyaltyPointValue);
        }
    }

    public static class LoyaltyEndpoints
    {
        private static readonly string[] Counter = { "cashier", "manager", "admin" };
        private static readonly string[] Managers = { "manager", "admin" };

        private static IResult Invalid(LoyaltyException e) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { [e.Field] = new[] { e.Message } });

        public static WebApplication MapLoyaltyEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/customers").WithTags("Customers").RequireAuthorization(p => p.RequireRole(Counter));

            group.MapGet("/", async (string? search, CustomerService customers) => Results.Ok(await customers.List(search)))
                .WithName("GetCustomers").WithSummary("Loyalty guests, last visit first (search by name or mobile)");

            group.MapGet("/by-phone", async (string? phone, CustomerService customers) =>
                await customers.ByPhone(phone) is { } found ? Results.Ok(found) : Results.NotFound())
                .WithName("GetCustomerByPhone").WithSummary("The loyalty guest with this mobile number");

            group.MapPost("/", async (CustomerRequest request, CustomerService customers) =>
            {
                try
                {
                    var created = await customers.Add(request);
                    return Results.Created($"/api/customers/{created.Id}", created);
                }
                catch (LoyaltyException e) { return Invalid(e); }
            }).WithName("AddCustomer").WithSummary("Add a guest who agreed to loyalty points");

            group.MapPut("/{id:int}", async (int id, CustomerRequest request, CustomerService customers) =>
            {
                try
                {
                    var updated = await customers.Update(id, request);
                    return updated == null ? Results.NotFound() : Results.Ok(updated);
                }
                catch (LoyaltyException e) { return Invalid(e); }
            }).WithName("UpdateCustomer").WithSummary("Change a guest's name or mobile number");

            group.MapDelete("/{id:int}", async (int id, CustomerService customers) =>
                await customers.Forget(id) ? Results.NoContent() : Results.NotFound())
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithName("ForgetCustomer").WithSummary("Forget a guest and their points (on the guest's request)");

            group.MapGet("/{id:int}/moves", async (int id, CustomerService customers) =>
                await customers.Moves(id) is { } moves ? Results.Ok(moves) : Results.NotFound())
                .WithName("GetCustomerPoints").WithSummary("Last 50 changes to a guest's points");

            group.MapGet("/settings", async (CustomerService customers) => Results.Ok(await customers.GetSettings()))
                .WithName("GetLoyaltySettings").WithSummary("Loyalty on or off, rupees for one point and the value of a point");

            group.MapPut("/settings", async (LoyaltySettingsDto request, CustomerService customers) =>
            {
                try { return Results.Ok(await customers.SaveSettings(request)); }
                catch (LoyaltyException e) { return Invalid(e); }
            }).RequireAuthorization(p => p.RequireRole(Managers))
              .WithName("SaveLoyaltySettings").WithSummary("Turn loyalty on or off and set the points rate");

            return app;
        }
    }
}
