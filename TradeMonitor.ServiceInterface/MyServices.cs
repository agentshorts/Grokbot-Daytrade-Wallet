using ServiceStack;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public class MyServices : Service
{
    public object Any(Hello request)
    {
        return new HelloResponse { Result = $"Hello, {request.Name}!" };
    }
}
