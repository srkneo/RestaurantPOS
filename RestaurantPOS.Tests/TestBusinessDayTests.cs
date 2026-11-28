using System;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // FIX1: the test helper keeps every test run inside one business day, whatever the time of day.
    public class TestBusinessDayTests
    {
        [Theory]
        [InlineData("Asia/Kolkata")]
        [InlineData("UTC")]
        [InlineData(null)]
        public void SafeStartHour_KeepsTheNextTwoHoursInOneBusinessDay(string? zone)
        {
            var start = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
            for (var minutes = 0; minutes < 24 * 60; minutes += 15)
            {
                var now = start.AddMinutes(minutes);
                var hour = TestBusinessDay.SafeStartHour(zone, now);
                Assert.InRange(hour, 0, 23);
                Assert.Equal(BusinessClock.BusinessDate(now, zone, hour), BusinessClock.BusinessDate(now.AddHours(2), zone, hour));
            }
        }

        [Fact]
        public void DefaultStartHour_SplitsARunThatCrosses4AmIst_TheSafeHourDoesNot()
        {
            // 22:29 UTC = 03:59 IST: two minutes later the default 4 AM start already gives the next business day (the CI failure).
            var now = new DateTime(2026, 11, 27, 22, 29, 0, DateTimeKind.Utc);
            Assert.Equal(new DateOnly(2026, 11, 27), BusinessClock.BusinessDate(now, "Asia/Kolkata", BusinessClock.DefaultDayStartHour));
            Assert.Equal(new DateOnly(2026, 11, 28), BusinessClock.BusinessDate(now.AddMinutes(2), "Asia/Kolkata", BusinessClock.DefaultDayStartHour));

            var safe = TestBusinessDay.SafeStartHour("Asia/Kolkata", now);
            Assert.Equal(15, safe);
            Assert.Equal(BusinessClock.BusinessDate(now, "Asia/Kolkata", safe), BusinessClock.BusinessDate(now.AddMinutes(2), "Asia/Kolkata", safe));
        }
    }
}
