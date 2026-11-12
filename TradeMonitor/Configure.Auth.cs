using ServiceStack.Auth;
using TradeMonitor.Data;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureAuth))]

namespace TradeMonitor;

public class ConfigureAuth : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services => {
            services.AddPlugin(new AuthFeature(IdentityAuth.For<ApplicationUser>(options => {
                options.SessionFactory = () => new CustomUserSession();
                options.CredentialsAuth();
                options.AdminUsersFeature();
                options.LoginPath = "/signin";
            })));
        });
}