using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

public class CandleIntervalTests
{
    [Test]
    public void Parses_presets_and_custom_intervals()
    {
        foreach (var (text, seconds) in new (string Text, int Seconds)[]
        {
            ("1m", 60), ("2m", 120), ("5m", 300), ("15m", 900), ("30m", 1800),
            ("1h", 3600), ("2h", 7200), ("3h", 10800), ("5h", 18000), ("1d", 86400), ("1w", 604800),
            ("7m", 420), ("4h", 14400), ("90m", 5400), ("1D", 86400), ("1W", 604800),
        })
        {
            Assert.That(CandleInterval.TryParse(text, false, out var parsed, out var error), Is.True, error);
            Assert.That(parsed.Seconds, Is.EqualTo(seconds), text);
            Assert.That(parsed.Key, Is.EqualTo(text.ToLowerInvariant()));
        }

        Assert.That(CandleInterval.TryParse("900", false, out var native, out _), Is.True);
        Assert.That(native.Key, Is.EqualTo("15m"));
        Assert.That(CandleInterval.TryParse("  ", true, out var fallback, out _), Is.True);
        Assert.That(fallback.Key, Is.EqualTo("5m"));
    }

    [Test]
    public void Rejects_invalid_intervals()
    {
        foreach (var text in new[] { "foo", "0m", "1.5h", "7", "7x", "-1m", "9w", "" })
        {
            Assert.That(CandleInterval.TryParse(text, false, out _, out var error), Is.False, text);
            Assert.That(error, Is.Not.Empty);
        }
    }

    [Test]
    public void Coarsen_picks_the_finest_interval_under_the_cap()
    {
        Assert.That(CandleInterval.Coarsen(60, 4000, 1500), Is.EqualTo(300));
        Assert.That(CandleInterval.Coarsen(420, 2000, 1500), Is.EqualTo(900));
        Assert.That(CandleInterval.Coarsen(60, 1500, 1500), Is.EqualTo(60));
        Assert.That(CandleInterval.Coarsen(604800, 9000, 1500), Is.EqualTo(604800));
    }

    [Test]
    public void Aggregate_builds_ohlcv_and_keeps_a_partial_last_bucket()
    {
        var rows = new[]
        {
            Bar(0, 10, 12, 9, 11, 1),
            Bar(60, 11, 14, 8, 13, 2),
            Bar(120, 13, 13, 7, 12, 3),
            Bar(300, 20, 21, 19, 20, 4),
        };
        var candles = PriceBars.Aggregate(rows, 300);
        Assert.That(candles.Select(x => (x.Start, x.Open, x.High, x.Low, x.Close, x.Volume)), Is.EqualTo(new[]
        {
            (0L, 10m, 14m, 7m, 12m, 6m),
            (300L, 20m, 21m, 19m, 20m, 4m),
        }));
    }

    [Test]
    public void Window_filters_the_range_and_coarsens_when_the_cap_is_hit()
    {
        var rows = Enumerable.Range(0, 4000).Select(i => Bar(i * 60L, 10 + i, 12 + i, 9 + i, 11 + i, 1)).ToList();
        var page = PriceBars.Window(rows, new CandleInterval(60, "1m"), from: 1000 * 60, to: 4000 * 60, cap: 1500);
        Assert.That(page.Capped, Is.True);
        Assert.That(page.Key, Is.EqualTo("2m"));
        Assert.That(page.Candles, Has.Count.EqualTo(1500));
        Assert.That(page.Hint, Does.Contain("1m"));
        var first = page.Candles[0];
        Assert.That(first.Start, Is.EqualTo(1000 * 60));
        Assert.That(first.Open, Is.EqualTo(1010m));
        Assert.That(first.High, Is.EqualTo(1013m));
        Assert.That(first.Low, Is.EqualTo(1009m));
        Assert.That(first.Close, Is.EqualTo(1012m));
        Assert.That(first.Volume, Is.EqualTo(2m));
    }

    public static PriceBar Bar(long start, decimal open, decimal high, decimal low, decimal close, decimal volume, string symbol = "BTC-USD") => new()
    {
        Symbol = symbol,
        Start = start,
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = volume,
        UpdatedAt = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
    };
}

public class CandleWindowStorageTests
{
    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-candles-" + Guid.NewGuid().ToString("N") + ".db");
    OrmLiteConnectionFactory factory = null!;
    PriceBars bars = null!;
    LiveServices live = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        LedgerDialect.Configure(SqliteDialect.Provider);
        factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        var migrated = new Migrator(factory, typeof(Migration1005).Assembly).Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
        var clock = new FixedClock { UtcNow = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc) };
        var market = new LiveMarketCache();
        bars = new PriceBars(factory, clock);
        live = new LiveServices(factory, clock, market, new PaperOrderBook(factory, clock), new LiveSimulationStore(factory, clock), bars, new TrackedSymbolBook(factory, market, clock));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var file = path + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Test]
    public void Stored_minutes_aggregate_for_a_custom_interval()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var start = now - (now % 420);
        using (var db = factory.Open())
        {
            Assert.That(db.ColumnExists<PriceBar>(x => x.Volume), Is.True);
            for (var i = 0; i < 7; i++)
                db.Insert(CandleIntervalTests.Bar(start + i * 60L, 10 + i, 20 + i, 5 + i, 15 + i, i + 1, "ADA-USD"));
        }

        var page = bars.Window("ada-usd", new CandleInterval(420, "7m"), null, null);
        Assert.That(page.Candles, Has.Count.EqualTo(1));
        var candle = page.Candles[0];
        Assert.That(candle.Open, Is.EqualTo(10m));
        Assert.That(candle.High, Is.EqualTo(26m));
        Assert.That(candle.Low, Is.EqualTo(5m));
        Assert.That(candle.Close, Is.EqualTo(21m));
        Assert.That(candle.Volume, Is.EqualTo(28m));

        var response = (GetLiveBarsResponse)live.Any(new GetLiveBars { Product = "ADA-USD", Granularity = "7m" });
        Assert.That(response.Granularity, Is.EqualTo("7m"));
        Assert.That(response.Results.Single().Volume, Is.EqualTo(28m));

        var invalid = Assert.Throws<HttpError>(() => live.Any(new GetLiveBars { Product = "ADA-USD", Granularity = "foo" }));
        Assert.That(invalid!.ErrorCode, Is.EqualTo("InvalidInterval"));
        Assert.That((int)invalid.StatusCode, Is.EqualTo(400));
    }

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
