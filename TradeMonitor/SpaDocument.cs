namespace TradeMonitor;

/// <summary>
/// Browser navigations to Vue routes that share a path with a read API.
/// JSON clients keep the API. A document request gets the SPA.
/// </summary>
public static class SpaDocument
{
    static readonly string[] Paths = ["/wallets", "/trades"];

    public static bool ShouldServe(string method, string path, string? accept, bool hasFormat)
    {
        if (hasFormat) return false;
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrEmpty(accept) || accept.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) < 0) return false;

        var clean = path.TrimEnd('/');
        if (clean.Length == 0) clean = "/";
        foreach (var candidate in Paths)
        {
            if (clean.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
