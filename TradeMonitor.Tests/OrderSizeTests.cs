using NUnit.Framework;
using ServiceStack;
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
public class OrderSizeTests
{
    static readonly ProductSteps Btc = new(0.00000001m, 0.01m, 0.00000001m, 1m);

    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-size-" + Guid.NewGuid().ToString("N") + ".db");
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LiveMarketCache market = null!;
    LiveServices live = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        LedgerDialect.Configure(SqliteDialect.Provider);
        factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        var migrated = new Migrator(factory, typeof(Migration1000).Assembly).Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
        clock = new FixedClock { UtcNow = new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc) };
        market = new LiveMarketCache();
        live = new LiveServices(
            factory,
            clock,
            market,
            new PaperOrderBook(factory, clock),
            new LiveSimulationStore(factory, clock),
            new PriceBars(factory, clock),
            new TrackedSymbolBook(factory, market, clock));
        market.PublishProducts(
        [
            new LiveProduct
            {
                ProductId = "BTC-USD",
                Base = "BTC",
                Quote = "USD",
                Name = "Bitcoin",
                BaseIncrement = 0.00000001m,
                QuoteIncrement = 0.01m,
                BaseMinSize = 0.00000001m,
                MinMarketFunds = 1m,
            },
        ]);
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

    [SetUp]
    public void SetUp()
    {
        clock.UtcNow = new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);
        using var db = factory.Open();
        db.DeleteAll<BalanceSnapshot>();
        db.DeleteAll<Position>();
        db.DeleteAll<PriceMark>();
        if (db.TableExists<PaperOrder>())
            db.DeleteAll<PaperOrder>();
        db.DeleteAll<Transfer>();
        db.DeleteAll<Trade>();
        db.DeleteAll<Wallet>();
    }

    [Test]
    public void Spend_floors_to_product_steps_and_caps_at_cash()
    {
        var quarter = OrderSize.FromSpend(250m, 50000m, 1000m, Btc);
        Assert.That(quarter.Error, Is.Null);
        Assert.That(quarter.Spend, Is.EqualTo(250m));
        Assert.That(quarter.Qty, Is.EqualTo(0.005m));

        var over = OrderSize.FromSpend(2500m, 50000m, 1000m, Btc);
        Assert.That(over.Error, Is.Null);
        Assert.That(over.Spend, Is.EqualTo(1000m));
        Assert.That(over.Qty, Is.EqualTo(0.02m));

        var cents = OrderSize.FromSpend(250.009m, 50000m, 1000m, Btc);
        Assert.That(cents.Spend, Is.EqualTo(250m));
        Assert.That(cents.Qty, Is.EqualTo(0.005m));

        var tiny = OrderSize.FromSpend(0.50m, 50000m, 1000m, Btc);
        Assert.That(tiny.Error, Does.Contain("minimum"));

        var parsed = CoinbaseMessages.Products("""
            [{"product_id":"BTC-USD","quote_currency_id":"USD","base_currency_id":"BTC","status":"online","product_type":"SPOT","base_increment":"0.00000001","quote_increment":"0.01","base_min_size":"0.00000001","min_market_funds":"1"}]
            """);
        Assert.That(parsed.Single().BaseIncrement, Is.EqualTo(0.00000001m));
        Assert.That(parsed.Single().QuoteIncrement, Is.EqualTo(0.01m));
        Assert.That(parsed.Single().MinMarketFunds, Is.EqualTo(1m));
    }

    [Test]
    public void Fractional_buy_leaves_a_remainder_on_the_same_lot()
    {
        var key = Open();
        Tick(50000);
        var bought = Place(key, TradeSide.Buy, 0.005000009m, null);
        var lot = bought.Lots.Single();
        Assert.That(lot.Qty, Is.EqualTo(0.005m));
        Assert.That(bought.Cash, Is.EqualTo(750m));

        var sold = Place(key, TradeSide.Sell, 0.0025m, lot.TradeId);
        var left = sold.Lots.Single();
        Assert.That(left.TradeId, Is.EqualTo(lot.TradeId));
        Assert.That(left.Qty, Is.EqualTo(0.0025m));
        Assert.That(sold.ClosedLots.Single().Qty, Is.EqualTo(0.0025m));
        Assert.That(sold.Cash, Is.EqualTo(875m));

        var error = Assert.Throws<HttpError>(() => Place(key, TradeSide.Buy, 0.00000001m, null));
        Assert.That(error!.ErrorCode, Is.EqualTo("BelowMinimum"));
        Assert.That(error.Message, Does.Contain("minimum").IgnoreCase);
    }

    [Test]
    public void Simulation_fractional_orders_never_persist()
    {
        var ledger = new LedgerServices(factory, clock);
        var created = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = "coinbase",
            Name = "Coinbase",
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            BaseCurrency = "USD",
        });
        ledger.Any(new RecordTransfer { Wallet = created.Result.Key, ClientId = "seed", Kind = TransferKind.Deposit, Amount = 1000m });
        Tick(50000);
        var opened = (LiveBookResponse)live.Any(new OpenLiveSimulation { Wallet = created.Result.Key, Symbol = "BTC-USD" });
        var bought = (LiveBookResponse)live.Any(new PlaceSimulationOrder
        {
            Id = opened.SimulationId,
            Symbol = "BTC-USD",
            Side = TradeSide.Buy,
            Type = LiveOrderType.Market,
            Qty = 0.005m,
        });
        Assert.That(bought.Simulated, Is.True);
        Assert.That(bought.Cash, Is.EqualTo(750m));
        Assert.That(bought.Lots.Single().Qty, Is.EqualTo(0.005m));
        using var db = factory.Open();
        Assert.That(db.Count<Trade>(), Is.EqualTo(0));
        Assert.That(db.Count<PaperOrder>(), Is.EqualTo(0));
    }

    string Open() =>
        ((CreatePaperWalletResponse)live.Any(new CreatePaperWallet { Name = "Size", StartingCash = 1000m })).Result.Key;

    void Tick(decimal price) =>
        market.PublishTick(new LiveTick { ProductId = "BTC-USD", Price = price, At = clock.UtcNow });

    LiveBookResponse Place(string wallet, TradeSide side, decimal qty, int? lotId) =>
        (LiveBookResponse)live.Any(new PlacePaperOrder
        {
            Wallet = wallet,
            Symbol = "BTC-USD",
            Side = side,
            Type = LiveOrderType.Market,
            Qty = qty,
            Costing = lotId == null ? CostingMethod.Fifo : CostingMethod.Lot,
            LotId = lotId,
        });

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
