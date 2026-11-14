using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace POSSystem.Data
{
    // Limits against password guessing and runaway screens. Over the limit the answer is 429 Too Many Requests.
    public static class RateLimits
    {
        public const string Login = "login";
        public const int DefaultLoginPerMinute = 10;
        public const int DefaultRequestsPerMinute = 600;

        // Limits can be raised by configuration (RateLimits__LoginPerMinute), e.g. for the E2E test setup only.
        public static IServiceCollection AddPosRateLimits(this IServiceCollection services, IConfiguration config)
        {
            var loginPerMinute = int.TryParse(config["RateLimits:LoginPerMinute"], out var l) && l > 0 ? l : DefaultLoginPerMinute;
            var requestsPerMinute = int.TryParse(config["RateLimits:RequestsPerMinute"], out var r) && r > 0 ? r : DefaultRequestsPerMinute;

            services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Sign-in: 10 tries a minute per network address (default).
                o.AddPolicy(Login, ctx => RateLimitPartition.GetFixedWindowLimiter(
                    "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

                // Everything else: 600 requests (default) a minute per signed-in user, or per address when signed out.
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.Identity?.IsAuthenticated == true
                        ? "user:" + ctx.User.Identity.Name
                        : "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });
            return services;
        }
    }
}
