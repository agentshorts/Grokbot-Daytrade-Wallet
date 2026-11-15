using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public sealed class ForecastOptions
{
    public const string Section = "Forecast";
    public const string ModelName = "TimesFM 2.5";
    public const int Patch = 32;
    public const int MaxHorizon = 128;
    public const int KeepSeconds = 7 * 24 * 3600;

    public bool Enabled { get; set; }
    public string ModelPath { get; set; } = "App_Data/models/timesfm-2.5/model.onnx";
    public int ContextLength { get; set; } = 512;
    public int HorizonBars { get; set; } = 12;
}

public sealed class ForecastBatch
{
    public float[][] Means { get; init; } = [];
    public float[][][] Quantiles { get; init; } = [];
}

public interface IForecastRunner
{
    string Name { get; }
    bool Ready { get; }
    ForecastBatch Run(IReadOnlyList<float[]> rows);
}

public interface IForecastEvents
{
    void Publish(string symbol, GetForecastResponse report);
}

public static class ForecastFiles
{
    public static bool ModelExists(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}

// The ONNX graph already normalizes past_values. This window stays in raw closes.
public static class ForecastWindow
{
    public static decimal[]? Take(IReadOnlyList<decimal> closes, int context)
    {
        if (context < ForecastOptions.Patch)
            context = ForecastOptions.Patch;
        if (closes.Count < ForecastOptions.Patch)
            return null;
        var length = Math.Min(context, closes.Count);
        length -= length % ForecastOptions.Patch;
        if (length < ForecastOptions.Patch)
            return null;
        var start = closes.Count - length;
        var window = new decimal[length];
        for (var i = 0; i < length; i++)
            window[i] = closes[start + i];
        return window;
    }

    public static List<LiveCandle> Recent(IReadOnlyList<LiveCandle> candles, long now, int context, int keepSeconds = ForecastOptions.KeepSeconds)
    {
        if (candles.Count == 0)
            return [];
        var cutoff = now - keepSeconds;
        var first = 0;
        while (first < candles.Count && candles[first].Start < cutoff)
            first++;
        var start = Math.Max(0, first - Math.Max(context, ForecastOptions.Patch));
        return candles.Skip(start).ToList();
    }
}

public static class ForecastMap
{
    public static int QuantileIndex(int count, decimal level)
    {
        if (count <= 1)
            return 0;
        if (count == 9)
            return Math.Clamp((int)(level * 10m) - 1, 0, 8);
        return Math.Clamp((int)Math.Round((double)level * (count - 1)), 0, count - 1);
    }

    public static decimal Level(IReadOnlyList<float> row, decimal level)
    {
        if (row.Count == 0)
            return 0;
        return (decimal)row[QuantileIndex(row.Count, level)];
    }

    public static List<ForecastStepDto> Steps(float[] mean, float[][] quantiles, long lastBar, int stepSeconds, int horizon)
    {
        var count = Math.Min(horizon, Math.Min(mean.Length, quantiles.Length));
        if (stepSeconds < 1)
            stepSeconds = 60;
        var steps = new List<ForecastStepDto>(count);
        for (var i = 0; i < count; i++)
        {
            var row = quantiles[i] ?? [];
            steps.Add(new ForecastStepDto
            {
                At = lastBar + (long)(i + 1) * stepSeconds,
                Mean = (decimal)mean[i],
                Q10 = Level(row, 0.1m),
                Q30 = Level(row, 0.3m),
                Q50 = Level(row, 0.5m),
                Q70 = Level(row, 0.7m),
                Q90 = Level(row, 0.9m),
            });
        }

        return steps;
    }
}

public static class ForecastCoverage
{
    public const int MaxOrigins = 4;

    public static decimal? HitRate(
        IReadOnlyList<decimal> closes,
        int context,
        int horizon,
        Func<IReadOnlyList<decimal>, (decimal Low, decimal High)> band,
        int maxOrigins = MaxOrigins)
    {
        if (horizon < 1 || closes.Count < ForecastOptions.Patch + horizon)
            return null;
        var origins = new List<int>();
        for (var origin = ForecastOptions.Patch; origin + horizon <= closes.Count; origin++)
        {
            if (ForecastWindow.Take(closes.Take(origin).ToList(), context) != null)
                origins.Add(origin);
        }

        if (origins.Count == 0)
            return null;
        if (maxOrigins > 0 && origins.Count > maxOrigins)
            origins = Sample(origins, maxOrigins);

        var hits = 0;
        foreach (var origin in origins)
        {
            var history = new decimal[origin];
            for (var i = 0; i < origin; i++)
                history[i] = closes[i];
            var sample = ForecastWindow.Take(history, context);
            if (sample == null)
                continue;
            var (low, high) = band(sample);
            var actual = closes[origin + horizon - 1];
            if (actual >= Math.Min(low, high) && actual <= Math.Max(low, high))
                hits++;
        }

        return origins.Count == 0 ? null : (decimal)hits / origins.Count;
    }

    public static string Text(decimal rate)
    {
        var pct = decimal.Round(rate * 100m, 0, MidpointRounding.AwayFromZero);
        return $"80% band hit {pct}% of the time, last 7 days";
    }

    static List<int> Sample(List<int> origins, int take)
    {
        var picked = new List<int>(take);
        for (var i = 0; i < take; i++)
        {
            var index = (int)Math.Round((double)i * (origins.Count - 1) / (take - 1));
            if (picked.Count == 0 || picked[^1] != origins[index])
                picked.Add(origins[index]);
        }

        return picked;
    }
}

public static class ForecastEngine
{
    public static GetForecastResponse Build(
        string symbol,
        string interval,
        IReadOnlyList<LiveCandle> candles,
        ForecastOptions options,
        int horizon,
        int stepSeconds,
        IForecastRunner runner,
        long now)
    {
        horizon = Math.Clamp(horizon, 1, ForecastOptions.MaxHorizon);
        if (!options.Enabled)
            return Notice(symbol, interval, "forecast disabled");
        if (!runner.Ready)
            return Notice(symbol, interval, "forecast model not installed");
        var closes = candles.Select(candle => candle.Close).ToList();
        var window = ForecastWindow.Take(closes, options.ContextLength);
        if (window == null || candles.Count == 0)
            return Notice(symbol, interval, "Not enough bars for a forecast.");

        var raw = runner.Run([ToFloat(window)]);
        if (raw.Means.Length == 0 || raw.Quantiles.Length == 0)
            return Notice(symbol, interval, "forecast model not installed");
        var steps = ForecastMap.Steps(raw.Means[0], raw.Quantiles[0], candles[^1].Start, stepSeconds, horizon);
        var recent = ForecastWindow.Recent(candles, now, options.ContextLength);
        var coverage = ForecastCoverage.HitRate(
            recent.Select(candle => candle.Close).ToList(),
            options.ContextLength,
            horizon,
            sample =>
            {
                var probe = runner.Run([ToFloat(sample)]);
                var row = probe.Quantiles.Length == 0 || probe.Quantiles[0].Length == 0 ? [] : probe.Quantiles[0][0];
                return (ForecastMap.Level(row, 0.1m), ForecastMap.Level(row, 0.9m));
            });
        return new GetForecastResponse
        {
            Symbol = symbol,
            Interval = interval,
            Model = runner.Name,
            Notice = "",
            Coverage = coverage is decimal rate ? ForecastCoverage.Text(rate) : "",
            GeneratedAt = now,
            CandleTime = candles[^1].Start,
            Horizon = steps.Count,
            Steps = steps,
        };
    }

    public static GetForecastResponse Notice(string symbol, string interval, string notice) => new()
    {
        Symbol = symbol,
        Interval = interval,
        Model = ForecastOptions.ModelName,
        Notice = notice,
    };

    static float[] ToFloat(IReadOnlyList<decimal> values)
    {
        var row = new float[values.Count];
        for (var i = 0; i < values.Count; i++)
            row[i] = (float)values[i];
        return row;
    }
}

public sealed class ForecastCache(IForecastRunner runner, ForecastOptions options, IForecastEvents? events = null)
{
    readonly object gate = new();
    readonly Dictionary<string, GetForecastResponse> reports = new();
    readonly Dictionary<string, Task> inflight = new();

    public bool Enabled => options.Enabled;
    public bool RunnerReady => runner.Ready;

    public GetForecastResponse Get(string symbol, string interval, int horizon)
    {
        var key = Key(symbol, interval, horizon);
        lock (gate)
        {
            if (reports.TryGetValue(key, out var report))
                return report;
            if (inflight.ContainsKey(key))
                return ForecastEngine.Notice(symbol, interval, "forecast running");
        }

        if (!options.Enabled)
            return ForecastEngine.Notice(symbol, interval, "forecast disabled");
        if (!runner.Ready)
            return ForecastEngine.Notice(symbol, interval, "forecast model not installed");
        return ForecastEngine.Notice(symbol, interval, "forecast running");
    }

    public Task Refresh(string symbol, string interval, IReadOnlyList<LiveCandle> candles, int horizon, int stepSeconds, CancellationToken cancel)
    {
        if (!options.Enabled || !runner.Ready)
            return Task.CompletedTask;
        var key = Key(symbol, interval, horizon);
        var copy = candles.ToList();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (gate)
        {
            if (inflight.TryGetValue(key, out var existing))
                return existing;
            var task = Task.Run(() =>
            {
                try
                {
                    cancel.ThrowIfCancellationRequested();
                    var report = ForecastEngine.Build(symbol, interval, copy, options, horizon, stepSeconds, runner, now);
                    lock (gate)
                        reports[key] = report;
                    events?.Publish(symbol, report);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    lock (gate)
                        inflight.Remove(key);
                }
            }, cancel);
            inflight[key] = task;
            return task;
        }
    }

    static string Key(string symbol, string interval, int horizon) => $"{symbol}|{interval}|{horizon}";
}
