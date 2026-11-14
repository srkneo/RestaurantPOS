using System;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class BusinessClockTests
    {
        [Theory]
        [InlineData("2026-11-14T06:00:00Z", "2026-11-14")] // 11:30 am in India
        [InlineData("2026-11-14T18:00:00Z", "2026-11-14")] // 11:30 pm, still the 14th
        [InlineData("2026-11-14T22:00:00Z", "2026-11-14")] // 3:30 am on the 15th, before the 4 am day start
        [InlineData("2026-11-14T22:30:00Z", "2026-11-15")] // exactly 4:00 am on the 15th
        [InlineData("2026-11-14T23:00:00Z", "2026-11-15")] // 4:30 am on the 15th
        public void BusinessDate_UsesIndiaTime_AndFourAmDayStart(string utc, string expected)
        {
            var date = BusinessClock.BusinessDate(DateTime.Parse(utc).ToUniversalTime(), "Asia/Kolkata", 4);
            Assert.Equal(DateOnly.Parse(expected), date);
        }

        [Fact]
        public void UnknownTimeZone_FallsBackToIndia()
        {
            var date = BusinessClock.BusinessDate(new DateTime(2026, 11, 14, 22, 30, 0, DateTimeKind.Utc), "Not/AZone", 4);
            Assert.Equal(new DateOnly(2026, 11, 15), date);
        }

        [Fact]
        public void OutletSettings_ParseHandlesEmptyAndBadJson()
        {
            Assert.Equal(99, OutletSettings.Parse(null).MaxItemQuantity);
            Assert.Equal(99, OutletSettings.Parse("not json").MaxItemQuantity);
            Assert.Equal(20, OutletSettings.Parse("{\"maxItemQuantity\":20}").MaxItemQuantity);
            Assert.Contains("\"maxItemQuantity\":20", new OutletSettings { MaxItemQuantity = 20 }.ToJson());
        }
    }
}
