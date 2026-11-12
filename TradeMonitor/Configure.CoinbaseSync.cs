using TradeMonitor.ServiceInterface;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureCoinbaseSync))]

namespace TradeMonitor;

public class ConfigureCoinbaseSync : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((context, services) =>
        {
            services.Configure<CoinbaseSyncOptions>(context.Configuration.GetSection(CoinbaseSyncOptions.Section));
            services.AddSingleton<CoinbaseSyncStatus>();
            services.AddSingleton<IWindowsCredentialStore, WindowsCredentialManagerStore>();
            services.AddSingleton<ICoinbaseCredentialSource, WinVaultCoinbaseCredentialSource>();
            services.AddHostedService<CoinbaseSyncHostedService>();
        });
}
