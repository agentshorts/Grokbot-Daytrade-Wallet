using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public readonly record struct SwingPivot(decimal Price, string Side, long At);

public sealed record SwingLevel(decimal Price, string Side, int Strength, long FirstAt, long LastAt);

public sealed record SwingReport(List<SwingLevel> Levels, List<SwingLevel> Above, List<SwingLevel> Below, string Notice)
{
    public static SwingReport Empty(string notice) => new([], [], [], notice);
}

// Pivot highs and lows, clustered into support and resistance. No network calls.
public static class SwingLevels
{
    public static SwingReport Compute(IReadOnlyList<LiveCandle> candles, decimal price, int wing = 3, int lookback = 200)
    {
        if (wing < 1)
            wing = 1;
        if (lookback < 1)
            lookback = 1;
        if (candles.Count == 0 || price <= 0)
            return SwingReport.Empty("Not enough bars for swing levels.");

        var window = candles.Count <= lookback ? candles.ToList() : candles.Skip(candles.Count - lookback).ToList();
        if (window.Count < wing * 2 + 1)
            return SwingReport.Empty("Not enough bars for swing levels.");

        var pivots = FindPivots(window, wing);
        var atr = WilderAtr(window, 14);
        var tolerance = atr is decimal band && band > 0 ? band * 0.25m : price * 0.0015m;
        var levels = Cluster(pivots, tolerance);
        var nearest = Nearest(levels, price, 2);
        return new SwingReport(levels, nearest.Above, nearest.Below, "");
    }

    public static List<SwingPivot> FindPivots(IReadOnlyList<LiveCandle> candles, int wing)
    {
        var pivots = new List<SwingPivot>();
        if (wing < 1 || candles.Count < wing * 2 + 1)
            return pivots;
        for (var i = wing; i < candles.Count - wing; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var swingHigh = true;
            var swingLow = true;
            for (var k = 1; k <= wing; k++)
            {
                if (candles[i - k].High >= high || candles[i + k].High >= high)
                    swingHigh = false;
                if (candles[i - k].Low <= low || candles[i + k].Low <= low)
                    swingLow = false;
            }

            if (swingHigh)
                pivots.Add(new SwingPivot(high, "resistance", candles[i].Start));
            if (swingLow)
                pivots.Add(new SwingPivot(low, "support", candles[i].Start));
        }

        return pivots;
    }

    public static List<SwingLevel> Cluster(IReadOnlyList<SwingPivot> pivots, decimal tolerance)
    {
        var levels = new List<SwingLevel>();
        foreach (var side in new[] { "resistance", "support" })
        {
            var rows = pivots.Where(pivot => pivot.Side == side).OrderBy(pivot => pivot.Price).ToList();
            var index = 0;
            while (index < rows.Count)
            {
                var group = new List<SwingPivot> { rows[index] };
                index++;
                while (index < rows.Count && rows[index].Price - Mean(group) <= tolerance)
                {
                    group.Add(rows[index]);
                    index++;
                }

                levels.Add(new SwingLevel(
                    decimal.Round(Mean(group), 8),
                    side,
                    group.Count,
                    group.Min(pivot => pivot.At),
                    group.Max(pivot => pivot.At)));
            }
        }

        return levels.OrderBy(level => level.Price).ToList();
    }

    public static (List<SwingLevel> Above, List<SwingLevel> Below) Nearest(IReadOnlyList<SwingLevel> levels, decimal price, int take)
    {
        var above = levels.Where(level => level.Price > price).OrderBy(level => level.Price).Take(take).ToList();
        var below = levels.Where(level => level.Price < price).OrderByDescending(level => level.Price).Take(take).ToList();
        return (above, below);
    }

    public static decimal? WilderAtr(IReadOnlyList<LiveCandle> candles, int period)
    {
        if (period < 1 || candles.Count < period + 1)
            return null;
        decimal seed = 0;
        for (var i = 1; i <= period; i++)
            seed += TrueRange(candles[i], candles[i - 1].Close);
        var atr = seed / period;
        for (var i = period + 1; i < candles.Count; i++)
            atr = (atr * (period - 1) + TrueRange(candles[i], candles[i - 1].Close)) / period;
        return atr;
    }

    public static decimal TrueRange(LiveCandle candle, decimal previousClose)
    {
        var range = candle.High - candle.Low;
        var up = Math.Abs(candle.High - previousClose);
        var down = Math.Abs(candle.Low - previousClose);
        return Math.Max(range, Math.Max(up, down));
    }

    static decimal Mean(IReadOnlyList<SwingPivot> pivots)
    {
        decimal sum = 0;
        foreach (var pivot in pivots)
            sum += pivot.Price;
        return sum / pivots.Count;
    }
}
