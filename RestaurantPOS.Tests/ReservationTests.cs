using System;
using System.Globalization;
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
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Reservations: book a table for a time in the outlet's time zone, no double booking, seat, cancel, no-show.
    public class ReservationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ReservationTests(WebApplicationFactory<Program> factory) => _factory = factory;

        private (WebApplicationFactory<Program> Factory, int TableId, TimeZoneInfo Zone) Build()
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
            var table = new RestaurantTable { TableNumber = "7", QRCodeString = "t1-7", SortOrder = 7 };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            var zone = BusinessClock.FindZone(db.Outlets.Single(o => o.Id == 1).TimeZone);
            return (f, table.Id, zone);
        }

        private static HttpClient As(WebApplicationFactory<Program> f, string role, string tenant = "1")
        {
            var client = f.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
            return client;
        }

        private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }

        // Local date and time strings of the outlet, the given minutes from now (seconds dropped).
        private static (string Date, string Time) LocalIn(TimeZoneInfo zone, int minutes)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMinutes(minutes), zone);
            return (local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        [Fact]
        public async Task Book_List_Seat_AndUpcoming()
        {
            var (f, tableId, zone) = Build();
            using var factory = f;
            var waiter = As(f, "waiter");
            var soon = LocalIn(zone, 60);
            var created = await waiter.PostAsync("/api/reservations", Json(new { customerName = "Patel family", phone = "98250 12345", people = 4, date = soon.Date, time = soon.Time, tableId, note = "Birthday" }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var booking = await Read(created);
            var id = booking.GetProperty("id").GetInt32();
            Assert.Equal("7", booking.GetProperty("tableNumber").GetString());
            Assert.Equal("Booked", booking.GetProperty("status").GetString());
            Assert.Equal(soon.Time, booking.GetProperty("time").GetString());

            var day = await Read(await As(f, "cashier").GetAsync($"/api/reservations?date={soon.Date}"));
            Assert.Equal(1, day.GetArrayLength());
            Assert.Equal("Patel family", day[0].GetProperty("customerName").GetString());

            var upcoming = await Read(await waiter.GetAsync("/api/reservations/upcoming"));
            Assert.Equal(id, upcoming[0].GetProperty("id").GetInt32());

            var seated = await Read(await waiter.PostAsync($"/api/reservations/{id}/status", Json(new { status = "seated" })));
            Assert.Equal("Seated", seated.GetProperty("status").GetString());
            Assert.Equal(0, (await Read(await waiter.GetAsync("/api/reservations/upcoming"))).GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest, (await waiter.PutAsync($"/api/reservations/{id}", Json(new { customerName = "X", people = 2, date = soon.Date, time = soon.Time }))).StatusCode);

            // Undo back to booked, then cancel.
            Assert.Equal("Booked", (await Read(await waiter.PostAsync($"/api/reservations/{id}/status", Json(new { status = "Booked" })))).GetProperty("status").GetString());
            Assert.Equal("Cancelled", (await Read(await waiter.PostAsync($"/api/reservations/{id}/status", Json(new { status = "Cancelled" })))).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await waiter.PostAsync($"/api/reservations/{id}/status", Json(new { status = "NoShow" }))).StatusCode);
        }

        [Fact]
        public async Task SameTable_CannotBeBookedTwiceWithinTheSlot()
        {
            var (f, tableId, zone) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var first = LocalIn(zone, 24 * 60);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Shah", people = 2, date = first.Date, time = first.Time, tableId }))).StatusCode);

            var clash = LocalIn(zone, 24 * 60 + 60);
            var refused = await cashier.PostAsync("/api/reservations", Json(new { customerName = "Desai", people = 2, date = clash.Date, time = clash.Time, tableId }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("Table 7 is already booked", await refused.Content.ReadAsStringAsync());

            var later = LocalIn(zone, 24 * 60 + 120);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Desai", people = 2, date = later.Date, time = later.Time, tableId }))).StatusCode);
            var noTable = LocalIn(zone, 24 * 60 + 30);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Mehta", people = 6, date = noTable.Date, time = noTable.Time }))).StatusCode);
        }

        [Fact]
        public async Task BadBookings_AreRefused_AndOthersCannotSeeThem()
        {
            var (f, tableId, zone) = Build();
            using var factory = f;
            var cashier = As(f, "cashier");
            var past = LocalIn(zone, -120);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Late", people = 2, date = past.Date, time = past.Time }))).StatusCode);
            var ok = LocalIn(zone, 180);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "", people = 2, date = ok.Date, time = ok.Time }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Big", people = 0, date = ok.Date, time = ok.Time }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Phone", phone = "abc", people = 2, date = ok.Date, time = ok.Time }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Table", people = 2, date = ok.Date, time = ok.Time, tableId = 9999 }))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsync("/api/reservations", Json(new { customerName = "Joshi", people = 3, date = ok.Date, time = ok.Time, tableId }))).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await As(f, "kitchen").GetAsync("/api/reservations")).StatusCode);
            Assert.Equal(0, (await Read(await As(f, "manager", "2").GetAsync($"/api/reservations?date={ok.Date}"))).GetArrayLength());
        }
    }
}
