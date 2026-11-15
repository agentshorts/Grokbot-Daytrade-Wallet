using System.Globalization;
using System.Text.Json;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

// Parses public Coinbase payloads. No network and no API key.
public static class CoinbaseMessages
{
    public static List<LiveProduct> Products(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Array
            ? root
            : Property(root, "products");
        var products = new List<LiveProduct>();
        if (list.ValueKind != JsonValueKind.Array)
            return products;
        foreach (var item in list.EnumerateArray())
        {
            var id = Text(item, "product_id") ?? Text(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var quote = Text(item, "quote_currency_id") ?? Text(item, "quote_currency") ?? "";
            if (!quote.Equals("USD", StringComparison.OrdinalIgnoreCase))
                continue;
            var status = Text(item, "status");
            if (status != null && !status.Equals("online", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Bool(item, "trading_disabled") == true)
                continue;
            var type = Text(item, "product_type");
            if (type != null && !type.Equals("SPOT", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = Text(item, "base_name") ?? Text(item, "display_name") ?? id;
            products.Add(new LiveProduct
            {
                ProductId = id.ToUpperInvariant(),
                Base = (Text(item, "base_currency_id") ?? Text(item, "base_currency") ?? "").ToUpperInvariant(),
                Quote = "USD",
                Name = name,
                Price = Money(item, "price"),
                BaseIncrement = Money(item, "base_increment") ?? 0,
                QuoteIncrement = Money(item, "quote_increment") ?? 0,
                BaseMinSize = Money(item, "base_min_size") ?? 0,
                MinMarketFunds = Money(item, "min_market_funds") ?? 0,
            });
        }

        products.Sort((a, b) => string.Compare(a.ProductId, b.ProductId, StringComparison.Ordinal));
        return products;
    }

    public static List<LiveTick> Ticks(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ticks = new List<LiveTick>();
        var channel = Text(root, "channel");
        if (channel != null && channel.Equals("ticker", StringComparison.OrdinalIgnoreCase))
        {
            var at = Time(root, "timestamp") ?? DateTime.UtcNow;
            if (Property(root, "events").ValueKind == JsonValueKind.Array)
            {
                foreach (var ev in Property(root, "events").EnumerateArray())
                {
                    if (Property(ev, "tickers").ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var ticker in Property(ev, "tickers").EnumerateArray())
                        AddTick(ticks, ticker, at);
                }
            }
        }

        var type = Text(root, "type");
        if (type != null && type.Equals("ticker", StringComparison.OrdinalIgnoreCase))
            AddTick(ticks, root, Time(root, "time") ?? DateTime.UtcNow);

        if (ticks.Count == 0 && Money(root, "price") is decimal)
            AddTick(ticks, root, Time(root, "time") ?? DateTime.UtcNow);
        return ticks;
    }

    public static List<LiveCandle> Candles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Array ? root : Property(root, "candles");
        var candles = new List<LiveCandle>();
        if (list.ValueKind != JsonValueKind.Array)
            return candles;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().ToList();
                if (values.Count < 5)
                    continue;
                candles.Add(new LiveCandle
                {
                    Start = Long(values[0]),
                    Low = Decimal(values[1]),
                    High = Decimal(values[2]),
                    Open = Decimal(values[3]),
                    Close = Decimal(values[4]),
                    Volume = values.Count > 5 ? Decimal(values[5]) : 0,
                });
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var start = Long(Property(item, "start"));
            if (start == 0)
                continue;
            candles.Add(new LiveCandle
            {
                Start = start,
                Open = Money(item, "open") ?? 0,
                High = Money(item, "high") ?? 0,
                Low = Money(item, "low") ?? 0,
                Close = Money(item, "close") ?? 0,
                Volume = Money(item, "volume") ?? 0,
            });
        }

        candles.Sort((a, b) => a.Start.CompareTo(b.Start));
        return candles;
    }

    public static int GranularitySeconds(string? text)
    {
        var key = text?.Trim().ToLowerInvariant();
        return key switch
        {
            "1m" or "60" or "one_minute" => 60,
            "15m" or "900" or "fifteen_minute" => 900,
            "1h" or "3600" or "one_hour" => 3600,
            "1d" or "86400" or "one_day" => 86400,
            _ => 300,
        };
    }

    public static string GranularityKey(string? text) => GranularitySeconds(text) switch
    {
        60 => "1m",
        900 => "15m",
        3600 => "1h",
        86400 => "1d",
        _ => "5m",
    };

    static void AddTick(List<LiveTick> ticks, JsonElement item, DateTime at)
    {
        var id = Text(item, "product_id") ?? Text(item, "product") ?? "";
        var price = Money(item, "price");
        if (id.Length == 0 || price is not decimal value || value <= 0)
            return;
        ticks.Add(new LiveTick
        {
            ProductId = id.ToUpperInvariant(),
            Price = value,
            At = Time(item, "time") ?? at,
        });
    }

    static JsonElement Property(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return default;
        foreach (var prop in item.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }

        return default;
    }

    static string? Text(JsonElement item, string name)
    {
        var prop = Property(item, name);
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    static bool? Bool(JsonElement item, string name)
    {
        var prop = Property(item, name);
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    static decimal? Money(JsonElement item, string name)
    {
        var prop = Property(item, name);
        if (prop.ValueKind == JsonValueKind.Undefined)
            return null;
        return Decimal(prop);
    }

    static decimal Decimal(JsonElement prop)
    {
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var number))
            return number;
        if (prop.ValueKind == JsonValueKind.String
            && decimal.TryParse(prop.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    static long Long(JsonElement prop)
    {
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var number))
            return number;
        if (prop.ValueKind == JsonValueKind.String
            && long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    static DateTime? Time(JsonElement item, string name)
    {
        var text = Text(item, name);
        if (text == null)
            return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return null;
    }
}
