using TradeMonitor.ServiceInterface;
using ServiceStack.NativeTypes.TypeScript;

[assembly: HostingStartup(typeof(TradeMonitor.AppHost))]

namespace TradeMonitor;

public class AppHost() : AppHostBase("TradeMonitor"), IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices((Action<WebHostBuilderContext, IServiceCollection>)((context, services) => {
            // Configure ASP.NET Core IOC Dependencies
            services.AddSingleton(context.Configuration.GetSection(nameof(AppConfig))?.Get<AppConfig>()
                ?? new AppConfig {
                        BaseUrl = context.HostingEnvironment.IsDevelopment()
                            ? "https://127.0.0.1:5051"
                            : "http://127.0.0.1:5050",
                    });
            services.AddSingleton<ILedgerClock, SystemLedgerClock>();
            }));

    // Configure your AppHost with the necessary configuration and dependencies your App needs
    public override void Configure()
    {
        TypeScriptGenerator.InsertTsNoCheck = true;
        
        SetConfig(new HostConfig {
        });
    }
    
    // TODO: Replace with your own License Key. FREE Individual or OSS License available from: https://servicestack.net/free
    public static void RegisterKey() =>
        ServiceStack.Licensing.RegisterLicense("OSS MIT 2026 https://github.com/agentshorts/Grokbot-Daytrade-Wallet w2JKbQ+M5CWIVi7jt/Lii8HGPZVhm43cm3ZPLqh7J4hQ7mxJl/VPbgLfNmMSL7KiSXJwy9ciKLmifRB7wUA4o41ImvwzkCCtpp9GpBwRq2onkR/3fDGmD34gmWfw7ej4qi8WD7yV+Z38wr9wg6ptG1P/M8RNl0VFpB25MYt8sBM=");
}
