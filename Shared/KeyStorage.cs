using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace POSSystem.Data
{
    // Sign-in cookies are protected by keys. In a container the keys must live on a volume
    // (DataProtection__KeysPath), otherwise every deploy signs everyone out.
    public static class KeyStorage
    {
        public static IServiceCollection AddPosKeyStorage(this IServiceCollection services)
        {
            services.AddDataProtection().SetApplicationName("RestaurantPOS");
            services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((options, config, loggers) =>
            {
                var path = config["DataProtection:KeysPath"];
                if (!string.IsNullOrWhiteSpace(path))
                {
                    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(path), loggers);
                }
            });
            return services;
        }
    }
}
