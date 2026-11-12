using TradeMonitor.ServiceInterface;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureLive))]

namespace TradeMonitor;

public class ConfigureLive : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddSingleton<LiveMarketCache>();
            services.AddSingleton<ILiveMarket>(sp => sp.GetRequiredService<LiveMarketCache>());
            services.AddSingleton<PaperOrderBook>();
            services.AddSingleton<LiveSimulationStore>();
            services.AddSingleton<PriceBars>();
            services.AddSingleton<TrackedSymbolBook>();
            services.AddSingleton<LiveTickerPublisher>();
            services.AddHttpClient("coinbase-public", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TradeMonitor");
            });
            services.AddHostedService<LiveFeedHostedService>();
        });
}
