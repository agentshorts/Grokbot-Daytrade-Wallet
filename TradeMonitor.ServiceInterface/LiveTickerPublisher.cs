using ServiceStack;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public static class LiveChannels
{
    public const string TickSelector = "price.tick";

    public static string Price(string product) => "price:" + product.Trim().ToUpperInvariant();
}

// One publish reaches the in-memory cache, the price table, and every Server Events subscriber.
public sealed class LiveTickerPublisher(LiveMarketCache cache, PriceBars bars, IServerEvents events)
{
    public void Publish(LiveTick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.ProductId) || tick.Price <= 0)
            return;
        tick.ProductId = tick.ProductId.Trim().ToUpperInvariant();
        cache.PublishTick(tick);
        bars.Record(tick);
        try
        {
            events.NotifyChannel(LiveChannels.Price(tick.ProductId), LiveChannels.TickSelector, tick);
        }
        catch (Exception)
        {
            // A dropped subscriber must not stop the feed.
        }
    }
}
