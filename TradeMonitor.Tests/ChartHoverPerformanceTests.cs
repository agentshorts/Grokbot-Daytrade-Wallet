using System.Diagnostics;
using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class ChartHoverPerformanceTests
{
    [Test]
    public void Grouping_trades_by_wallet_matches_a_scan_and_is_faster()
    {
        var wallets = Enumerable.Range(0, 12).ToList();
        var trades = new List<Trade>(12_000);
        foreach (var walletId in wallets)
        {
            for (var i = 0; i < 1000; i++)
                trades.Add(new Trade { WalletId = walletId, ClientId = walletId + "-" + i, Symbol = "BTC-USD" });
        }

        var scan = Time(() => Scan(wallets, trades));
        var grouped = Time(() => Group(wallets, trades));
        TestContext.Out.WriteLine($"ledger assign ms scan={scan.Ms:F2} grouped={grouped.Ms:F2} trades={trades.Count}");
        Assert.That(grouped.Counts, Is.EqualTo(scan.Counts));
        Assert.That(grouped.Ms, Is.LessThan(scan.Ms));
    }

    [Test]
    public void Overlapping_reads_share_one_load()
    {
        var key = new object();
        var started = 0;
        var release = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        var secondEntered = new ManualResetEventSlim(false);
        var first = Task.Run(() => ReadShare.Share(key, () =>
        {
            Interlocked.Increment(ref started);
            entered.Set();
            release.Wait();
            return 7;
        }));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        var second = Task.Run(() => ReadShare.Share(key, () =>
        {
            Interlocked.Increment(ref started);
            secondEntered.Set();
            return 9;
        }));
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(2) && second.Status != TaskStatus.Running)
            Thread.Sleep(1);
        Thread.Sleep(50);
        Assert.That(secondEntered.IsSet, Is.False);
        Assert.That(started, Is.EqualTo(1));
        release.Set();
        Assert.That(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(started, Is.EqualTo(1));
        Assert.That(first.Result, Is.EqualTo(7));
        Assert.That(second.Result, Is.EqualTo(7));
    }

    [Test]
    public void A_failed_shared_read_is_not_reused()
    {
        var key = new object();
        Assert.Throws<InvalidOperationException>(() => ReadShare.Share<int>(key, () => throw new InvalidOperationException("boom")));
        Assert.That(ReadShare.Share(key, () => 4), Is.EqualTo(4));
    }

    [Test]
    public void Candle_window_uses_the_symbol_range_and_a_warm_cache()
    {
        var path = Path.Combine(Path.GetTempPath(), "trademonitor-hover-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            LedgerDialect.Configure(SqliteDialect.Provider);
            var inner = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
            var factory = new CountingFactory(inner);
            var migrated = new Migrator(factory, typeof(Migration1000).Assembly).Run();
            Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
            var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            end -= end % 60;
            var clock = new FixedClock { UtcNow = DateTimeOffset.FromUnixTimeSeconds(end).UtcDateTime };
            var bars = new PriceBars(factory, clock);
            const string symbol = "BTC-USD";
            const int count = 2000;
            var origin = end - count * 60L;
            using (var db = factory.Open())
            {
                for (var i = 0; i < count; i++)
                    db.Insert(new PriceBar { Symbol = symbol, Start = origin + i * 60L, Open = i, High = i + 1, Low = i, Close = i, Volume = 1, UpdatedAt = clock.UtcNow });
            }

            var from = origin + 1500 * 60L;
            var to = origin + 1800 * 60L;
            const int rounds = 40;
            var watch = Stopwatch.StartNew();
            List<PriceBar> full = [];
            for (var round = 0; round < rounds; round++)
            {
                using var db = factory.Open();
                full = db.Select<PriceBar>(x => x.Symbol == symbol);
            }
            var fullMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            List<PriceBar> ranged = [];
            for (var round = 0; round < rounds; round++)
            {
                using var db = factory.Open();
                ranged = db.Select<PriceBar>(x => x.Symbol == symbol && x.Start >= from && x.Start < to);
            }
            var rangeMs = watch.Elapsed.TotalMilliseconds;
            TestContext.Out.WriteLine($"candle read ms full={fullMs:F2} rows={full.Count} range={rangeMs:F2} rows={ranged.Count}");
            Assert.That(ranged, Has.Count.LessThan(full.Count));
            Assert.That(rangeMs, Is.LessThan(fullMs));

            var page = bars.Window(symbol, new CandleInterval(60, "1m"), from, to);
            Assert.That(page.Candles.Select(x => x.Start), Is.EqualTo(ranged.OrderBy(x => x.Start).Select(x => x.Start)));
            var opens = factory.Opens;
            watch.Restart();
            var again = bars.Window(symbol, new CandleInterval(60, "1m"), from, to);
            var cachedMs = watch.Elapsed.TotalMilliseconds;
            TestContext.Out.WriteLine($"candle window ms cached={cachedMs:F2} extraOpens={factory.Opens - opens}");
            Assert.That(again.Candles.Select(x => x.Close), Is.EqualTo(page.Candles.Select(x => x.Close)));
            Assert.That(factory.Opens, Is.EqualTo(opens));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    static (long[] Counts, double Ms) Scan(List<int> wallets, List<Trade> trades)
    {
        var watch = Stopwatch.StartNew();
        var counts = new long[wallets.Count];
        for (var i = 0; i < 40; i++)
        {
            for (var w = 0; w < wallets.Count; w++)
                counts[w] = trades.Count(trade => trade.WalletId == wallets[w]);
        }

        return (counts, watch.Elapsed.TotalMilliseconds);
    }

    static (long[] Counts, double Ms) Group(List<int> wallets, List<Trade> trades)
    {
        var watch = Stopwatch.StartNew();
        var counts = new long[wallets.Count];
        for (var i = 0; i < 40; i++)
        {
            var grouped = LedgerData.GroupByWallet(trades, trade => trade.WalletId);
            for (var w = 0; w < wallets.Count; w++)
                counts[w] = grouped.TryGetValue(wallets[w], out var rows) ? rows.Count : 0;
        }

        return (counts, watch.Elapsed.TotalMilliseconds);
    }

    static (long[] Counts, double Ms) Time(Func<(long[] Counts, double Ms)> run) => run();

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }

    sealed class CountingFactory(IDbConnectionFactory inner) : IDbConnectionFactory
    {
        public int Opens { get; private set; }

        public System.Data.IDbConnection OpenDbConnection()
        {
            Opens++;
            return inner.OpenDbConnection();
        }

        public System.Data.IDbConnection CreateDbConnection() => inner.CreateDbConnection();
    }
}
