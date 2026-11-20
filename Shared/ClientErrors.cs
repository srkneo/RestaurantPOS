using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using POSSystem.Data;
using System.Text;

namespace POSSystem.Logging
{
    public record ClientErrorReport(string? Message, string? Stack, string? Url, string? Source);

    // OB2: the browser reports its own errors here; they only go to the server log (and later to monitoring).
    // No sign-in needed (errors on the login and guest pages count too), limited per network address, small bodies only.
    public static class ClientErrors
    {
        public const int MaxMessage = 500;
        public const int MaxStack = 4000;
        public const int MaxUrl = 300;
        public const int MaxSource = 40;
        public const int MaxAgent = 200;

        // Cuts to length and removes control characters, so a report cannot fake extra log lines.
        public static string Cut(string? s, int max, bool keepNewLines = false)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(System.Math.Min(s.Length, max));
            foreach (var ch in s)
            {
                if (sb.Length >= max) break;
                if (ch == '\n' && keepNewLines) sb.Append('\n');
                else if (char.IsControl(ch)) sb.Append(' ');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        public static WebApplication MapClientErrorEndpoints(this WebApplication app)
        {
            app.MapPost("/api/client-errors", (ClientErrorReport report, HttpContext ctx, ILoggerFactory loggers) =>
            {
                if (string.IsNullOrWhiteSpace(report.Message)) return Results.BadRequest();
                var log = loggers.CreateLogger("BrowserErrors");
                log.LogWarning("Browser error: {BrowserMessage} on {PagePath} ({Source}) | {UserAgent}\n{BrowserStack}",
                    Cut(report.Message, MaxMessage),
                    Cut(report.Url, MaxUrl),
                    Cut(report.Source, MaxSource),
                    Cut(ctx.Request.Headers.UserAgent.ToString(), MaxAgent),
                    Cut(report.Stack, MaxStack, keepNewLines: true));
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Qr)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024))
            .WithName("ReportClientError")
            .WithSummary("Browser error report (written to the server log only)");
            return app;
        }
    }
}
