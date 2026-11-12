using ServiceStack;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public sealed class ServerForecastEvents(IServerEvents events) : IForecastEvents
{
    public const string Selector = "forecast";

    public void Publish(string symbol, GetForecastResponse report)
    {
        try
        {
            events.NotifyChannel(LiveChannels.Price(symbol), Selector, report);
        }
        catch (Exception)
        {
            // A dropped subscriber must not stop a forecast.
        }
    }
}

public class ForecastServices(PriceBars bars, ForecastCache cache) : Service
{
    public async Task<object> Any(GetForecast request)
    {
        var symbol = request.Symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
            throw new HttpError(400, "InvalidRequest", "Symbol is required.");
        if (!CandleInterval.TryParse(request.Interval, emptyIsDefault: true, out var interval, out var error))
            throw new HttpError(400, "InvalidInterval", error);
        var horizon = request.Horizon is > 0 and <= ForecastOptions.MaxHorizon ? request.Horizon.Value : 12;
        if (!cache.Enabled)
            return ForecastEngine.Notice(symbol, interval.Key, "forecast disabled");
        if (!cache.RunnerReady)
            return ForecastEngine.Notice(symbol, interval.Key, "forecast model not installed");

        var page = bars.Window(symbol, interval, null, null, CandleInterval.Cap);
        var cached = cache.Get(symbol, page.Key, horizon);
        var candleTime = page.Candles.Count == 0 ? 0 : page.Candles[^1].Start;
        if (cached.Steps.Count > 0 && cached.CandleTime == candleTime && cached.Horizon == horizon)
            return cached;

        var pending = cache.Refresh(symbol, page.Key, page.Candles, horizon, page.Seconds, CancellationToken.None);
        await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(25)));
        return cache.Get(symbol, page.Key, horizon);
    }
}
