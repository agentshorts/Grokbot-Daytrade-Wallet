using NUnit.Framework;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

public class AtrBandTests
{
    [Test]
    public void Wilder_atr_matches_the_hand_series()
    {
        var candles = Series();
        Assert.That(SwingLevels.TrueRange(candles[1], candles[0].Close), Is.EqualTo(3m));
        Assert.That(SwingLevels.TrueRange(candles[2], candles[1].Close), Is.EqualTo(4m));
        Assert.That(SwingLevels.TrueRange(candles[3], candles[2].Close), Is.EqualTo(4m));
        Assert.That(SwingLevels.TrueRange(candles[4], candles[3].Close), Is.EqualTo(6m));

        var report = AtrBands.Compute(candles, 47m, period: 3, horizonBars: 1);
        Assert.That(report.Notice, Is.Empty);
        Assert.That(report.Atr, Is.EqualTo(40m / 9m).Within(0.0000001m));
        Assert.That(report.Scale, Is.EqualTo(1m));
        Assert.That(report.Bands.Select(band => band.Multiplier), Is.EqualTo(new[] { 1m, 2m }));
        Assert.That(report.Bands[0].Upper - 47m, Is.EqualTo(report.Atr!.Value).Within(0.0000001m));
        Assert.That(report.Bands[0].Lower, Is.EqualTo(47m - report.Atr.Value).Within(0.0000001m));
        Assert.That(report.Bands[1].Upper - 47m, Is.EqualTo(report.Atr.Value * 2m).Within(0.0000001m));
    }

    [Test]
    public void Horizon_scales_the_band_by_the_square_root()
    {
        var report = AtrBands.Compute(Series(), 100m, period: 3, horizonBars: 4);
        Assert.That(report.Scale, Is.EqualTo(2m));
        Assert.That(report.HorizonBars, Is.EqualTo(4));
        Assert.That((report.Bands[0].Upper - 100m) / report.Atr!.Value, Is.EqualTo(2m).Within(0.0000001m));
        Assert.That((100m - report.Bands[1].Lower) / report.Atr.Value, Is.EqualTo(4m).Within(0.0000001m));
    }

    [Test]
    public void Short_history_returns_no_bands()
    {
        var report = AtrBands.Compute([Candle(10, 9, 10), Candle(11, 9, 10)], 10m, period: 14, horizonBars: 1);
        Assert.That(report.Atr, Is.Null);
        Assert.That(report.Bands, Is.Empty);
        Assert.That(report.Notice, Does.Contain("Not enough"));
        Assert.That(AtrBands.Compute([], 10m).Notice, Does.Contain("Not enough"));
    }

    static List<LiveCandle> Series() =>
    [
        Candle(48, 45, 46),
        Candle(47, 44, 45),
        Candle(46, 42, 43),
        Candle(45, 41, 42),
        Candle(48, 44, 47),
    ];

    static LiveCandle Candle(decimal high, decimal low, decimal close) => new()
    {
        High = high,
        Low = low,
        Close = close,
        Open = close,
    };
}
