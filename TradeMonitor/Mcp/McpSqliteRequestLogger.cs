using ServiceStack.Jobs;

namespace TradeMonitor;

public class McpSqliteRequestLogger : SqliteRequestLogger, IRequireRegistration
{
    void IRequireRegistration.Register(IAppHost appHost)
    {
        base.Register(appHost);

        // RequestLogsFeature assigns its own filter during Register, after the constructor.
        // Wrap whatever it installed so /mcp rows are still redacted.
        var priorBody = RequestBodyTrackingFilter;
        RequestBodyTrackingFilter = request =>
            !McpRequestLogRedaction.IsMcp(request) && (priorBody?.Invoke(request) ?? true);

        var prior = RequestLogFilter;
        RequestLogFilter = (request, entry) =>
        {
            prior?.Invoke(request, entry);
            McpRequestLogRedaction.Apply(request, entry);
        };
    }
}
