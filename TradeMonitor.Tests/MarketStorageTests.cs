using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class MarketStorageTests
{
    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-market-" + Guid.NewGuid().ToString("N") + ".db");
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LiveMarketCache market = null!;
    PriceBars bars = null!;
    TrackedSymbolBook symbols = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        LedgerDialect.Configure(SqliteDialect.Provider);
        factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        var migrated = new Migrator(factory, typeof(Migration1003).Assembly).Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
        clock = new FixedClock { UtcNow = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc) };
        market = new LiveMarketCache();
        bars = new PriceBars(factory, clock);
        symbols = new TrackedSymbolBook(factory, market, clock);
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
    public void Migration_seeds_tracked_symbols_once()
    {
        using (var db = factory.Open())
        {
            Assert.That(db.TableExists<PriceBar>(), Is.True);
            Assert.That(db.TableExists<TrackedSymbol>(), Is.True);
            db.DeleteAll<TrackedSymbol>();
            db.ExecuteSql("DELETE FROM Migration WHERE Name IN ('Migration1003', 'Migration1004', 'Migration1005')");
        }

        var restored = new Migrator(factory, typeof(Migration1003).Assembly).Run();
        Assert.That(restored.Succeeded, Is.True, restored.Error?.ToString() ?? "rerun failed");
        using (var db = factory.Open())
            Assert.That(db.Select<TrackedSymbol>().OrderBy(x => x.SortOrder).Select(x => x.ProductId), Is.EqualTo(TrackedSymbolDefaults.Products));

        var again = new Migrator(factory, typeof(Migration1003).Assembly).Run();
        Assert.That(again.Succeeded, Is.True, again.Error?.ToString() ?? "second run failed");
        using (var db = factory.Open())
            Assert.That(db.Count<TrackedSymbol>(), Is.EqualTo(TrackedSymbolDefaults.Products.Length));
    }

    [Test]
    public void Minute_bars_update_the_close_and_aggregate()
    {
        var at = clock.UtcNow;
        bars.Record(Tick("SOL-USD", 10, at));
        bars.Record(Tick("SOL-USD", 12, at.AddSeconds(20)));
        bars.Record(Tick("SOL-USD", 9, at.AddSeconds(40)));
        bars.Record(Tick("SOL-USD", 11, at.AddMinutes(1)));
        var candles = bars.Candles("sol-usd", 60);
        Assert.That(candles.Select(x => (x.Open, x.High, x.Low, x.Close)), Is.EqualTo(new[]
        {
            (10m, 12m, 9m, 9m),
            (11m, 11m, 11m, 11m),
        }));
        var five = bars.Candles("SOL-USD", 300);
        Assert.That(five.Single().Open, Is.EqualTo(10m));
        Assert.That(five.Single().High, Is.EqualTo(12m));
        Assert.That(five.Single().Low, Is.EqualTo(9m));
        Assert.That(five.Single().Close, Is.EqualTo(11m));
    }

    [Test]
    public void Prune_drops_expired_bars_and_folds_older_minutes()
    {
        var now = PriceBars.Bucket(clock.UtcNow, 60);
        using (var db = factory.Open())
        {
            db.Insert(Bar("DOGE-USD", now - (14 * 24 * 3600) - 120, 1));
            db.Insert(Bar("DOGE-USD", now - (3 * 24 * 3600), 2));
            db.Insert(Bar("DOGE-USD", now - (3 * 24 * 3600) + 60, 4));
            db.Insert(Bar("DOGE-USD", now - 60, 8));
        }

        bars.Prune();
        var left = bars.Candles("DOGE-USD", 60);
        Assert.That(left.Select(x => x.Close), Does.Not.Contain(1m));
        Assert.That(left.Any(x => x.Close == 8m), Is.True);
        var folded = left.Single(x => x.Open == 2m);
        Assert.That(folded.High, Is.EqualTo(4m));
        Assert.That(folded.Close, Is.EqualTo(4m));
        Assert.That(folded.Start % PriceBars.CoarseSeconds, Is.EqualTo(0));
    }

    [Test]
    public void Feed_follows_tracked_symbols_open_orders_and_held_positions()
    {
        market.PublishProducts(
        [
            new LiveProduct { ProductId = "BTC-USD", Base = "BTC", Quote = "USD", Name = "Bitcoin" },
            new LiveProduct { ProductId = "ETH-USD", Base = "ETH", Quote = "USD", Name = "Ether" },
            new LiveProduct { ProductId = "SOL-USD", Base = "SOL", Quote = "USD", Name = "Solana" },
        ]);
        using (var db = factory.Open())
        {
            db.DeleteAll<TrackedSymbol>();
            db.Insert(new TrackedSymbol { ProductId = "ETH-USD", SortOrder = 0, CreatedAt = clock.UtcNow });
            db.Insert(new TrackedSymbol { ProductId = "GONE-USD", SortOrder = 1, CreatedAt = clock.UtcNow });
            var walletId = (int)db.Insert(new Wallet
            {
                Key = "held",
                Name = "Held",
                Mode = WalletMode.Paper,
                BaseCurrency = "USD",
                CreatedAt = clock.UtcNow,
            }, selectIdentity: true);
            db.Insert(new PaperOrder
            {
                WalletId = walletId,
                ClientId = "resting",
                Symbol = "SOL-USD",
                Side = TradeSide.Buy,
                Type = LiveOrderType.Limit,
                Qty = 1,
                TriggerPrice = 20,
                Status = LiveOrderStatus.Open,
                CreatedAt = clock.UtcNow,
            });
            db.Insert(new Position
            {
                WalletId = walletId,
                Symbol = "btc-usd",
                Qty = 1,
                UpdatedAt = clock.UtcNow,
            });
            db.Insert(new Position
            {
                WalletId = walletId,
                Symbol = "DOGE-USD",
                Qty = 0,
                UpdatedAt = clock.UtcNow,
            });
        }

        Assert.That(symbols.PollSymbols(), Is.EqualTo(new[] { "ETH-USD", "SOL-USD", "BTC-USD" }));
        var listed = symbols.List();
        Assert.That(listed.Single(x => x.ProductId == "ETH-USD").Listed, Is.True);
        Assert.That(listed.Single(x => x.ProductId == "GONE-USD").Listed, Is.False);

        var added = symbols.Add("sol-usd");
        Assert.That(added.Select(x => x.ProductId), Does.Contain("SOL-USD"));
        var moved = symbols.Reorder(["SOL-USD", "ETH-USD", "GONE-USD"]);
        Assert.That(moved.Select(x => x.ProductId), Is.EqualTo(new[] { "SOL-USD", "ETH-USD", "GONE-USD" }));
        var removed = symbols.Remove("GONE-USD");
        Assert.That(removed.Select(x => x.ProductId), Does.Not.Contain("GONE-USD"));
        var unknown = Assert.Throws<ServiceStack.HttpError>(() => symbols.Add("NOPE-USD"));
        Assert.That(unknown!.ErrorCode, Is.EqualTo("UnknownProduct"));
    }

    static LiveTick Tick(string product, decimal price, DateTime at) => new()
    {
        ProductId = product,
        Price = price,
        At = at,
    };

    static PriceBar Bar(string symbol, long start, decimal price) => new()
    {
        Symbol = symbol,
        Start = start,
        Open = price,
        High = price,
        Low = price,
        Close = price,
        UpdatedAt = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
    };

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
