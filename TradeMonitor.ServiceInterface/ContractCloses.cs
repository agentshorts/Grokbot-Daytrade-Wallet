namespace TradeMonitor.ServiceInterface;

public interface IContractCloses
{
    DateTime? CloseAt(string productOrKey);
}

public sealed class NoContractCloses : IContractCloses
{
    public static readonly NoContractCloses Instance = new();

    public DateTime? CloseAt(string productOrKey) => null;
}

// Read-only lookup of a prediction contract's close time. Kalshi's public
// market API is the source. Coinbase's public product endpoint is the fallback.
// Results are cached. No trading calls, and nothing here is logged.
public sealed class PublicContractCloses : IContractCloses
{
    public const string HttpName = "prediction-public";
    const string KalshiRoot = "https://api.elections.kalshi.com/trade-api/v2/markets/";
    const string CoinbaseRoot = "https://api.coinbase.com/api/v3/brokerage/market/products/";

    readonly Func<HttpClient> clients;
    readonly Dictionary<string, Entry> cache = new(StringComparer.Ordinal);
    DateTime kalshiDownUntil = DateTime.MinValue;

    public PublicContractCloses(HttpClient client)
        : this(() => client)
    {
    }

    public PublicContractCloses(Func<HttpClient> clients)
    {
        this.clients = clients;
    }

    public DateTime? CloseAt(string productOrKey)
    {
        var ticker = ContractTickers.KalshiTicker(productOrKey);
        if (ticker == null)
            return null;
        lock (cache)
        {
            if (cache.TryGetValue(ticker, out var hit) && hit.Until > DateTime.UtcNow)
                return hit.CloseAt;
        }

        DateTime? close = null;
        try
        {
            close = Fetch(ticker);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            close = null;
        }

        var until = DateTime.UtcNow.Add(close == null ? TimeSpan.FromMinutes(2) : TimeSpan.FromHours(12));
        lock (cache)
            cache[ticker] = new Entry(close, until);
        return close;
    }

    DateTime? Fetch(string ticker)
    {
        if (DateTime.UtcNow >= kalshiDownUntil)
        {
            var kalshi = Read(KalshiRoot + ticker);
            if (kalshi.Status == 200 && kalshi.Body != null)
            {
                var close = ContractCloseReader.ReadKalshi(kalshi.Body);
                if (close != null)
                    return close;
            }
            else if (kalshi.Status == 0 || kalshi.Status >= 500)
            {
                kalshiDownUntil = DateTime.UtcNow.AddMinutes(2);
            }
        }

        var product = ContractTickers.CoinbaseProductId(ticker);
        if (product == null)
            return null;
        var coinbase = Read(CoinbaseRoot + product);
        if (coinbase.Status == 200 && coinbase.Body != null)
            return ContractCloseReader.ReadCoinbase(coinbase.Body);
        return null;
    }

    ReadResult Read(string url)
    {
        var http = clients();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "TradeMonitor");
        using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        var status = (int)response.StatusCode;
        if (status is < 200 or >= 300)
            return new ReadResult(status, null);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (body.Length > 65536)
            body = body[..65536];
        return new ReadResult(status, body);
    }

    readonly record struct Entry(DateTime? CloseAt, DateTime Until);

    readonly record struct ReadResult(int Status, string? Body);
}
