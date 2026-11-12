using System.Diagnostics;
using NUnit.Framework;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.Tests;

public class ForecastTests
{
    [Test]
    public void Window_keeps_raw_closes_on_a_patch_boundary()
    {
        var closes = Enumerable.Range(1, 40).Select(value => (decimal)value).ToList();
        var window = ForecastWindow.Take(closes, 512);
        Assert.That(window, Is.Not.Null);
        Assert.That(window!.Length, Is.EqualTo(32));
        Assert.That(window[0], Is.EqualTo(9m));
        Assert.That(window[^1], Is.EqualTo(40m));
        Assert.That(ForecastWindow.Take(Enumerable.Range(1, 20).Select(value => (decimal)value).ToList(), 512), Is.Null);
    }

    [Test]
    public void Maps_mean_and_quantiles_onto_future_bars()
    {
        var row = new[] { 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f };
        Assert.That(ForecastMap.QuantileIndex(9, 0.1m), Is.EqualTo(0));
        Assert.That(ForecastMap.QuantileIndex(9, 0.5m), Is.EqualTo(4));
        Assert.That(ForecastMap.QuantileIndex(9, 0.9m), Is.EqualTo(8));
        var steps = ForecastMap.Steps([55f, 56f, 57f, 58f], [row, row, row, row], lastBar: 1_000, stepSeconds: 300, horizon: 2);
        Assert.That(steps.Count, Is.EqualTo(2));
        Assert.That(steps[0].At, Is.EqualTo(1_300));
        Assert.That(steps[1].At, Is.EqualTo(1_600));
        Assert.That(steps[0].Mean, Is.EqualTo(55m));
        Assert.That(steps[0].Q10, Is.EqualTo(10m));
        Assert.That(steps[0].Q50, Is.EqualTo(50m));
        Assert.That(steps[0].Q90, Is.EqualTo(90m));
    }

    [Test]
    public void Coverage_counts_prices_inside_the_band()
    {
        var closes = Enumerable.Repeat(10m, 50).ToList();
        var rate = ForecastCoverage.HitRate(closes, 32, 1, sample => (sample[^1] - 1m, sample[^1] + 1m), maxOrigins: 8);
        Assert.That(rate, Is.EqualTo(1m));
        closes[^1] = 100m;
        var missed = ForecastCoverage.HitRate(closes, 32, 1, sample => (sample[^1] - 1m, sample[^1] + 1m), maxOrigins: 50);
        Assert.That(missed, Is.LessThan(1m));
        Assert.That(ForecastCoverage.Text(0.74m), Is.EqualTo("80% band hit 74% of the time, last 7 days"));
    }

    [Test]
    public void Disabled_and_missing_models_do_not_run()
    {
        var calls = 0;
        var runner = new ScriptRunner(() => calls++);
        var disabled = new ForecastCache(runner, new ForecastOptions { Enabled = false });
        Assert.That(disabled.Get("BTC-USD", "5m", 12).Notice, Is.EqualTo("forecast disabled"));
        var missing = new ForecastCache(runner, new ForecastOptions { Enabled = true });
        runner.ReadyFlag = false;
        Assert.That(missing.Get("BTC-USD", "5m", 12).Notice, Is.EqualTo("forecast model not installed"));
        Assert.That(calls, Is.EqualTo(0));
        Assert.That(ForecastFiles.ModelExists("App_Data/models/timesfm-2.5/missing.onnx"), Is.False);
    }

    [Test]
    public void Refresh_does_not_hold_the_caller_while_the_model_runs()
    {
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var runner = new GateRunner(entered, release);
        var cache = new ForecastCache(runner, new ForecastOptions { Enabled = true, ContextLength = 32, HorizonBars = 4 });
        var task = cache.Refresh("BTC-USD", "5m", Flat(40), 4, 300, CancellationToken.None);
        Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);
        var watch = Stopwatch.StartNew();
        var pending = cache.Get("BTC-USD", "5m", 4);
        watch.Stop();
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(200));
        Assert.That(pending.Notice, Is.EqualTo("forecast running"));
        Assert.That(pending.Steps, Is.Empty);
        release.Set();
        Assert.That(task.Wait(TimeSpan.FromSeconds(2)), Is.True);
        var done = cache.Get("BTC-USD", "5m", 4);
        Assert.That(done.Steps, Is.Not.Empty);
        Assert.That(done.Model, Is.EqualTo("TimesFM 2.5"));
        Assert.That(done.Steps[0].Q10, Is.LessThan(done.Steps[0].Mean));
        Assert.That(done.Steps[0].Q90, Is.GreaterThan(done.Steps[0].Mean));
    }

    static List<LiveCandle> Flat(int count) => Enumerable.Range(0, count).Select(i => new LiveCandle
    {
        Start = 1_700_000_000 + i * 300,
        Open = 10,
        High = 11,
        Low = 9,
        Close = 10,
    }).ToList();

    sealed class ScriptRunner(Action onRun) : IForecastRunner
    {
        public bool ReadyFlag { get; set; } = true;
        public string Name => ForecastOptions.ModelName;
        public bool Ready => ReadyFlag;
        public ForecastBatch Run(IReadOnlyList<float[]> rows)
        {
            onRun();
            return Batch(rows);
        }
    }

    sealed class GateRunner(ManualResetEventSlim entered, ManualResetEventSlim release) : IForecastRunner
    {
        int calls;
        public string Name => ForecastOptions.ModelName;
        public bool Ready => true;
        public ForecastBatch Run(IReadOnlyList<float[]> rows)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                release.Wait();
            }

            return Batch(rows);
        }
    }

    static ForecastBatch Batch(IReadOnlyList<float[]> rows)
    {
        var means = new float[rows.Count][];
        var quantiles = new float[rows.Count][][];
        for (var i = 0; i < rows.Count; i++)
        {
            var last = rows[i].Length == 0 ? 0 : rows[i][^1];
            means[i] = [last, last, last, last];
            quantiles[i] =
            [
                Band(last),
                Band(last),
                Band(last),
                Band(last),
            ];
        }

        return new ForecastBatch { Means = means, Quantiles = quantiles };
    }

    static float[] Band(float last) => [last - 1, last - 0.8f, last - 0.5f, last - 0.2f, last, last + 0.2f, last + 0.5f, last + 0.8f, last + 1];
}
