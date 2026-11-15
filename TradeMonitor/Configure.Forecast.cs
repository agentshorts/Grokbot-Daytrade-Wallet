using Microsoft.Extensions.Options;
using TradeMonitor.ServiceInterface;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureForecast))]

namespace TradeMonitor;

public class ConfigureForecast : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddOptions<ForecastOptions>().BindConfiguration(ForecastOptions.Section);
            services.AddSingleton<IForecastRunner>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<ForecastOptions>>().Value;
                if (!options.Enabled)
                    return new DisabledForecastRunner();
                return new OnnxForecastRunner(options.ModelPath);
            });
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<ForecastOptions>>().Value);
            services.AddSingleton<IForecastEvents, ServerForecastEvents>();
            services.AddSingleton<ForecastCache>();
            services.AddHostedService<ForecastRefreshHostedService>();
        });
}

sealed class DisabledForecastRunner : IForecastRunner
{
    public string Name => ForecastOptions.ModelName;
    public bool Ready => false;
    public ForecastBatch Run(IReadOnlyList<float[]> rows) => new();
}
