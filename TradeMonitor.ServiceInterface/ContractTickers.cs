using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradeMonitor.ServiceInterface;

// Kalshi hourly tickers encode the hour in Eastern time, for example
// KXBTCD-26NOV1212-T71499.99. The displayed clock comes from the market close
// timestamp, not from that embedded hour.
public static class ContractTickers
{
    static readonly Regex Ticker = new(
        @"KX[A-Z0-9]+-\d{2}[A-Z]{3}\d{4}-[TB]\d+(?:\.\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? KalshiTicker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var match = Ticker.Match(value.ToUpperInvariant());
        return match.Success ? match.Value : null;
    }

    public static string? CoinbaseProductId(string? value)
    {
        var ticker = KalshiTicker(value);
        return ticker == null ? null : ticker + "-KALSHI";
    }
}

// Reads a contract close from a public market payload. close_time is the
// observation time. expiration_time on these hourly markets is days later.
public static class ContractCloseReader
{
    public static DateTime? ReadKalshi(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("market", out var market) && market.ValueKind == JsonValueKind.Object)
                root = market;
            return FirstTime(root, "close_time", "expected_expiration_time", "expiration_time");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DateTime? ReadCoinbase(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FindTime(doc.RootElement, 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static DateTime? FindTime(JsonElement node, int depth)
    {
        if (node.ValueKind != JsonValueKind.Object || depth > 4)
            return null;
        var direct = FirstTime(node, "close_time", "expiration_time", "contract_expiry", "contract_expiration", "expiration");
        if (direct != null)
            return direct;
        foreach (var name in new[] { "prediction_market_product_details", "prediction_market_details", "prediction_details", "future_product_details", "product" })
        {
            if (!node.TryGetProperty(name, out var child))
                continue;
            var found = FindTime(child, depth + 1);
            if (found != null)
                return found;
        }
        return null;
    }

    static DateTime? FirstTime(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            var at = ReadTime(node, name);
            if (at != null)
                return at;
        }
        return null;
    }

    static DateTime? ReadTime(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return null;
        return DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);
    }
}
