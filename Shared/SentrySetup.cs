using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sentry.AspNetCore;
using Sentry.Extensibility;

namespace POSSystem.Logging
{
    // OB3: error tracking with Sentry (free plan). Off unless SENTRY_DSN is set, so dev, tests and shop PCs send nothing.
    // Only errors (log level Error and crashes) become Sentry events; info lines are kept as breadcrumbs for context.
    public static class SentrySetup
    {
        public static string? Dsn(IConfiguration config)
        {
            var dsn = config["SENTRY_DSN"];
            return string.IsNullOrWhiteSpace(dsn) ? null : dsn.Trim();
        }

        public static void Configure(SentryAspNetCoreOptions o, string dsn, IConfiguration config)
        {
            o.Dsn = dsn;
            var env = config["SENTRY_ENVIRONMENT"];
            o.Environment = string.IsNullOrWhiteSpace(env) ? "production" : env.Trim();
            var version = config["App:Version"];
            o.Release = "restaurantpos@" + (string.IsNullOrWhiteSpace(version) ? "dev" : version.Trim());
            o.SendDefaultPii = false;                    // no cookies, IP addresses or user names
            o.MaxRequestBodySize = RequestSize.None;     // never send request bodies (orders, phone numbers)
            o.TracesSampleRate = 0;                      // traces go to Grafana (OB4); Sentry only gets errors
            o.MinimumEventLevel = LogLevel.Error;
            o.MinimumBreadcrumbLevel = LogLevel.Information;
            o.AttachStacktrace = true;
        }

        public static WebApplicationBuilder AddPosSentry(this WebApplicationBuilder builder)
        {
            var dsn = Dsn(builder.Configuration);
            if (dsn != null) builder.WebHost.UseSentry(o => Configure(o, dsn, builder.Configuration));
            return builder;
        }
    }
}
