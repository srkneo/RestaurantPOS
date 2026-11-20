using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Globalization;

namespace POSSystem.Logging
{
    // OB4: traces and metrics to Grafana Cloud (free plan) over OTLP. Off unless OTEL_EXPORTER_OTLP_ENDPOINT is set.
    // The exporter reads OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_HEADERS and OTEL_EXPORTER_OTLP_PROTOCOL itself.
    public static class TelemetrySetup
    {
        public const double DefaultTraceRatio = 0.1;

        public static bool Enabled(IConfiguration config) =>
            !string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        // Share of requests traced (0 to 1). Missing or invalid = 10%, so the free plan's limits are not reached.
        public static double TraceRatio(IConfiguration config)
        {
            var raw = config["TELEMETRY_TRACE_RATIO"];
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) || double.IsNaN(r))
                return DefaultTraceRatio;
            return r < 0 ? 0 : r > 1 ? 1 : r;
        }

        // Health checks and long-lived live-update connections would only add noise.
        public static bool IsNoise(PathString path) =>
            path.StartsWithSegments("/health") || path.StartsWithSegments("/orderHub");

        public static WebApplicationBuilder AddPosTelemetry(this WebApplicationBuilder builder)
        {
            if (!Enabled(builder.Configuration)) return builder;
            var version = builder.Configuration["App:Version"];
            var ratio = TraceRatio(builder.Configuration);

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService("restaurantpos-api",
                    serviceVersion: string.IsNullOrWhiteSpace(version) ? "dev" : version.Trim()))
                .WithTracing(t => t
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !IsNoise(ctx.Request.Path))
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql")
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio)))
                    .AddOtlpExporter())
                .WithMetrics(m => m
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("System.Runtime", "Npgsql")
                    .AddOtlpExporter());
            return builder;
        }
    }
}
