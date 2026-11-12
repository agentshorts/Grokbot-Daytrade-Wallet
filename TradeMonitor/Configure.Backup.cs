[assembly: HostingStartup(typeof(TradeMonitor.ConfigureBackup))]

namespace TradeMonitor;

public class ConfigureBackup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((context, services) =>
        {
            if (context.HostingEnvironment.IsEnvironment("Testing"))
                return;
            if (!context.Configuration.GetValue("Backup:Enabled", true))
                return;

            services.AddHostedService<DatabaseBackupHostedService>();
        });
}
