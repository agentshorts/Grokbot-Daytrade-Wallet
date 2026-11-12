using System.Net;

namespace TradeMonitor;

/// <summary>
/// The host listens on 127.0.0.1 only. A non-loopback URL fails startup.
/// </summary>
public static class LoopbackBinding
{
    public static readonly string[] DefaultUrls =
    [
        "http://127.0.0.1:5050",
        "https://127.0.0.1:5051",
    ];

    public static void Apply(WebApplicationBuilder builder)
    {
        var urls = ReadUrls(builder.Configuration);
        var rewritten = urls.Select(ToIpv4Loopback).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (builder.Environment.IsEnvironment("Testing"))
            return;

        builder.WebHost.UseUrls(rewritten);
    }

    public static IReadOnlyList<string> ReadUrls(IConfiguration configuration)
    {
        var raw = configuration["urls"] ?? configuration["ASPNETCORE_URLS"];
        var urls = Split(raw);
        if (urls.Count == 0)
            urls = [..DefaultUrls];

        foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            var url = endpoint["Url"];
            if (!string.IsNullOrWhiteSpace(url))
                urls.Add(url.Trim());
        }

        return urls;
    }

    public static string ToIpv4Loopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException(
                $"Refusing to bind '{url}'. The host listens on 127.0.0.1 only.");

        if (!IsLoopback(uri.Host))
            throw new InvalidOperationException(
                $"Refusing to bind '{url}'. The host listens on 127.0.0.1 only.");

        var builder = new UriBuilder(uri) { Host = "127.0.0.1" };
        return builder.Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
    }

    public static bool IsLoopback(string host)
    {
        var value = host.Trim().Trim('[', ']');
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(value, out var address) && IPAddress.IsLoopback(address);
    }

    static List<string> Split(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
