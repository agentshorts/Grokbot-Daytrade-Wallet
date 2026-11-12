using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Folds public ticks into one-minute bars and keeps the table small.
public sealed class PriceBars(IDbConnectionFactory dbFactory, ILedgerClock clock)
{
    public static readonly TimeSpan MinuteDetail = TimeSpan.FromDays(2);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    public const int CoarseSeconds = 300;

    readonly object gate = new();
    readonly object flushLock = new();
    readonly Dictionary<string, PriceBar> pending = new(StringComparer.Ordinal);
    DateTime lastPrune = DateTime.MinValue;

    public void Record(LiveTick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.ProductId) || tick.Price <= 0)
            return;
        var at = tick.At == default ? clock.UtcNow : AsUtc(tick.At);
        var start = Bucket(at, 60);
        var symbol = tick.ProductId.Trim().ToUpperInvariant();
        var key = symbol + "|" + start;
        lock (gate)
        {
            if (!pending.TryGetValue(key, out var bar))
            {
                pending[key] = new PriceBar
                {
                    Symbol = symbol,
                    Start = start,
                    Open = tick.Price,
                    High = tick.Price,
                    Low = tick.Price,
                    Close = tick.Price,
                    Volume = 0,
                    UpdatedAt = clock.UtcNow,
                };
                return;
            }

            if (tick.Price > bar.High)
                bar.High = tick.Price;
            if (tick.Price < bar.Low)
                bar.Low = tick.Price;
            bar.Close = tick.Price;
            bar.UpdatedAt = clock.UtcNow;
        }
    }

    public bool Any(string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var prefix = symbol + "|";
        lock (gate)
        {
            if (pending.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)))
                return true;
        }

        using var db = dbFactory.Open();
        return db.Exists<PriceBar>(x => x.Symbol == symbol);
    }

    public void Seed(string symbol, IReadOnlyList<LiveCandle> candles)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0 || candles.Count == 0)
            return;
        lock (flushLock)
        {
            using var db = dbFactory.Open();
            foreach (var candle in candles)
            {
                if (candle.Start <= 0 || candle.Close <= 0)
                    continue;
                var start = candle.Start - (candle.Start % 60);
                if (db.Exists<PriceBar>(x => x.Symbol == symbol && x.Start == start))
                    continue;
                db.Insert(new PriceBar
                {
                    Symbol = symbol,
                    Start = start,
                    Open = candle.Open > 0 ? candle.Open : candle.Close,
                    High = candle.High > 0 ? candle.High : candle.Close,
                    Low = candle.Low > 0 ? candle.Low : candle.Close,
                    Close = candle.Close,
                    Volume = candle.Volume > 0 ? candle.Volume : 0,
                    UpdatedAt = clock.UtcNow,
                });
            }
        }
    }

    public void Flush()
    {
        lock (flushLock)
        {
            List<PriceBar> batch;
            lock (gate)
            {
                batch = pending.Values.ToList();
                pending.Clear();
            }

            if (batch.Count > 0)
            {
                using var db = dbFactory.Open();
                foreach (var bar in batch)
                    Merge(db, bar);
            }

            if (clock.UtcNow - lastPrune >= TimeSpan.FromMinutes(1))
            {
                lastPrune = clock.UtcNow;
                PruneCore();
            }
        }
    }

    public void Prune()
    {
        lock (flushLock)
            PruneCore();
    }

    void PruneCore()
    {
        var now = Unix(clock.UtcNow);
        var deleteBefore = now - (long)Retention.TotalSeconds;
        var coarseBefore = now - (long)MinuteDetail.TotalSeconds;
        using var db = dbFactory.Open();
        db.Delete<PriceBar>(x => x.Start < deleteBefore);
        var coarse = db.Select<PriceBar>(x => x.Start < coarseBefore && x.Start >= deleteBefore);
        foreach (var group in coarse.GroupBy(x => (x.Symbol, Bucket: x.Start - (x.Start % CoarseSeconds))))
        {
            var rows = group.OrderBy(x => x.Start).ToList();
            if (rows.Count == 1 && rows[0].Start == group.Key.Bucket)
                continue;
            foreach (var row in rows)
                db.Delete<PriceBar>(x => x.Symbol == row.Symbol && x.Start == row.Start);
            db.Insert(new PriceBar
            {
                Symbol = group.Key.Symbol,
                Start = group.Key.Bucket,
                Open = rows[0].Open,
                High = rows.Max(x => x.High),
                Low = rows.Min(x => x.Low),
                Close = rows[^1].Close,
                Volume = rows.Sum(x => x.Volume),
                UpdatedAt = rows.Max(x => x.UpdatedAt),
            });
        }
    }

    public List<LiveCandle> Candles(string symbol, int granularitySeconds, int take = 2000)
    {
        Flush();
        symbol = symbol.Trim().ToUpperInvariant();
        var seconds = granularitySeconds < 60 ? 60 : granularitySeconds;
        using var db = dbFactory.Open();
        var rows = db.Select<PriceBar>(x => x.Symbol == symbol).OrderBy(x => x.Start).ToList();
        var candles = Aggregate(rows, seconds);
        if (take > 0 && candles.Count > take)
            candles = candles.Skip(candles.Count - take).ToList();
        return candles;
    }

    public CandlePage Window(string symbol, CandleInterval requested, long? from, long? to, int cap = CandleInterval.Cap)
    {
        Flush();
        symbol = symbol.Trim().ToUpperInvariant();
        using var db = dbFactory.Open();
        var rows = db.Select<PriceBar>(x => x.Symbol == symbol).OrderBy(x => x.Start).ToList();
        return Window(rows, requested, from, to, cap);
    }

    public static CandlePage Window(IReadOnlyList<PriceBar> rows, CandleInterval requested, long? from, long? to, int cap = CandleInterval.Cap)
    {
        var filtered = rows.Where(row => InRange(row.Start, from, to, requested.Seconds)).OrderBy(row => row.Start).ToList();
        var seconds = requested.Seconds;
        var candles = Aggregate(filtered, seconds);
        var capped = false;
        if (cap > 0 && candles.Count > cap)
        {
            var coarser = CandleInterval.Coarsen(seconds, candles.Count, cap);
            if (coarser != seconds)
            {
                seconds = coarser;
                candles = Aggregate(filtered, seconds);
            }

            capped = true;
            if (candles.Count > cap)
                candles = candles.Skip(candles.Count - cap).ToList();
        }

        var key = CandleInterval.Format(seconds);
        var hint = "";
        if (capped)
            hint = $"Showing {key} ({candles.Count} candles). {requested.Key} is finer than the {cap} candle cap.";
        return new CandlePage(candles, seconds, key, requested.Key, capped, hint);
    }

    public static List<LiveCandle> Aggregate(IReadOnlyList<PriceBar> rows, int seconds)
    {
        var size = seconds < 60 ? 60 : seconds;
        var candles = new List<LiveCandle>();
        LiveCandle? current = null;
        var bucket = long.MinValue;
        foreach (var row in rows.OrderBy(x => x.Start))
        {
            var start = row.Start - (row.Start % size);
            if (current == null || start != bucket)
            {
                current = new LiveCandle
                {
                    Start = start,
                    Open = row.Open,
                    High = row.High,
                    Low = row.Low,
                    Close = row.Close,
                    Volume = row.Volume,
                };
                candles.Add(current);
                bucket = start;
                continue;
            }

            current.High = Math.Max(current.High, row.High);
            current.Low = Math.Min(current.Low, row.Low);
            current.Close = row.Close;
            current.Volume += row.Volume;
        }

        return candles;
    }

    static bool InRange(long start, long? from, long? to, int seconds)
    {
        if (to is long end && start >= end)
            return false;
        if (from is long begin)
        {
            var aligned = begin - (begin % seconds);
            if (start < aligned)
                return false;
        }

        return true;
    }

    public static long Bucket(DateTime at, int seconds)
    {
        var unix = Unix(AsUtc(at));
        return unix - (unix % seconds);
    }

    static void Merge(System.Data.IDbConnection db, PriceBar bar)
    {
        var row = db.Single<PriceBar>(x => x.Symbol == bar.Symbol && x.Start == bar.Start);
        if (row == null)
        {
            db.Insert(bar);
            return;
        }

        row.High = Math.Max(row.High, bar.High);
        row.Low = Math.Min(row.Low, bar.Low);
        row.Close = bar.Close;
        if (bar.Volume > 0)
            row.Volume += bar.Volume;
        row.UpdatedAt = bar.UpdatedAt;
        db.Update(row);
    }

    static DateTime AsUtc(DateTime at) =>
        at.Kind == DateTimeKind.Utc ? at : DateTime.SpecifyKind(at, DateTimeKind.Utc);

    static long Unix(DateTime at) => new DateTimeOffset(AsUtc(at)).ToUnixTimeSeconds();
}

public sealed record CandlePage(
    List<LiveCandle> Candles,
    int Seconds,
    string Key,
    string Requested,
    bool Capped,
    string Hint);

