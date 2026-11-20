using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NLog;
using System;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace POSSystem.Logging
{
    // OB1: every request gets a short ID. It goes back in the X-Request-ID header, into every log line written
    // while the request runs, and into error responses, so a support call ("error ID 3f9c...") finds the log lines.
    public static class RequestIds
    {
        public const string Header = "X-Request-ID";

        // An ID sent by a caller (load test, proxy) is kept only if it is short and plain; anything else is replaced.
        private static readonly Regex Safe = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

        public static bool IsSafe(string? id) => !string.IsNullOrEmpty(id) && Safe.IsMatch(id);

        public static string Pick(string? incoming) =>
            IsSafe(incoming) ? incoming! : Guid.NewGuid().ToString("N").Substring(0, 16);

        public static IApplicationBuilder UseRequestIds(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var id = Pick(context.Request.Headers[Header].ToString());
                context.TraceIdentifier = id;
                // OnStarting so the header survives the error handler clearing the response.
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[Header] = id;
                    return Task.CompletedTask;
                });
                using (ScopeContext.PushProperty("RequestId", id))
                    await next();
            });

        // After sign-in: restaurant and user on every log line (IDs only, never names, phones or passwords).
        public static IApplicationBuilder UseRequestUserInLogs(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var tenant = context.User?.FindFirst("tenant_id")?.Value ?? "";
                var user = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                using (ScopeContext.PushProperty("Tenant", tenant))
                using (ScopeContext.PushProperty("UserId", user))
                    await next();
            });

        public static void AddToProblem(ProblemDetailsContext ctx) =>
            ctx.ProblemDetails.Extensions["requestId"] = ctx.HttpContext.TraceIdentifier;
    }
}
