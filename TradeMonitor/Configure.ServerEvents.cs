using ServiceStack.Auth;
using ServiceStack.Configuration;

[assembly: HostingStartup(typeof(TradeMonitor.ConfigureServerEvents))]

namespace TradeMonitor;

// Live prices use ServiceStack Server Events. Each frame is flushed with buffering off.
// The connect message tells the browser to post a heartbeat every 15 seconds.
public class ConfigureServerEvents : IHostingStartup
{
    public const int HeartbeatSeconds = 15;

    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services =>
        {
            services.AddPlugin(new ServerEventsFeature
            {
                HeartbeatInterval = TimeSpan.FromSeconds(HeartbeatSeconds),
                IdleTimeout = TimeSpan.FromSeconds(60),
                LimitToAuthenticatedUsers = true,
                OnCreated = (sub, req) =>
                {
                    if (!IsAdmin(req))
                        req.Response.Close();
                },
            });
        });

    static bool IsAdmin(ServiceStack.Web.IRequest req)
    {
        try
        {
            var session = req.GetSession();
            return session is { IsAuthenticated: true }
                && session.HasRole(RoleNames.Admin, req.TryResolve<IAuthRepository>());
        }
        catch (Exception)
        {
            return false;
        }
    }
}
