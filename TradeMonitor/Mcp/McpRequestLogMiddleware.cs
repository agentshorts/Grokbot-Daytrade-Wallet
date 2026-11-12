using System.Diagnostics;
using ServiceStack.Web;

namespace TradeMonitor;

/// <summary>
/// A service request with no fields. It exists so the request log keeps the /mcp call
/// (the logger ignores requests that have no DTO) without storing the call body.
/// </summary>
public sealed class McpRequestLog;

/// <summary>
/// Writes a redacted /mcp row to the ServiceStack request log.
/// </summary>
public class McpRequestLogMiddleware(RequestDelegate next, ILogger<McpRequestLogMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context, IRequestLogger requestLogger)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context);
            return;
        }

        context.Items[McpRequestLogRedaction.Flag] = true;
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            try
            {
                var request = context.ToRequest();
                requestLogger.Log(request, new McpRequestLog(), response: null!, Stopwatch.GetElapsedTime(start));
            }
            catch (Exception ex)
            {
                log.LogWarning("MCP request log was not saved: {Message}", ex.Message);
            }
        }
    }
}
