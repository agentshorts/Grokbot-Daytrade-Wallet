using Microsoft.Extensions.DependencyInjection;
using TradeMonitor.ServiceInterface;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigurePredictions))]

namespace TradeMonitor;

public class ConfigurePredictions : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddHttpClient(PublicContractCloses.HttpName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(4);
            });
            services.AddSingleton<IContractCloses>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                return new PublicContractCloses(() => factory.CreateClient(PublicContractCloses.HttpName));
            });
        });
}
