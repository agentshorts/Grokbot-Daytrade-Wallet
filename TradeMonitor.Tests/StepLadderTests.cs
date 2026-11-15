using NUnit.Framework;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

public class StepLadderTests
{
    [Test]
    public void Sma_is_empty_until_24_bars_and_then_averages_the_window()
    {
        var candles = Enumerable.Range(1, 25).Select(volume => Bar(volume, volume)).ToList();
        var bars = StepLadder.Compute(candles);

        Assert.That(bars.Take(23).All(bar => bar.Vol24 == null), Is.True);
        Assert.That(bars.Take(23).Any(bar => bar.Step || bar.Dormant), Is.False);
        Assert.That(bars[23].Vol24, Is.EqualTo(300m / 24m));
        Assert.That(bars[24].Vol24, Is.EqualTo(324m / 24m));
    }

    [Test]
    public void No_markers_before_vol24_has_24_bars()
    {
        var candles = new List<LiveCandle> { Bar(0, 1_000_000m) };
        candles.AddRange(Enumerable.Range(1, 22).Select(index => Bar(index, 1)));
        var bars = StepLadder.Compute(candles);

        Assert.That(bars, Has.Count.EqualTo(23));
        Assert.That(bars.All(bar => bar.Vol24 == null && !bar.Step && !bar.Dormant), Is.True);
    }

    [Test]
    public void Step_bar_requires_volume_strictly_above_vol24_times_step_increase()
    {
        var below = Flat(23, 10m);
        below.Add(Bar(23, 20m));
        var quiet = StepLadder.Compute(below);
        Assert.That(quiet[23].Vol24, Is.EqualTo(250m / 24m));
        Assert.That(quiet[23].Step, Is.False);

        var above = Flat(23, 10m);
        above.Add(Bar(23, 21m));
        var loud = StepLadder.Compute(above);
        Assert.That(loud[23].Vol24, Is.EqualTo(251m / 24m));
        Assert.That(loud[23].Step, Is.True);
        Assert.That(loud[23].Dormant, Is.False);

        var equal = Flat(24, 10m);
        var same = StepLadder.Compute(equal, stepIncrease: 1m, dormantThreshold: 1m);
        Assert.That(same[23].Vol24, Is.EqualTo(10m));
        Assert.That(same[23].Step, Is.False);
        Assert.That(same[23].Dormant, Is.False);
    }

    [Test]
    public void Dormant_run_marks_on_the_greater_or_equal_boundary_and_resets()
    {
        var bars = StepLadder.Compute(Run());

        Assert.That(bars.Take(24).Any(bar => bar.Step || bar.Dormant), Is.False);
        Assert.That(bars[24].Dormant, Is.False, "first dormant bar, difference 0");
        Assert.That(bars[28].Dormant, Is.False, "fifth dormant bar, difference 4");
        Assert.That(bars[29].Dormant, Is.True, "sixth dormant bar, difference 5");
        Assert.That(bars[29].Vol24, Is.EqualTo(186m / 24m));

        Assert.That(bars[30].Step, Is.True);
        Assert.That(bars[30].Dormant, Is.False);
        Assert.That(bars[30].Vol24, Is.EqualTo(276m / 24m));

        Assert.That(bars[35].Dormant, Is.False, "fifth bar of the new run");
        Assert.That(bars[36].Dormant, Is.True, "sixth bar of the new run");
        Assert.That(bars[36].Vol24, Is.EqualTo(222m / 24m));
        Assert.That(bars[36].Step, Is.False);
    }

    [Test]
    public void Dormant_period_of_one_marks_from_the_second_bar()
    {
        var bars = StepLadder.Compute(Run(), dormantPeriod: 1);

        Assert.That(bars[24].Dormant, Is.False);
        Assert.That(bars[25].Dormant, Is.True);
        Assert.That(bars[30].Dormant, Is.False);
        Assert.That(bars[31].Dormant, Is.False);
        Assert.That(bars[32].Dormant, Is.True);
    }

    [Test]
    public void Rejects_multipliers_below_or_equal_to_zero_and_a_period_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StepLadder.Compute([], 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepLadder.Compute([], -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepLadder.Compute([], 2m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepLadder.Compute([], 2m, 0.5m, 0));
    }

    static List<LiveCandle> Run()
    {
        var candles = Flat(24, 10m);
        for (var index = 24; index <= 29; index++)
            candles.Add(Bar(index, 1m));
        candles.Add(Bar(30, 100m));
        for (var index = 31; index <= 36; index++)
            candles.Add(Bar(index, 1m));
        return candles;
    }

    static List<LiveCandle> Flat(int count, decimal volume) =>
        Enumerable.Range(0, count).Select(index => Bar(index, volume)).ToList();

    static LiveCandle Bar(int index, decimal volume) => new()
    {
        Start = index * 60L,
        Open = 1,
        High = 1,
        Low = 1,
        Close = 1,
        Volume = volume,
    };
}
