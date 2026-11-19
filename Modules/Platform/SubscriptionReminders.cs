using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POSSystem.Data;

namespace POSSystem.Platform
{
    public record ReminderTenant(string Name, string? Plan, DateOnly? PaidUntil, string? OwnerPhone);

    // Once a day at 10:00 India time the platform owner gets a Telegram list of cloud restaurants whose trial or paid
    // period ends within 3 days, that are past their paid-up date (the 7 grace days) or that stopped today.
    // Nothing is sent when the list is empty, on shop PCs (Licence:Required) or when no Telegram bot is set up.
    public static class SubscriptionReminders
    {
        public const int SoonDays = 3;
        public const int SendHour = 10;

        public static IServiceCollection AddSubscriptionReminders(this IServiceCollection services)
        {
            services.AddHostedService<SubscriptionReminderWorker>();
            return services;
        }

        public static string? Compose(IEnumerable<ReminderTenant> tenants, DateOnly today)
        {
            var soon = new List<string>();
            var due = new List<string>();
            var stopped = new List<string>();
            foreach (var t in tenants.OrderBy(t => t.PaidUntil).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (t.PaidUntil is not { } until) continue;
                var who = string.IsNullOrWhiteSpace(t.OwnerPhone) ? t.Name : $"{t.Name} ({t.OwnerPhone})";
                var left = until.DayNumber - today.DayNumber;
                var kind = t.Plan == "trial" ? "trial" : $"{t.Plan ?? "plan"} plan";
                if (left >= 0 && left <= SoonDays)
                    soon.Add($"• {who}: {kind} ends {Day(until)} ({Left(left)})");
                else if (left < 0 && -left <= Subscription.GraceDays)
                    due.Add($"• {who}: unpaid since {Day(until)}, stops {Day(until.AddDays(Subscription.GraceDays + 1))}");
                else if (-left == Subscription.GraceDays + 1)
                    stopped.Add($"• {who}: Stopped today (paid until {Day(until)})");
            }
            if (soon.Count + due.Count + stopped.Count == 0) return null;

            var text = new StringBuilder($"📅 Subscriptions on {Day(today)}");
            void Part(string title, List<string> lines)
            {
                if (lines.Count > 0) text.Append("\n\n").Append(title).Append('\n').Append(string.Join("\n", lines));
            }
            Part("Ending soon:", soon);
            Part("Payment due:", due);
            Part("Stopped:", stopped);
            var s = text.ToString();
            return s.Length > 3900 ? s[..3900] + "…" : s;
        }

        private static string Left(int days) => days == 0 ? "today" : days == 1 ? "1 day" : $"{days} days";

        private static string Day(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        // Time from now (UTC) until the next 10:00 India time. India has no daylight saving, so UTC+5:30 is exact.
        public static TimeSpan UntilNextRun(DateTime utcNow)
        {
            var india = utcNow.AddMinutes(330);
            var next = india.Date.AddHours(SendHour);
            if (india >= next) next = next.AddDays(1);
            return next - india;
        }
    }

    public class SubscriptionReminderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IOtpSender _sender;
        private readonly IConfiguration _config;
        private readonly ILogger<SubscriptionReminderWorker> _log;

        public SubscriptionReminderWorker(IServiceScopeFactory scopes, IOtpSender sender, IConfiguration config, ILogger<SubscriptionReminderWorker> log)
        {
            _scopes = scopes;
            _sender = sender;
            _config = config;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            if (!_sender.Ready || string.Equals(_config["Licence:Required"], "true", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(SubscriptionReminders.UntilNextRun(DateTime.UtcNow), stop);
                    await SendOnce(stop);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task SendOnce(CancellationToken stop)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var list = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                    .Where(t => t.IsActive && t.PaidUntil != null)
                    .Select(t => new ReminderTenant(t.Name, t.Plan, t.PaidUntil, t.OwnerPhone))
                    .ToListAsync(stop);
                var text = SubscriptionReminders.Compose(list, Subscription.Today());
                if (text != null) await _sender.SendAsync(text, stop);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "Subscription reminder failed");
            }
        }
    }
}
