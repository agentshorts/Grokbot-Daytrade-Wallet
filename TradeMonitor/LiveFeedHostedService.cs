using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor;

// Relays public Coinbase prices and fills resting paper orders while the app is running.
// The Testing environment does not open a socket or call the network.
public sealed class LiveFeedHostedService(
    IHostEnvironment environment,
    IHttpClientFactory httpFactory,
    LiveMarketCache cache,
    LiveTickerPublisher publisher,
    TrackedSymbolBook symbols,
    PriceBars bars,
    PaperOrderBook orders,
    LiveSimulationStore simulations,
    ILogger<LiveFeedHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing"))
            return;

        cache.Listen(tick =>
        {
            try
            {
                orders.FillDue(tick.ProductId, tick.Price);
                simulations.FillDue(tick.ProductId, tick.Price);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not fill live orders for {Product}", tick.ProductId);
            }
        });

        await Task.WhenAll(
            RefreshProducts(stoppingToken),
            PollWatched(stoppingToken),
            RunSocket(stoppingToken));
    }

    async Task RefreshProducts(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var http = httpFactory.CreateClient("coinbase-public");
                var body = await http.GetStringAsync("https://api.exchange.coinbase.com/products", ct);
                cache.PublishProducts(CoinbaseMessages.Products(body));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not refresh the public product list");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    async Task PollWatched(CancellationToken ct)
    {
        var seeded = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        while (!ct.IsCancellationRequested)
        {
            foreach (var product in symbols.PollSymbols())
            {
                try
                {
                    using var http = httpFactory.CreateClient("coinbase-public");
                    var ticker = await http.GetStringAsync($"https://api.exchange.coinbase.com/products/{Uri.EscapeDataString(product)}/ticker", ct);
                    foreach (var tick in CoinbaseMessages.Ticks(ticker))
                        publisher.Publish(tick);
                    if (!bars.Any(product) && (!seeded.TryGetValue(product, out var tried) || clockAge(tried) > TimeSpan.FromMinutes(5)))
                    {
                        seeded[product] = DateTime.UtcNow;
                        var history = await http.GetStringAsync($"https://api.exchange.coinbase.com/products/{Uri.EscapeDataString(product)}/candles?granularity=60", ct);
                        bars.Seed(product, CoinbaseMessages.Candles(history));
                    }

                    var candles = await http.GetStringAsync($"https://api.exchange.coinbase.com/products/{Uri.EscapeDataString(product)}/candles?granularity=300", ct);
                    cache.PublishCandles(product, "5m", CoinbaseMessages.Candles(candles));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "Public poll failed for {Product}", product);
                }
            }

            bars.Flush();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    static TimeSpan clockAge(DateTime at) => DateTime.UtcNow - at;

    async Task RunSocket(CancellationToken ct)
    {
        var useExchange = true;
        while (!ct.IsCancellationRequested)
        {
            var products = symbols.PollSymbols();
            if (products.Count == 0)
            {
                await Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            var url = useExchange
                ? "wss://ws-feed.exchange.coinbase.com"
                : "wss://advanced-trade-ws.coinbase.com";
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(url), ct);
                var payload = useExchange
                    ? JsonSerializer.Serialize(new { type = "subscribe", product_ids = products, channels = new[] { "ticker" } })
                    : JsonSerializer.Serialize(new { type = "subscribe", product_ids = products, channel = "ticker" });
                await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);
                await ReadSocket(socket, products, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Public ticker socket dropped. REST polling continues.");
                useExchange = !useExchange;
                await Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    async Task ReadSocket(ClientWebSocket socket, IReadOnlyList<string> subscribed, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var builder = new StringBuilder();
        var nextCheck = DateTime.UtcNow.AddSeconds(2);
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            builder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            foreach (var tick in CoinbaseMessages.Ticks(builder.ToString()))
                publisher.Publish(tick);
            if (DateTime.UtcNow >= nextCheck)
            {
                nextCheck = DateTime.UtcNow.AddSeconds(2);
                if (!SameSet(subscribed, symbols.PollSymbols()))
                    return;
            }
        }
    }

    static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;
        var seen = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
        return right.All(seen.Contains);
    }

    static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}
