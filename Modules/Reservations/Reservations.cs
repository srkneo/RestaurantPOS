using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;

namespace POSSystem.Reservations
{
    public enum ReservationStatus { Booked, Seated, Cancelled, NoShow }

    // A table booking: guest name, optional phone, people, local date and time of the outlet, optional table.
    public class Reservation : TenantEntity
    {
        public int Id { get; set; }
        [MaxLength(60)] public string CustomerName { get; set; } = string.Empty;
        [MaxLength(20)] public string? Phone { get; set; }
        public int People { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public DateTime AtUtc { get; set; }
        public int? TableId { get; set; }
        [MaxLength(200)] public string? Note { get; set; }
        public ReservationStatus Status { get; set; } = ReservationStatus.Booked;
    }

    public record ReservationRequest(string? CustomerName, string? Phone, int? People, string? Date, string? Time, int? TableId, string? Note);
    public record ReservationStatusRequest(string? Status);
    public record ReservationDto(int Id, string CustomerName, string? Phone, int People, DateOnly Date, string Time, int? TableId, string? TableNumber,
        string? Note, string Status, DateTime AtUtc);

    public class ReservationException : Exception
    {
        public string Field { get; }
        public ReservationException(string field, string message) : base(message) => Field = field;
    }

    public class ReservationService
    {
        // A booked table is kept free this long around each booking.
        public const int SlotMinutes = 90;
        public const int MaxPeople = 100;
        public const int MaxDaysAhead = 365;
        private static readonly Regex PhoneRule = new(@"^\+?[\d\s-]{6,20}$", RegexOptions.Compiled);

        private readonly AppDbContext _db;
        public ReservationService(AppDbContext db) => _db = db;

        private IQueryable<Reservation> Mine => _db.Reservations.Where(r => r.OutletId == _db.CurrentOutletId);

        private async Task<TimeZoneInfo> Zone()
        {
            var tz = await _db.Outlets.Where(o => o.Id == _db.CurrentOutletId).Select(o => o.TimeZone).FirstOrDefaultAsync();
            return BusinessClock.FindZone(tz);
        }

        public async Task<DateOnly> LocalToday() =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await Zone()));

        private async Task<List<ReservationDto>> ToDtos(List<Reservation> list)
        {
            var tableIds = list.Where(r => r.TableId != null).Select(r => r.TableId!.Value).Distinct().ToList();
            var tables = await _db.RestaurantTables.Where(t => tableIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TableNumber);
            return list.Select(r => new ReservationDto(r.Id, r.CustomerName, r.Phone, r.People, r.Date, r.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                r.TableId, r.TableId is { } id && tables.TryGetValue(id, out var n) ? n : null, r.Note, r.Status.ToString(), r.AtUtc)).ToList();
        }

        public async Task<List<ReservationDto>> List(DateOnly date) =>
            await ToDtos(await Mine.Where(r => r.Date == date).OrderBy(r => r.Time).ThenBy(r => r.Id).ToListAsync());

        // Booked reservations from 30 minutes ago up to the next few hours (for the POS screen).
        public async Task<List<ReservationDto>> Upcoming(int minutes)
        {
            var now = DateTime.UtcNow;
            var until = now.AddMinutes(Math.Clamp(minutes, 15, 24 * 60));
            var from = now.AddMinutes(-30);
            return await ToDtos(await Mine.Where(r => r.Status == ReservationStatus.Booked && r.AtUtc >= from && r.AtUtc <= until)
                .OrderBy(r => r.AtUtc).ToListAsync());
        }

        private async Task<Reservation> Fill(Reservation target, ReservationRequest r, int? id)
        {
            var name = r.CustomerName?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 60) throw new ReservationException("customerName", "Enter the guest's name (up to 60 characters).");
            var phone = r.Phone?.Trim();
            if (string.IsNullOrEmpty(phone)) phone = null;
            else if (!PhoneRule.IsMatch(phone)) throw new ReservationException("phone", "Enter a phone number with digits only, or leave it empty.");
            var people = r.People ?? 0;
            if (people < 1 || people > MaxPeople) throw new ReservationException("people", $"Enter 1 to {MaxPeople} people.");
            if (!DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ReservationException("date", "Choose a date.");
            if (!TimeOnly.TryParseExact(r.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new ReservationException("time", "Choose a time.");
            var zone = await Zone();
            var atUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), zone);
            var now = DateTime.UtcNow;
            if (atUtc < now.AddMinutes(-15)) throw new ReservationException("time", "Choose a time that has not passed.");
            if (atUtc > now.AddDays(MaxDaysAhead)) throw new ReservationException("date", "Bookings can be made up to a year ahead.");
            var note = r.Note?.Trim();
            if (note is { Length: > 200 }) throw new ReservationException("note", "The note can be at most 200 characters.");

            if (r.TableId is { } tableId)
            {
                var table = await _db.RestaurantTables.FirstOrDefaultAsync(t => t.Id == tableId && t.OutletId == _db.CurrentOutletId)
                    ?? throw new ReservationException("tableId", "Choose a table of this outlet.");
                var from = atUtc.AddMinutes(-SlotMinutes);
                var to = atUtc.AddMinutes(SlotMinutes);
                var clash = await Mine.Where(x => x.Id != (id ?? 0) && x.TableId == tableId && x.Status == ReservationStatus.Booked
                    && x.AtUtc > from && x.AtUtc < to).OrderBy(x => x.AtUtc).FirstOrDefaultAsync();
                if (clash != null)
                    throw new ReservationException("tableId", $"Table {table.TableNumber} is already booked at {clash.Time.ToString("HH:mm", CultureInfo.InvariantCulture)} for {clash.CustomerName}.");
            }

            target.CustomerName = name;
            target.Phone = phone;
            target.People = people;
            target.Date = date;
            target.Time = time;
            target.AtUtc = atUtc;
            target.TableId = r.TableId;
            target.Note = string.IsNullOrEmpty(note) ? null : note;
            return target;
        }

        public async Task<ReservationDto> Add(ReservationRequest r)
        {
            var reservation = await Fill(new Reservation(), r, null);
            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync();
            return (await ToDtos(new List<Reservation> { reservation }))[0];
        }

        public async Task<ReservationDto?> Update(int id, ReservationRequest r)
        {
            var reservation = await Mine.FirstOrDefaultAsync(x => x.Id == id);
            if (reservation == null) return null;
            if (reservation.Status != ReservationStatus.Booked) throw new ReservationException("status", "Only a booked reservation can be changed.");
            await Fill(reservation, r, id);
            await _db.SaveChangesAsync();
            return (await ToDtos(new List<Reservation> { reservation }))[0];
        }

        // Booked -> Seated, Cancelled or NoShow; any of those back to Booked to undo a mistake.
        public async Task<ReservationDto?> SetStatus(int id, string? status)
        {
            if (!Enum.TryParse<ReservationStatus>(status, true, out var next) || int.TryParse(status, out _))
                throw new ReservationException("status", "Choose booked, seated, cancelled or no-show.");
            var reservation = await Mine.FirstOrDefaultAsync(x => x.Id == id);
            if (reservation == null) return null;
            var ok = reservation.Status == ReservationStatus.Booked ? next != ReservationStatus.Booked : next == ReservationStatus.Booked;
            if (!ok) throw new ReservationException("status", $"A {reservation.Status} reservation cannot become {next}.");
            reservation.Status = next;
            await _db.SaveChangesAsync();
            return (await ToDtos(new List<Reservation> { reservation }))[0];
        }
    }

    public static class ReservationEndpoints
    {
        private static readonly string[] Staff = { "waiter", "cashier", "manager", "admin" };

        private static IResult Invalid(ReservationException e) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { [e.Field] = new[] { e.Message } });

        public static WebApplication MapReservationEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/reservations").WithTags("Reservations").RequireAuthorization(p => p.RequireRole(Staff));

            group.MapGet("/", async (string? date, ReservationService reservations) =>
            {
                DateOnly day;
                if (string.IsNullOrWhiteSpace(date)) day = await reservations.LocalToday();
                else if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                    return Results.BadRequest(new { message = "Use date=YYYY-MM-DD." });
                return Results.Ok(await reservations.List(day));
            }).WithName("GetReservations").WithSummary("Reservations of one day (today in the outlet's time zone by default)");

            group.MapGet("/upcoming", async (int? minutes, ReservationService reservations) =>
                Results.Ok(await reservations.Upcoming(minutes ?? 120)))
                .WithName("GetUpcomingReservations").WithSummary("Booked reservations from 30 minutes ago to the next hours");

            group.MapPost("/", async (ReservationRequest request, ReservationService reservations) =>
            {
                try
                {
                    var created = await reservations.Add(request);
                    return Results.Created($"/api/reservations/{created.Id}", created);
                }
                catch (ReservationException e) { return Invalid(e); }
            }).WithName("AddReservation").WithSummary("Book a table (the table is optional)");

            group.MapPut("/{id:int}", async (int id, ReservationRequest request, ReservationService reservations) =>
            {
                try
                {
                    var updated = await reservations.Update(id, request);
                    return updated == null ? Results.NotFound() : Results.Ok(updated);
                }
                catch (ReservationException e) { return Invalid(e); }
            }).WithName("UpdateReservation").WithSummary("Change a booked reservation");

            group.MapPost("/{id:int}/status", async (int id, ReservationStatusRequest request, ReservationService reservations) =>
            {
                try
                {
                    var updated = await reservations.SetStatus(id, request.Status);
                    return updated == null ? Results.NotFound() : Results.Ok(updated);
                }
                catch (ReservationException e) { return Invalid(e); }
            }).WithName("SetReservationStatus").WithSummary("Seat, cancel or mark a no-show (or undo back to booked)");

            return app;
        }
    }
}
