using ServiceStack.Host.NetCore;
using ServiceStack.Web;

namespace TradeMonitor;

/// <summary>
/// /mcp request logs keep the path and status, and drop the body, headers, and API key.
/// </summary>
public static class McpRequestLogRedaction
{
    public const string Flag = "trademonitor.mcp";

    public static bool IsMcp(IRequest? request)
    {
        if (request == null)
            return false;
        if (IsMcpPath(request.PathInfo) || IsMcpPath(request.AbsoluteUri))
            return true;
        return request is NetCoreRequest net && net.HttpContext.Items.ContainsKey(Flag);
    }

    public static void Apply(IRequest request, RequestLogEntry entry)
    {
        if (!IsMcp(request) && !IsMcpPath(entry.PathInfo) && !IsMcpPath(entry.AbsoluteUri))
            return;

        entry.RequestBody = null;
        entry.RequestDto = null;
        entry.ResponseDto = null;
        entry.ErrorResponse = null;
        entry.FormData = null;
        entry.Headers = [];
        entry.ResponseHeaders = null;
        entry.Items = [];
        entry.Session = null;
        entry.ExceptionData = null;
        entry.ExceptionSource = null;
        if (entry.Meta != null)
        {
            entry.Meta.Remove("apikey");
            entry.Meta.Remove("bearer");
            entry.Meta.Remove("keyname");
        }

        entry.AbsoluteUri = StripQuery(entry.AbsoluteUri);
        entry.PathInfo = StripQuery(entry.PathInfo);
        entry.Referer = null;
        entry.ForwardedFor = null;
    }

    public static bool IsMcpPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var value = path;
        var query = value.IndexOf('?');
        if (query >= 0)
            value = value[..query];
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.AbsolutePath;
        return value.Equals("/mcp", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/mcp/", StringComparison.OrdinalIgnoreCase);
    }

    static string? StripQuery(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var query = value.IndexOf('?');
        return query >= 0 ? value[..query] : value;
    }
}
