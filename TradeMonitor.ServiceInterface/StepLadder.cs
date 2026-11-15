using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public readonly record struct StepLadderBar(long Time, decimal Volume, decimal? Vol24, bool Step, bool Dormant);

// Pine v5 step ladder. vol24 is a 24-bar SMA and stays empty until 24 bars exist.
// A step bar is volume strictly above vol24 * stepIncrease.
// A dormant bar is volume strictly below vol24 * dormantThreshold.
// The dormant run starts at the first dormant bar and clears on any other bar.
// The bar is marked when barIndex - firstDormantIndex >= dormantPeriod.
public static class StepLadder
{
    public const int Length = 24;
    public const decimal DefaultStepIncrease = 2m;
    public const decimal DefaultDormantThreshold = 0.5m;
    public const int DefaultDormantPeriod = 5;

    public static List<StepLadderBar> Compute(
        IReadOnlyList<LiveCandle> candles,
        decimal stepIncrease = DefaultStepIncrease,
        decimal dormantThreshold = DefaultDormantThreshold,
        int dormantPeriod = DefaultDormantPeriod)
    {
        if (stepIncrease <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepIncrease), "Step increase must be greater than 0.");
        if (dormantThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(dormantThreshold), "Dormant threshold must be greater than 0.");
        if (dormantPeriod < 1)
            throw new ArgumentOutOfRangeException(nameof(dormantPeriod), "Dormant period must be a whole number of at least 1.");

        var bars = new List<StepLadderBar>(candles.Count);
        int? dormantStart = null;
        for (var index = 0; index < candles.Count; index++)
        {
            var volume = candles[index].Volume;
            var vol24 = Average(candles, index);
            var step = false;
            var dormantBar = false;
            if (vol24 is decimal average)
            {
                step = volume > average * stepIncrease;
                dormantBar = volume < average * dormantThreshold;
            }

            if (dormantBar && dormantStart == null)
                dormantStart = index;
            if (!dormantBar)
                dormantStart = null;

            var marked = dormantStart != null && index - dormantStart.Value >= dormantPeriod;
            bars.Add(new StepLadderBar(candles[index].Start, volume, vol24, step, marked));
        }

        return bars;
    }

    static decimal? Average(IReadOnlyList<LiveCandle> candles, int index)
    {
        if (index + 1 < Length)
            return null;
        decimal sum = 0;
        for (var i = index - Length + 1; i <= index; i++)
            sum += candles[i].Volume;
        return sum / Length;
    }
}
