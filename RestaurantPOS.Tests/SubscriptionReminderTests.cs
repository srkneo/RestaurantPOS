using System;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // The 10 AM Telegram list for the platform owner: trials and plans ending soon, unpaid ones and ones stopped today.
    public class SubscriptionReminderTests
    {
        private static readonly DateOnly Today = new(2026, 11, 19);

        [Fact]
        public void Nothing_is_sent_when_no_restaurant_needs_attention()
        {
            var text = SubscriptionReminders.Compose(new[]
            {
                new ReminderTenant("Free Cafe", null, null, null),
                new ReminderTenant("Paid Cafe", "monthly", Today.AddDays(10), null),
                new ReminderTenant("Long Gone", "monthly", Today.AddDays(-20), null),
            }, Today);
            Assert.Null(text);
        }

        [Fact]
        public void Trial_ending_in_two_days_is_listed_with_the_owner_phone()
        {
            var text = SubscriptionReminders.Compose(new[] { new ReminderTenant("Cafe A", "trial", Today.AddDays(2), "9825012345") }, Today);
            Assert.NotNull(text);
            Assert.Contains("Subscriptions on 19 Nov 2026", text);
            Assert.Contains("Ending soon:", text);
            Assert.Contains("• Cafe A (9825012345): trial ends 21 Nov 2026 (2 days)", text);
        }

        [Fact]
        public void Plan_ending_today_says_today()
        {
            var text = SubscriptionReminders.Compose(new[] { new ReminderTenant("Cafe B", "yearly", Today, null) }, Today);
            Assert.Contains("• Cafe B: yearly plan ends 19 Nov 2026 (today)", text);
        }

        [Fact]
        public void Unpaid_restaurant_shows_when_it_stops()
        {
            var text = SubscriptionReminders.Compose(new[] { new ReminderTenant("Cafe C", "monthly", Today.AddDays(-3), null) }, Today);
            Assert.Contains("Payment due:", text);
            Assert.Contains("• Cafe C: unpaid since 16 Nov 2026, stops 24 Nov 2026", text);
        }

        [Fact]
        public void Stopped_today_is_listed_once()
        {
            var today = SubscriptionReminders.Compose(new[] { new ReminderTenant("Cafe D", "monthly", Today.AddDays(-8), null) }, Today);
            Assert.Contains("Stopped:", today);
            Assert.Contains("• Cafe D: Stopped today (paid until 11 Nov 2026)", today);
            Assert.Null(SubscriptionReminders.Compose(new[] { new ReminderTenant("Cafe D", "monthly", Today.AddDays(-9), null) }, Today));
        }

        [Theory]
        [InlineData(3, 0, 90)]     // 08:30 India time: 1.5 hours to go
        [InlineData(4, 30, 1440)]  // exactly 10:00: the next day
        [InlineData(5, 0, 1410)]   // 10:30: 23.5 hours to go
        public void Next_run_is_at_ten_in_the_morning_india_time(int utcHour, int utcMinute, int minutes)
        {
            var now = new DateTime(2026, 11, 19, utcHour, utcMinute, 0, DateTimeKind.Utc);
            Assert.Equal(TimeSpan.FromMinutes(minutes), SubscriptionReminders.UntilNextRun(now));
        }
    }
}
