using ServiceStack;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public class TargetServices(PriceBars bars) : Service
{
    public object Any(GetSwingTargets request)
    {
        var symbol = request.Symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
            throw new HttpError(400, "InvalidRequest", "Symbol is required.");
        if (!CandleInterval.TryParse(request.Interval, emptyIsDefault: true, out var interval, out var error))
            throw new HttpError(400, "InvalidInterval", error);
        var wing = request.Wing is > 0 and <= 20 ? request.Wing.Value : 3;
        var lookback = request.Lookback is > 0 and <= 2000 ? request.Lookback.Value : 200;
        var page = bars.Window(symbol, interval, null, null, Math.Max(lookback, CandleInterval.Cap));
        var price = page.Candles.Count == 0 ? 0 : page.Candles[^1].Close;
        var report = SwingLevels.Compute(page.Candles, price, wing, lookback);
        return new GetSwingTargetsResponse
        {
            Symbol = symbol,
            Interval = page.Key,
            Price = price,
            Notice = report.Notice,
            Levels = report.Levels.Select(Dto).ToList(),
            Nearest = new SwingNearestDto
            {
                Above = report.Above.Select(Dto).ToList(),
                Below = report.Below.Select(Dto).ToList(),
            },
        };
    }

    static SwingLevelDto Dto(SwingLevel level) => new()
    {
        Price = level.Price,
        Side = level.Side,
        Strength = level.Strength,
        FirstAt = level.FirstAt,
        LastAt = level.LastAt,
    };
}
