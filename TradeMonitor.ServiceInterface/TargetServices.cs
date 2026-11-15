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

    public object Any(GetAtrTargets request)
    {
        var symbol = request.Symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
            throw new HttpError(400, "InvalidRequest", "Symbol is required.");
        if (!CandleInterval.TryParse(request.Interval, emptyIsDefault: true, out var interval, out var error))
            throw new HttpError(400, "InvalidInterval", error);
        var period = request.Period is > 0 and <= 200 ? request.Period.Value : 14;
        var horizon = request.HorizonBars is > 0 and <= 10000 ? request.HorizonBars.Value : 1;
        var page = bars.Window(symbol, interval, null, null, CandleInterval.Cap);
        var price = page.Candles.Count == 0 ? 0 : page.Candles[^1].Close;
        var report = AtrBands.Compute(page.Candles, price, period, horizon);
        return new GetAtrTargetsResponse
        {
            Symbol = symbol,
            Interval = page.Key,
            Period = period,
            HorizonBars = report.HorizonBars,
            Scale = report.Scale,
            Price = price,
            Atr = report.Atr,
            Notice = report.Notice,
            Bands = report.Bands.Select(band => new AtrBandDto
            {
                Multiplier = band.Multiplier,
                Upper = band.Upper,
                Lower = band.Lower,
            }).ToList(),
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
