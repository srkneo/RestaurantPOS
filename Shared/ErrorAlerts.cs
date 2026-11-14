using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace POSSystem.Alerts
{
    // Sends server errors to the owner's Telegram: at most one message a minute, up to 10 errors each.
    // Turned on only when Alerts:TelegramToken and Alerts:TelegramChatId are set (environment variables on the server).
    public static class ErrorAlerts
    {
        public static IServiceCollection AddTelegramErrorAlerts(this IServiceCollection services, IConfiguration config)
        {
            var token = config["Alerts:TelegramToken"];
            var chat = config["Alerts:TelegramChatId"];
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat)) return services;

            var queue = new ErrorAlertQueue();
            services.AddSingleton(queue);
            services.AddSingleton<ILoggerProvider>(new ErrorAlertLoggerProvider(queue));
            services.AddHttpClient("telegram-alerts");
            services.AddHostedService(sp => new ErrorAlertSender(
                queue, sp.GetRequiredService<IHttpClientFactory>(), token, chat, config["Alerts:Name"] ?? "RestaurantPOS"));
            return services;
        }
    }

    public class ErrorAlertQueue
    {
        public const int Capacity = 50;
        private readonly ConcurrentQueue<string> _items = new();
        private int _dropped;

        public void Add(string message)
        {
            if (_items.Count >= Capacity)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }
            _items.Enqueue(message);
        }

        public (List<string> Items, int Dropped) Take(int max)
        {
            var list = new List<string>();
            while (list.Count < max && _items.TryDequeue(out var item)) list.Add(item);
            var dropped = Interlocked.Exchange(ref _dropped, 0) + _items.Count;
            _items.Clear();
            return (list, dropped);
        }
    }

    public class ErrorAlertLoggerProvider : ILoggerProvider
    {
        private readonly ErrorAlertQueue _queue;
        public ErrorAlertLoggerProvider(ErrorAlertQueue queue) => _queue = queue;
        public ILogger CreateLogger(string categoryName) => new ErrorAlertLogger(categoryName, _queue);
        public void Dispose() { }
    }

    public class ErrorAlertLogger : ILogger
    {
        private readonly string _category;
        private readonly ErrorAlertQueue _queue;

        public ErrorAlertLogger(string category, ErrorAlertQueue queue)
        {
            _category = category;
            _queue = queue;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Errors only, and never from the HTTP client or this feature itself (that could loop).
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Error
            && !_category.StartsWith("System.Net.Http", StringComparison.Ordinal)
            && !_category.StartsWith("POSSystem.Alerts", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = $"{logLevel} in {_category}: {formatter(state, exception)}";
            if (exception != null) text += $" | {exception.GetType().Name}: {exception.Message}";
            _queue.Add(text.Length > 400 ? text[..400] + "..." : text);
        }
    }

    public class ErrorAlertSender : BackgroundService
    {
        private readonly ErrorAlertQueue _queue;
        private readonly IHttpClientFactory _http;
        private readonly string _token;
        private readonly string _chat;
        private readonly string _name;

        public ErrorAlertSender(ErrorAlertQueue queue, IHttpClientFactory http, string token, string chat, string name)
        {
            _queue = queue;
            _http = http;
            _token = token;
            _chat = chat;
            _name = name;
        }

        public static string Compose(string name, IReadOnlyList<string> items, int dropped)
        {
            var text = $"⚠️ {name}: {items.Count + dropped} error(s) in the last minute\n\n" + string.Join("\n\n", items.Select(i => "• " + i));
            if (dropped > 0) text += $"\n\n…and {dropped} more. See the server log.";
            return text.Length > 3900 ? text[..3900] + "…" : text;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var (items, dropped) = _queue.Take(10);
                if (items.Count == 0) continue;
                try
                {
                    var client = _http.CreateClient("telegram-alerts");
                    using var body = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["chat_id"] = _chat,
                        ["text"] = Compose(_name, items, dropped)
                    });
                    await client.PostAsync($"https://api.telegram.org/bot{_token}/sendMessage", body, stoppingToken);
                }
                catch (Exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Alerts must never break the app; the errors are still in the normal log.
                }
            }
        }
    }
}
