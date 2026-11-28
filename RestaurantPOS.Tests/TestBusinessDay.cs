using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using POSSystem.Data;
using POSSystem.Models;

namespace RestaurantPOS.Tests
{
    // FIX1: tests that sell and then read "today" must never run across the business-day start hour
    // (4 AM IST = 22:30 UTC by default), or the shift and its bills fall into different business days.
    public static class TestBusinessDay
    {
        // A start hour 12 hours away from the outlet's local time now, so a test run never crosses the day boundary.
        public static int SafeStartHour(string? timeZoneId, DateTime utcNow)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), BusinessClock.FindZone(timeZoneId));
            return (local.Hour + 12) % 24;
        }

        // Moves the business-day start of every outlet in the test database away from the current time.
        public static WebApplicationFactory<Program> AwayFromDayStart(WebApplicationFactory<Program> factory)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var outlet in db.Outlets.ToList())
                outlet.BusinessDayStartHour = SafeStartHour(outlet.TimeZone, DateTime.UtcNow);
            db.SaveChanges();
            return factory;
        }
    }
}
