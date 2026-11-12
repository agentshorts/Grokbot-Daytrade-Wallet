using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public sealed record AtrBand(decimal Multiplier, decimal Upper, decimal Lower);

public sealed record AtrReport(decimal? Atr, decimal Price, int HorizonBars, decimal Scale, List<AtrBand> Bands, string Notice)
{
    public static AtrReport Empty(decimal price, string notice) => new(null, price, 1, 1m, [], notice);
}

// Wilder ATR bands around the latest price. Horizon stretches the band by sqrt(bars).
public static class AtrBands
{
    public static readonly decimal[] DefaultMultipliers = [1m, 2m];

    public static AtrReport Compute(
        IReadOnlyList<LiveCandle> candles,
        decimal price,
        int period = 14,
        int horizonBars = 1,
        IReadOnlyList<decimal>? multipliers = null)
    {
        if (period < 1)
            period = 14;
        if (horizonBars < 1)
            horizonBars = 1;
        var chosen = (multipliers == null || multipliers.Count == 0 ? DefaultMultipliers : multipliers)
            .Where(value => value > 0)
            .ToList();
        var atr = SwingLevels.WilderAtr(candles, period);
        if (atr is not decimal value || value <= 0 || price <= 0 || chosen.Count == 0)
            return new AtrReport(null, price, horizonBars, 1m, [], "Not enough bars for ATR.");

        var scale = Scale(horizonBars);
        var bands = new List<AtrBand>();
        foreach (var multiplier in chosen)
        {
            var move = multiplier * value * scale;
            bands.Add(new AtrBand(multiplier, price + move, price - move));
        }

        return new AtrReport(value, price, horizonBars, scale, bands, "");
    }

    public static decimal Scale(int horizonBars)
    {
        if (horizonBars <= 1)
            return 1m;
        return (decimal)Math.Sqrt(horizonBars);
    }
}
