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
public class LiveLotsTests
{
    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-lots-" + Guid.NewGuid().ToString("N") + ".db");
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LedgerServices ledger = null!;
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
        ledger = new LedgerServices(factory, clock);
        market = new LiveMarketCache();
        var orders = new PaperOrderBook(factory, clock);
        var simulations = new LiveSimulationStore(factory, clock);
        live = new LiveServices(factory, clock, market, orders, simulations, new PriceBars(factory, clock), new TrackedSymbolBook(factory, market, clock));
        market.PublishProducts(
        [
            new LiveProduct { ProductId = "BTC-USD", Base = "BTC", Quote = "USD", Name = "Bitcoin" },
            new LiveProduct { ProductId = "ETH-USD", Base = "ETH", Quote = "USD", Name = "Ether" },
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
    public void Fifo_lots_match_the_worked_example_including_a_partial_sell()
    {
        var key = Open("Lots");
        Buy(key, 10, 3);
        Buy(key, 4, 4);
        Buy(key, 2, 12);
        Tick(15);
        var open = Book(key);
        Assert.That(open.Lots.Select(x => (x.Qty, x.BuyPrice, x.UnrealizedPnl)), Is.EqualTo(new[]
        {
            (10m, 3m, 120m),
            (4m, 4m, 44m),
            (2m, 12m, 6m),
        }));
        Assert.That(open.Totals.Qty, Is.EqualTo(16m));
        Assert.That(open.Totals.AverageCost, Is.EqualTo(4.375m));
        Assert.That(open.Totals.UnrealizedPnl, Is.EqualTo(170m));
        Assert.That(open.Totals.RealizedPnl, Is.EqualTo(0m));
        Assert.That(open.Totals.TotalPnl, Is.EqualTo(170m));
        Assert.That(open.ClosedLots, Is.Empty);

        var lot = open.Lots[0].TradeId;
        var sold = Place(key, TradeSide.Sell, 10, CostingMethod.Lot, lot);
        Assert.That(sold.ClosedLots.Select(x => (x.Qty, x.BuyPrice, x.SellPrice, x.RealizedPnl)), Is.EqualTo(new[] { (10m, 3m, 15m, 120m) }));
        Assert.That(sold.Totals.RealizedPnl, Is.EqualTo(120m));
        Assert.That(sold.Totals.UnrealizedPnl, Is.EqualTo(50m));
        Assert.That(sold.Totals.TotalPnl, Is.EqualTo(170m));
        Assert.That(sold.Lots.Select(x => (x.Qty, x.BuyPrice)), Is.EqualTo(new[] { (4m, 4m), (2m, 12m) }));
        ledger.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 15m, At = clock.UtcNow, ClientId = "mark-15" });
        var marked = Book(key);
        var balances = Balances(key);
        var held = balances.Positions.Single(x => x.Symbol == "BTC-USD");
        Assert.That(marked.Totals.RealizedPnl, Is.EqualTo(held.RealizedPnl));
        Assert.That(marked.Totals.UnrealizedPnl, Is.EqualTo(held.UnrealizedPnl));
        Assert.That(balances.RealizedPnl, Is.EqualTo(marked.RealizedPnl));

        var mid = sold.Lots.Single(x => x.BuyPrice == 4m);
        var partial = Place(key, TradeSide.Sell, 2, CostingMethod.Lot, mid.TradeId);
        var left = partial.Lots.Single(x => x.TradeId == mid.TradeId);
        Assert.That(left.Qty, Is.EqualTo(2m));
        Assert.That(left.Cost, Is.EqualTo(8m));
        Assert.That(left.UnrealizedPnl, Is.EqualTo(22m));
        Assert.That(partial.ClosedLots.Select(x => (x.Qty, x.BuyPrice, x.RealizedPnl)), Is.EqualTo(new[]
        {
            (10m, 3m, 120m),
            (2m, 4m, 22m),
        }));
        Assert.That(partial.Totals.RealizedPnl, Is.EqualTo(142m));
        Assert.That(partial.Totals.UnrealizedPnl, Is.EqualTo(28m));
        var marks = (GetLiveTradesResponse)live.Any(new GetLiveTrades { Wallet = key, Symbol = "BTC-USD" });
        Assert.That(marks.Results.Where(x => x.Side == TradeSide.Sell).Select(x => x.RealizedPnl).OrderBy(x => x), Is.EqualTo(new decimal?[] { 22m, 120m }));
    }

    [Test]
    public void Price_zero_settlements_stay_out_of_crypto_lot_totals()
    {
        var key = Open("Settled");
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        ledger.Any(new RecordTrade
        {
            Wallet = key,
            ClientId = "eth-buy",
            Symbol = "ETH-USD",
            Side = TradeSide.Buy,
            Qty = 2,
            Price = 5,
            ExecutedAt = clock.UtcNow,
        });
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        ledger.Any(new RecordTrade
        {
            Wallet = key,
            ClientId = "eth-settle",
            Symbol = "ETH-USD",
            Side = TradeSide.Sell,
            Kind = TradeKind.Settlement,
            Qty = 2,
            Price = 0,
            ExecutedAt = clock.UtcNow,
        });
        var book = Book(key, "ETH-USD");
        Assert.That(book.Lots, Is.Empty);
        Assert.That(book.ClosedLots, Is.Empty);
        Assert.That(book.Settlements.Single().Qty, Is.EqualTo(2m));
        Assert.That(book.Settlements.Single().RealizedPnl, Is.EqualTo(-10m));
        Assert.That(book.Totals.RealizedPnl, Is.EqualTo(0m));
        Assert.That(book.Totals.TotalPnl, Is.EqualTo(0m));
        Assert.That(Balances(key).RealizedPnl, Is.EqualTo(-10m));
    }

    [Test]
    public void Simulation_trades_never_persist()
    {
        var created = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = "coinbase",
            Name = "Coinbase",
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            BaseCurrency = "USD",
        });
        ledger.Any(new RecordTransfer { Wallet = created.Result.Key, ClientId = "seed", Kind = TransferKind.Deposit, Amount = 100m });
        Tick(10);
        var opened = (LiveBookResponse)live.Any(new OpenLiveSimulation { Wallet = created.Result.Key, Symbol = "BTC-USD" });
        var bought = (LiveBookResponse)live.Any(new PlaceSimulationOrder
        {
            Id = opened.SimulationId,
            Symbol = "BTC-USD",
            Side = TradeSide.Buy,
            Type = LiveOrderType.Market,
            Qty = 1,
        });
        Assert.That(bought.Simulated, Is.True);
        Assert.That(bought.Cash, Is.EqualTo(90m));
        Assert.That(bought.Lots.Single().Qty, Is.EqualTo(1m));
        Tick(15);
        var sold = (LiveBookResponse)live.Any(new PlaceSimulationOrder
        {
            Id = opened.SimulationId,
            Symbol = "BTC-USD",
            Side = TradeSide.Sell,
            Type = LiveOrderType.Market,
            Qty = 1,
            Costing = CostingMethod.Lot,
            LotId = bought.Lots.Single().TradeId,
        });
        Assert.That(sold.Totals.RealizedPnl, Is.EqualTo(5m));
        Assert.That(sold.Lots, Is.Empty);
        var marks = (GetLiveTradesResponse)live.Any(new GetLiveTrades { SimulationId = opened.SimulationId, Symbol = "BTC-USD" });
        Assert.That(marks.Results.Single(x => x.Side == TradeSide.Sell).RealizedPnl, Is.EqualTo(5m));
        Assert.That(marks.Results.All(x => x.Simulated), Is.True);
        using var db = factory.Open();
        Assert.That(db.Count<Trade>(), Is.EqualTo(0));
        Assert.That(db.Count<PaperOrder>(), Is.EqualTo(0));
    }

    string Open(string name) =>
        ((CreatePaperWalletResponse)live.Any(new CreatePaperWallet { Name = name, StartingCash = 1000m })).Result.Key;

    void Tick(decimal price) =>
        market.PublishTick(new LiveTick { ProductId = "BTC-USD", Price = price, At = clock.UtcNow });

    void Buy(string wallet, decimal qty, decimal price)
    {
        Tick(price);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Place(wallet, TradeSide.Buy, qty, CostingMethod.Fifo, null);
    }

    LiveBookResponse Place(string wallet, TradeSide side, decimal qty, CostingMethod costing, int? lotId) =>
        (LiveBookResponse)live.Any(new PlacePaperOrder
        {
            Wallet = wallet,
            Symbol = "BTC-USD",
            Side = side,
            Type = LiveOrderType.Market,
            Qty = qty,
            Costing = costing,
            LotId = lotId,
        });

    LiveBookResponse Book(string wallet, string symbol = "BTC-USD") =>
        (LiveBookResponse)live.Any(new GetLiveBook { Wallet = wallet, Symbol = symbol });

    WalletBalances Balances(string wallet) =>
        ((GetWalletBalancesResponse)ledger.Any(new GetWalletBalances { Wallet = wallet })).Result;

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
