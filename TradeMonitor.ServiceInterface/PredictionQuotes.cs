using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public sealed class PredictionBar
{
    public long At { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
}

public sealed class PredictionQuote
{
    public string Source { get; init; } = "none";
    public decimal? Price { get; init; }
    public decimal? YesBid { get; init; }
    public decimal? YesAsk { get; init; }
    public decimal? NoBid { get; init; }
    public decimal? NoAsk { get; init; }
    public decimal? Last { get; init; }
    public decimal? Volume { get; init; }
    public decimal? OpenInterest { get; init; }
    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Status { get; init; }
    public decimal? Strike { get; init; }

    // True when the source has no NO bid or ask. The NO price is then 1 minus YES.
    public bool NoImplied { get; init; }

    public DateTime? CloseAt { get; init; }
    public List<PredictionBar> Bars { get; init; } = [];
    public string? Error { get; init; }

    public static PredictionQuote Failed(string error) => new() { Source = "none", Error = error, NoImplied = true };
}

public sealed record MarketBook(
    decimal? YesBid,
    decimal? YesAsk,
    decimal? NoBid,
    decimal? NoAsk,
    decimal? Last,
    decimal? Volume,
    decimal? OpenInterest,
    string? Title,
    string? Subtitle,
    string? Status,
    decimal? Strike)
{
    public static MarketBook Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, null);
}

public interface IPredictionQuotes
{
    Task<PredictionQuote> Load(MappedTicker ticker, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}

// Public market data only. Coinbase candles and ticker are tried first.
// Kalshi candlesticks are the fallback. No API key is read and no order path is called.
public sealed class PublicPredictionQuotes : IPredictionQuotes
{
    public const string HttpName = "prediction-quotes";
    const string CoinbaseRoot = "https://api.coinbase.com/api/v3/brokerage/market/products/";
    const string KalshiRoot = "https://api.elections.kalshi.com/trade-api/v2/";

    readonly Func<HttpClient> clients;
    readonly Dictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);

    public PublicPredictionQuotes(HttpClient client)
        : this(() => client)
    {
    }

    public PublicPredictionQuotes(Func<HttpClient> clients)
    {
        this.clients = clients;
    }

    public async Task<PredictionQuote> Load(MappedTicker ticker, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = Floor(fromUtc);
        var to = Floor(toUtc);
        if (to <= from)
            to = from.AddMinutes(1);
        var key = ticker.Ticker + "|" + Unix(from) + "|" + Unix(to);
        lock (cache)
        {
            if (cache.TryGetValue(key, out var hit) && hit.Until > DateTime.UtcNow)
                return hit.Quote;
        }

        PredictionQuote quote;
        try
        {
            quote = await Fetch(ticker, from, to, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            quote = PredictionQuote.Failed("Price history is unavailable.");
        }

        var until = DateTime.UtcNow.AddSeconds(quote.Bars.Count == 0 ? 20 : 30);
        lock (cache)
            cache[key] = new CacheEntry(quote, until);
        return quote;
    }

    async Task<PredictionQuote> Fetch(MappedTicker ticker, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var coinbase = await TryCoinbase(ticker, from, to, cancellationToken).ConfigureAwait(false);
        if (coinbase != null)
            return coinbase;
        var kalshi = await TryKalshi(ticker, from, to, cancellationToken).ConfigureAwait(false);
        return kalshi ?? PredictionQuote.Failed("Price history is unavailable.");
    }

    async Task<PredictionQuote?> TryCoinbase(MappedTicker ticker, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var span = to - from;
        var url = CoinbaseRoot + ticker.ProductId + "/candles?start=" + Unix(from) + "&end=" + Unix(to)
            + "&granularity=" + OscillationWindow.CoinbaseGranularity(span) + "&limit=350";
        var candles = await Get(url, cancellationToken).ConfigureAwait(false);
        if (candles.Status != 200 || candles.Body == null)
            return null;
        var bars = PredictionBars.FromCoinbase(candles.Body);
        if (bars.Count == 0)
            return null;

        decimal? price = null;
        decimal? bid = null;
        decimal? ask = null;
        var tickerBody = await Get(CoinbaseRoot + ticker.ProductId + "/ticker?limit=1", cancellationToken).ConfigureAwait(false);
        if (tickerBody.Status == 200 && tickerBody.Body != null)
        {
            price = PredictionBars.CoinbasePrice(tickerBody.Body);
            (bid, ask) = PredictionBars.CoinbaseBook(tickerBody.Body);
        }

        DateTime? close = null;
        string? title = null;
        var product = await Get(CoinbaseRoot + ticker.ProductId, cancellationToken).ConfigureAwait(false);
        if (product.Status == 200 && product.Body != null)
        {
            close = ContractCloseReader.ReadCoinbase(product.Body);
            title = PredictionBars.CoinbaseTitle(product.Body);
        }

        var last = price ?? bars[^1].Close;
        return new PredictionQuote
        {
            Source = "coinbase",
            Price = last,
            Last = price,
            YesBid = bid,
            YesAsk = ask,
            NoImplied = true,
            Title = title,
            CloseAt = close,
            Bars = bars,
        };
    }

    async Task<PredictionQuote?> TryKalshi(MappedTicker ticker, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var period = OscillationWindow.KalshiPeriodMinutes(to - from);
        var candlesUrl = KalshiRoot + "series/" + ticker.Series + "/markets/" + ticker.Ticker
            + "/candlesticks?start_ts=" + Unix(from) + "&end_ts=" + Unix(to) + "&period_interval=" + period;
        var candles = await Get(candlesUrl, cancellationToken).ConfigureAwait(false);
        var market = await Get(KalshiRoot + "markets/" + ticker.Ticker, cancellationToken).ConfigureAwait(false);
        if ((candles.Status != 200 || candles.Body == null) && (market.Status != 200 || market.Body == null))
            return null;

        var bars = candles.Status == 200 && candles.Body != null ? PredictionBars.FromKalshi(candles.Body) : [];
        var body = market.Status == 200 ? market.Body : null;
        var book = body != null ? PredictionBars.KalshiMarket(body) : MarketBook.Empty;
        decimal? price = book.Last;
        if (price == null && body != null)
            price = PredictionBars.KalshiLastPrice(body);
        price ??= bars.Count == 0 ? null : bars[^1].Close;
        return new PredictionQuote
        {
            Source = "kalshi",
            Price = price,
            Last = book.Last,
            YesBid = book.YesBid,
            YesAsk = book.YesAsk,
            NoBid = book.NoBid,
            NoAsk = book.NoAsk,
            Volume = book.Volume,
            OpenInterest = book.OpenInterest,
            Title = book.Title,
            Subtitle = book.Subtitle,
            Status = book.Status,
            Strike = book.Strike,
            NoImplied = book.NoBid == null && book.NoAsk == null,
            CloseAt = body == null ? null : ContractCloseReader.ReadKalshi(body),
            Bars = bars,
            Error = bars.Count == 0 ? "No price history in this window." : null,
        };
    }

    async Task<ReadResult> Get(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Allowed(uri))
            throw new InvalidOperationException("Prediction quotes only read public market data.");

        var http = clients();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", "TradeMonitor");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is < 200 or >= 300)
            return new ReadResult(status, null);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (body.Length > 2_000_000)
            body = body[..2_000_000];
        return new ReadResult(status, body);
    }

    static bool Allowed(Uri uri)
    {
        var host = uri.Host;
        var coinbase = host.Equals("api.coinbase.com", StringComparison.OrdinalIgnoreCase);
        var kalshi = host.Equals("api.elections.kalshi.com", StringComparison.OrdinalIgnoreCase);
        if (!coinbase && !kalshi)
            return false;
        var path = uri.AbsolutePath;
        if (path.Contains("order", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
            return false;
        return true;
    }

    static DateTime Floor(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    readonly record struct CacheEntry(PredictionQuote Quote, DateTime Until);

    readonly record struct ReadResult(int Status, string? Body);
}

public static class PredictionBars
{
    public static List<PredictionBar> FromCoinbase(string json)
    {
        try
        {
            return CoinbaseMessages.Candles(json)
                .Select(candle => new PredictionBar
                {
                    At = candle.Start,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static decimal? CoinbasePrice(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("trades", out var trades) && trades.ValueKind == JsonValueKind.Array)
            {
                decimal? latest = null;
                foreach (var trade in trades.EnumerateArray())
                {
                    var price = First(trade, "price");
                    if (price is > 0 and <= 1.5m)
                        latest = price;
                }

                if (latest != null)
                    return latest;
            }

            var direct = First(root, "price");
            if (direct is > 0 and <= 1.5m)
                return direct;
            return Mid(First(root, "best_bid"), First(root, "best_ask"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static List<PredictionBar> FromKalshi(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candlesticks", out var list) || list.ValueKind != JsonValueKind.Array)
                return [];

            var raw = new List<RawBar>();
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("end_period_ts", out var stamp) || stamp.ValueKind != JsonValueKind.Number)
                    continue;
                var at = stamp.GetInt64();
                var price = Child(item, "price");
                var bid = Child(item, "yes_bid");
                var ask = Child(item, "yes_ask");
                raw.Add(new RawBar(
                    at,
                    Dollars(price, "high_dollars", "high"),
                    Dollars(price, "low_dollars", "low"),
                    Dollars(price, "close_dollars", "close"),
                    Dollars(price, "previous_dollars", "previous"),
                    Dollars(bid, "close_dollars", "close"),
                    Dollars(ask, "close_dollars", "close")));
            }

            raw.Sort((a, b) => a.At.CompareTo(b.At));
            var anyTrade = raw.Any(row => row.Close != null);
            var bars = new List<PredictionBar>(raw.Count);
            decimal? carry = null;
            foreach (var row in raw)
            {
                decimal close;
                decimal high;
                decimal low;
                if (row.Close is decimal traded)
                {
                    close = traded;
                    high = row.High ?? traded;
                    low = row.Low ?? traded;
                    if (high < low)
                        (high, low) = (low, high);
                    carry = close;
                }
                else if (anyTrade)
                {
                    var flat = row.Previous ?? carry;
                    if (flat == null)
                        continue;
                    close = high = low = flat.Value;
                    carry = close;
                }
                else
                {
                    var mid = Mid(row.Bid, row.Ask) ?? row.Previous;
                    if (mid == null)
                        continue;
                    close = high = low = mid.Value;
                }

                if (close < 0 || close > 1.5m)
                    continue;
                bars.Add(new PredictionBar { At = row.At, High = high, Low = low, Close = close });
            }

            return bars;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static MarketBook KalshiMarket(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("market", out var market) && market.ValueKind == JsonValueKind.Object)
                root = market;
            return new MarketBook(
                Quote(root, "yes_bid_dollars", "yes_bid"),
                Quote(root, "yes_ask_dollars", "yes_ask"),
                Quote(root, "no_bid_dollars", "no_bid"),
                Quote(root, "no_ask_dollars", "no_ask"),
                Quote(root, "last_price_dollars", "last_price"),
                Count(root, "volume_fp", "volume"),
                Count(root, "open_interest_fp", "open_interest"),
                Text(root, "title"),
                Text(root, "subtitle") ?? Text(root, "yes_sub_title"),
                Text(root, "status"),
                First(root, "floor_strike") ?? First(root, "cap_strike"));
        }
        catch (JsonException)
        {
            return MarketBook.Empty;
        }
    }

    public static (decimal? Bid, decimal? Ask) CoinbaseBook(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (QuotePrice(First(root, "best_bid")), QuotePrice(First(root, "best_ask")));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public static string? CoinbaseTitle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FindTitle(doc.RootElement, 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static decimal? KalshiLastPrice(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("market", out var market) && market.ValueKind == JsonValueKind.Object)
                root = market;
            var last = Dollars(root, "last_price_dollars", "last_price");
            if (last is > 0 and <= 1.5m)
                return last;
            return Mid(Dollars(root, "yes_bid_dollars", "yes_bid"), Dollars(root, "yes_ask_dollars", "yes_ask"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static JsonElement Child(JsonElement node, string name)
    {
        if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var child))
            return child;
        return default;
    }

    static decimal? Dollars(JsonElement node, string dollarsName, string centsName)
    {
        var dollars = First(node, dollarsName);
        if (dollars != null)
            return dollars;
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(centsName, out var raw))
            return null;
        var cents = AsDecimal(raw);
        if (cents == null)
            return null;
        // Legacy Kalshi fields are integer cents. Dollar strings use the *_dollars name.
        if (raw.ValueKind == JsonValueKind.Number)
            return cents.Value / 100m;
        return cents > 1.5m ? cents / 100m : cents;
    }

    static decimal? First(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value))
            return null;
        return AsDecimal(value);
    }

    static decimal? AsDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    static decimal? Quote(JsonElement node, string dollarsName, string centsName) => QuotePrice(Dollars(node, dollarsName, centsName));

    static decimal? QuotePrice(decimal? value) => value is > 0 and <= 1.5m ? value : null;

    static decimal? Count(JsonElement node, string fpName, string legacyName)
    {
        var fp = First(node, fpName);
        if (fp is >= 0)
            return fp;
        var legacy = First(node, legacyName);
        return legacy is >= 0 ? legacy : null;
    }

    static string? Text(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    static string? FindTitle(JsonElement node, int depth)
    {
        if (node.ValueKind != JsonValueKind.Object || depth > 3)
            return null;
        foreach (var name in new[] { "title", "display_name", "name" })
        {
            var text = Text(node, name);
            if (text != null)
                return text;
        }

        foreach (var name in new[] { "product", "prediction_market_product_details" })
        {
            if (!node.TryGetProperty(name, out var child))
                continue;
            var found = FindTitle(child, depth + 1);
            if (found != null)
                return found;
        }

        return null;
    }

    static decimal? Mid(decimal? bid, decimal? ask)
    {
        if (bid is decimal left && ask is decimal right && right >= left)
            return (left + right) / 2m;
        return ask ?? bid;
    }

    readonly record struct RawBar(long At, decimal? High, decimal? Low, decimal? Close, decimal? Previous, decimal? Bid, decimal? Ask);
}
