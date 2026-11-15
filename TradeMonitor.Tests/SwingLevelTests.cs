using NUnit.Framework;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

public class SwingLevelTests
{
    [Test]
    public void Finds_swing_highs_and_lows()
    {
        var candles = new[]
        {
            Candle(0, 10, 9),
            Candle(60, 14, 8),
            Candle(120, 11, 10),
            Candle(180, 16, 7),
            Candle(240, 12, 9),
            Candle(300, 15, 6),
            Candle(360, 11, 8),
        };
        var pivots = SwingLevels.FindPivots(candles, 1);
        Assert.That(pivots.Where(x => x.Side == "resistance").Select(x => x.Price), Is.EqualTo(new[] { 14m, 16m, 15m }));
        Assert.That(pivots.Where(x => x.Side == "support").Select(x => x.Price), Is.EqualTo(new[] { 8m, 7m, 6m }));
    }

    [Test]
    public void Clusters_pivots_inside_the_tolerance()
    {
        var pivots = new[]
        {
            new SwingPivot(100m, "resistance", 1),
            new SwingPivot(100.1m, "resistance", 2),
            new SwingPivot(90m, "support", 3),
            new SwingPivot(80m, "support", 4),
        };
        var levels = SwingLevels.Cluster(pivots, 0.25m);
        var resistance = levels.Single(x => x.Side == "resistance");
        Assert.That(resistance.Price, Is.EqualTo(100.05m));
        Assert.That(resistance.Strength, Is.EqualTo(2));
        Assert.That(resistance.FirstAt, Is.EqualTo(1));
        Assert.That(resistance.LastAt, Is.EqualTo(2));
        Assert.That(levels.Count(x => x.Side == "support"), Is.EqualTo(2));
    }

    [Test]
    public void Nearest_keeps_two_above_and_two_below()
    {
        var levels = new[]
        {
            Level(90, "support"),
            Level(95, "support"),
            Level(99, "support"),
            Level(110, "resistance"),
            Level(120, "resistance"),
            Level(130, "resistance"),
        };
        var nearest = SwingLevels.Nearest(levels, 100, 2);
        Assert.That(nearest.Above.Select(x => x.Price), Is.EqualTo(new[] { 110m, 120m }));
        Assert.That(nearest.Below.Select(x => x.Price), Is.EqualTo(new[] { 99m, 95m }));
    }

    [Test]
    public void Compute_uses_a_fraction_of_price_when_atr_is_unavailable()
    {
        var candles = new List<LiveCandle>();
        for (var i = 0; i < 9; i++)
            candles.Add(Flat(i * 60, 100));
        candles[2] = Candle(120, 110, 100, 100);
        candles[6] = Candle(360, 100, 90, 100);
        var report = SwingLevels.Compute(candles, 100, wing: 1, lookback: 200);
        Assert.That(report.Notice, Is.Empty);
        Assert.That(report.Above.Select(x => x.Price), Is.EqualTo(new[] { 110m }));
        Assert.That(report.Below.Select(x => x.Price), Is.EqualTo(new[] { 90m }));
        Assert.That(report.Above[0].Strength, Is.EqualTo(1));
    }

    [Test]
    public void Empty_and_short_histories_return_no_levels()
    {
        Assert.That(SwingLevels.Compute([], 100).Notice, Does.Contain("Not enough"));
        Assert.That(SwingLevels.Compute([Flat(0, 10), Flat(60, 10)], 10, wing: 3).Levels, Is.Empty);
        Assert.That(SwingLevels.FindPivots([Flat(0, 1)], 3), Is.Empty);
    }

    static SwingLevel Level(decimal price, string side) => new(price, side, 1, 0, 0);

    static LiveCandle Flat(long start, decimal price) => Candle(start, price, price, price);

    static LiveCandle Candle(long start, decimal high, decimal low, decimal close = 0) => new()
    {
        Start = start,
        Open = close == 0 ? high : close,
        High = high,
        Low = low,
        Close = close == 0 ? high : close,
    };
}
