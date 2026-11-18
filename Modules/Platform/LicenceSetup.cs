using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace POSSystem.Platform
{
    public static class LicenceSetup
    {
        public static IServiceCollection AddPosLicence(this IServiceCollection services, IConfiguration config)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddHttpClient<ILicenceCloud, HttpLicenceCloud>(c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddSingleton<LicenceService>();
            services.AddSingleton<ShopUpdates>();
            services.AddSingleton<ShopBackups>();
            services.AddHostedService<LicenceRenewalWorker>();
            return services;
        }
    }

    // Fetches a fresh lease from the cloud every 6 hours while the PC is online (the lease lasts 7 days).
    public class LicenceRenewalWorker : BackgroundService
    {
        private readonly LicenceService _licence;
        private readonly ILogger<LicenceRenewalWorker> _log;

        public LicenceRenewalWorker(LicenceService licence, ILogger<LicenceRenewalWorker> log)
        {
            _licence = licence;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            if (!_licence.Required) return;
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stop);
                while (!stop.IsCancellationRequested)
                {
                    await RenewOnce(stop);
                    await Task.Delay(TimeSpan.FromHours(6), stop);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task RenewOnce(CancellationToken stop)
        {
            try { await _licence.RenewStored(stop); }
            catch (ArgumentException e) { _log.LogInformation("Licence not renewed: {Reason}", e.Message); }
            catch (Exception e) when (e is not OperationCanceledException) { _log.LogWarning(e, "Licence renewal failed"); }
        }
    }
}
