using ServiceStack.Data;
using TradeMonitor.ServiceInterface;

namespace TradeMonitor;

public static class LedgerHealthEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", (IDbConnectionFactory dbFactory) =>
        {
            var report = LedgerHealthReport.Create(dbFactory, AppVersion.Current);
            return report.Status == "ok"
                ? Results.Json(report)
                : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }
}
