using ServiceStack.Data;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureAutoQuery))]

namespace TradeMonitor;

public class ConfigureAutoQuery : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services => {
            // Enable Audit History
            services.AddSingleton<ICrudEvents>(c =>
                new OrmLiteCrudEvents(c.GetRequiredService<IDbConnectionFactory>()));

            services.AddPlugin(new AutoQueryFeature {
                MaxLimit = 1000,
            });
        })
        .ConfigureAppHost(appHost => {
            appHost.Resolve<ICrudEvents>().InitSchema();
        });
}