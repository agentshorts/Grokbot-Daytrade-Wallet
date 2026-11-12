using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public interface ILiveMarket
{
    IReadOnlyList<LiveProduct> Products();
    bool IsListed(string productId);
    LiveTick? Ticker(string productId);
    IReadOnlyList<LiveCandle> Candles(string productId, string granularity);
    void Watch(string productId, string granularity);
    IDisposable Listen(Action<LiveTick> onTick);
}

public sealed class LiveMarketCache : ILiveMarket
{
    readonly object gate = new();
    List<LiveProduct> products = [];
    readonly Dictionary<string, LiveTick> ticks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<LiveCandle>> candles = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> watched = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Action<LiveTick>> listeners = [];

    public event Action<string, string>? WatchRequested;

    public IReadOnlyList<LiveProduct> Products()
    {
        lock (gate)
            return products;
    }

    public bool IsListed(string productId)
    {
        lock (gate)
            return products.Any(x => x.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase));
    }

    public LiveTick? Ticker(string productId)
    {
        lock (gate)
            return ticks.TryGetValue(productId, out var tick) ? tick : null;
    }

    public IReadOnlyList<LiveCandle> Candles(string productId, string granularity)
    {
        lock (gate)
            return candles.TryGetValue(Key(productId, granularity), out var rows) ? rows : [];
    }

    public void Watch(string productId, string granularity)
    {
        var key = granularity;
        lock (gate)
            watched.Add(productId);
        WatchRequested?.Invoke(productId, key);
    }

    public IReadOnlyList<string> Watched()
    {
        lock (gate)
            return watched.ToList();
    }

    public IDisposable Listen(Action<LiveTick> onTick)
    {
        lock (gate)
            listeners.Add(onTick);
        return new Subscription(() =>
        {
            lock (gate)
                listeners.Remove(onTick);
        });
    }

    public void PublishProducts(IReadOnlyList<LiveProduct> next)
    {
        lock (gate)
            products = next.ToList();
    }

    public void PublishTick(LiveTick tick)
    {
        Action<LiveTick>[] copy;
        lock (gate)
        {
            ticks[tick.ProductId] = tick;
            copy = listeners.ToArray();
        }

        foreach (var listener in copy)
        {
            try
            {
                listener(tick);
            }
            catch (Exception)
            {
                // A slow subscriber must not stop the feed.
            }
        }
    }

    public void PublishCandles(string productId, string granularity, IReadOnlyList<LiveCandle> rows)
    {
        lock (gate)
            candles[Key(productId, granularity)] = rows.ToList();
    }

    static string Key(string productId, string granularity) => productId.ToUpperInvariant() + "|" + granularity;

    sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class CoinbasePublicClient(HttpMessageInvoker http)
{
    public Task<string> GetProducts(CancellationToken ct) =>
        Send("https://api.exchange.coinbase.com/products", ct);

    public Task<string> GetTicker(string productId, CancellationToken ct) =>
        Send($"https://api.exchange.coinbase.com/products/{Uri.EscapeDataString(productId)}/ticker", ct);

    public Task<string> GetCandles(string productId, int granularity, CancellationToken ct) =>
        Send($"https://api.exchange.coinbase.com/products/{Uri.EscapeDataString(productId)}/candles?granularity={granularity}", ct);

    async Task<string> Send(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "TradeMonitor");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
