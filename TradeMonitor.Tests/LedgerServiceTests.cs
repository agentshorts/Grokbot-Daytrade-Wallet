using System.Data;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Data;
using ServiceStack.Host;
using ServiceStack.Web;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;
using ServiceStack.Testing;
using ServiceStack.Validation;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class LedgerServiceTests
{
    static readonly Type[] LedgerRequests =
    [
        typeof(CreateWallet),
        typeof(UpdateWallet),
        typeof(GetWallet),
        typeof(QueryWallets),
        typeof(GetWalletBalances),
        typeof(QueryWalletHistory),
        typeof(RecordTrade),
        typeof(RecordTransfer),
        typeof(RecordPriceMark),
        typeof(RecordPriceMarks),
        typeof(QueryPriceMarks),
        typeof(QueryTrades),
        typeof(GetSummary),
        typeof(GetEquity),
        typeof(PnlByDay),
        typeof(GetPnlBySymbol),
        typeof(GetFees),
        typeof(QueryPositions),
        typeof(GetTrade),
        typeof(RebuildProjections),
    ];

    static readonly (decimal Price, decimal Qty, decimal Sell)[] Legs =
    [
        (1m, 5m, 1.1099m),
        (2m, 2.5m, 2.2198m),
        (4m, 1.25m, 4.4396m),
        (5m, 1m, 5.5495m),
        (2.5m, 2m, 2.77475m),
        (0.5m, 10m, 0.55495m),
        (10m, 0.5m, 11.099m),
        (8m, 0.625m, 8.8792m),
        (1.25m, 4m, 1.387375m),
        (1.6m, 3.125m, 1.77584m),
    ];

    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-ledger-svc-" + Guid.NewGuid().ToString("N") + ".db");
    ServiceStackHost appHost = null!;
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LedgerServices svc = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AppHost.RegisterKey();
        LedgerDialect.Configure(SqliteDialect.Provider);
        factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        var migrator = new Migrator(factory, typeof(Migration1000).Assembly);
        var migrated = migrator.Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");

        clock = new FixedClock { UtcNow = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc) };
        appHost = new BasicAppHost(typeof(LedgerServices).Assembly)
        {
            ConfigureContainer = container =>
            {
                container.Register<IDbConnectionFactory>(factory);
                container.Register<ILedgerClock>(clock);
            },
            ConfigureAppHost = host => host.Plugins.Add(new ValidationFeature()),
        };
        appHost.Init();
        // The web host registers these rules in ValidationFeature.AfterConfigure.
        // BasicAppHost does not run that step, so register them here before the filters run.
        foreach (var type in LedgerRequests)
            Validators.RegisterRequestRulesFor(type);
        svc = new LedgerServices(factory, clock);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        appHost?.Dispose();
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
        clock.UtcNow = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);
        using var db = factory.Open();
        db.DeleteAll<BalanceSnapshot>();
        db.DeleteAll<Position>();
        db.DeleteAll<PriceMark>();
        db.DeleteAll<Transfer>();
        db.DeleteAll<Trade>();
        db.DeleteAll<Wallet>();
    }

    [Test]
    public void Fresh_ledger_has_no_wallets()
    {
        var listed = (QueryWalletsResponse)svc.Any(new QueryWallets { IncludeArchived = true });
        Assert.That(listed.Results, Is.Empty);
        Assert.That(Rows<Wallet>(), Is.EqualTo(0));
    }

    [Test]
    public void Paper_round_trips_match_hand_computed_results_to_the_cent()
    {
        Assert.That(5m * 0.009m, Is.EqualTo(0.045m));
        Assert.That(5m + 0.045m, Is.EqualTo(5.045m));
        Assert.That(5.045m * 1.10m, Is.EqualTo(5.5495m));
        Assert.That(5.5495m - 5.045m, Is.EqualTo(0.5045m));
        Assert.That(50m - 5.045m, Is.EqualTo(44.955m));
        Assert.That(44.955m + 5.5495m, Is.EqualTo(50.5045m));
        Assert.That(5.045m / 5m, Is.EqualTo(1.009m));
        Assert.That(5.045m / 2.5m, Is.EqualTo(2.018m));
        Assert.That(10m * 0.5045m, Is.EqualTo(5.045m));
        Assert.That(10m * 0.045m, Is.EqualTo(0.45m));
        Assert.That(Legs, Has.Length.EqualTo(10));
        foreach (var leg in Legs)
        {
            Assert.That(leg.Price * leg.Qty, Is.EqualTo(5m));
            Assert.That(leg.Sell * leg.Qty, Is.EqualTo(5.5495m));
        }

        var created = Create("daytrade", "Day trade", venue: "Coinbase");
        Assert.That(created.Result.Mode, Is.EqualTo(WalletMode.Paper));
        Assert.That(created.Result.BaseCurrency, Is.EqualTo("USD"));
        Assert.That(created.AlreadyExists, Is.False);

        var t = new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);
        Move("daytrade", "dep-50", TransferKind.Deposit, 50m, t);
        Assert.That(Balances("daytrade").Cash, Is.EqualTo(50m));
        Assert.That(Balances("daytrade").Swept, Is.EqualTo(0m));
        Assert.That(Balances("daytrade").TotalValue, Is.EqualTo(50m));

        var firstBuyId = 0;
        for (var i = 0; i < Legs.Length; i++)
        {
            var leg = Legs[i];
            t = t.AddMinutes(1);
            var buy = Fill("daytrade", $"buy-{i + 1}", TradeSide.Buy, leg.Qty, leg.Price, fee: 0.045m, at: t,
                title: $"leg {i + 1}", orderRef: $"ord-{i + 1}", target: leg.Sell);
            if (i == 0)
            {
                firstBuyId = buy.Result.Id;
                Assert.That(buy.Balance.Cash, Is.EqualTo(44.955m));
                Assert.That(buy.Balance.PositionsValue, Is.EqualTo(5.045m));
                Assert.That(buy.Balance.TotalValue, Is.EqualTo(50m));
                Assert.That(buy.Balance.ValuedAtCost, Is.True);
                Assert.That(buy.Balance.Positions, Has.Count.EqualTo(1));
                Assert.That(buy.Balance.Positions[0].Qty, Is.EqualTo(5m));
                Assert.That(buy.Balance.Positions[0].CostBasis, Is.EqualTo(5.045m));
                Assert.That(buy.Balance.Positions[0].AvgPrice, Is.EqualTo(1.009m));
                Assert.That(buy.Balance.Positions[0].UnrealizedPnl, Is.Null);
                Assert.That(buy.Balance.Positions[0].MarketValue, Is.EqualTo(5.045m));
                Assert.That(buy.Balance.Fees, Is.EqualTo(0.045m));
            }

            if (i == 1)
            {
                Assert.That(buy.Balance.Positions[0].Qty, Is.EqualTo(2.5m));
                Assert.That(buy.Balance.Positions[0].CostBasis, Is.EqualTo(5.045m));
                Assert.That(buy.Balance.Positions[0].AvgPrice, Is.EqualTo(2.018m));
                Assert.That(buy.Balance.RealizedPnl, Is.EqualTo(0.5045m));
                Assert.That(buy.Balance.Fees, Is.EqualTo(0.09m));
            }

            t = t.AddMinutes(1);
            var sell = Fill("daytrade", $"sell-{i + 1}", TradeSide.Sell, leg.Qty, leg.Sell, fee: 0m, at: t, symbol: "btc-usd");
            if (i == 0)
            {
                Assert.That(sell.Balance.Cash, Is.EqualTo(50.5045m));
                Assert.That(sell.Balance.RealizedPnl, Is.EqualTo(0.5045m));
                Assert.That(sell.Balance.Positions, Is.Empty);
                Assert.That(sell.Balance.PositionsValue, Is.EqualTo(0m));
                Assert.That(sell.Balance.TotalValue, Is.EqualTo(50.5045m));
                Assert.That(sell.Result.Symbol, Is.EqualTo("BTC-USD"));
            }
        }

        t = t.AddMinutes(1);
        var sweep = Move("daytrade", "sweep-profit", TransferKind.Sweep, 5.045m, t);
        Assert.That(sweep.Balance.Cash, Is.EqualTo(50m));
        Assert.That(sweep.Balance.Swept, Is.EqualTo(5.045m));
        Assert.That(sweep.Balance.RealizedPnl, Is.EqualTo(5.045m));
        Assert.That(sweep.Balance.Fees, Is.EqualTo(0.45m));
        Assert.That(sweep.Balance.PositionsValue, Is.EqualTo(0m));
        Assert.That(sweep.Balance.TotalValue, Is.EqualTo(50m));
        Assert.That(sweep.Balance.UnrealizedPnl, Is.EqualTo(0m));
        Assert.That(sweep.Balance.Positions, Is.Empty);

        using (var db = factory.Open())
        {
            Assert.That(db.Count<Trade>(), Is.EqualTo(20));
            Assert.That(db.Count<Transfer>(), Is.EqualTo(2));
            Assert.That(db.Count<BalanceSnapshot>(), Is.EqualTo(22));
            var latest = db.Select<BalanceSnapshot>().OrderByDescending(x => x.Id).First();
            Assert.That(latest.Cash, Is.EqualTo(50m));
            Assert.That(latest.Swept, Is.EqualTo(5.045m));
            Assert.That(latest.PositionsValue, Is.EqualTo(0m));
            Assert.That(latest.TotalValue, Is.EqualTo(50m));
            var position = db.Select<Position>().Single();
            Assert.That(position.Symbol, Is.EqualTo("BTC-USD"));
            Assert.That(position.Qty, Is.EqualTo(0m));
            Assert.That(position.CostBasis, Is.EqualTo(0m));
            Assert.That(position.RealizedPnl, Is.EqualTo(5.045m));
            Assert.That(position.FeesPaid, Is.EqualTo(0.45m));
        }

        var replay = Fill("daytrade", "buy-1", TradeSide.Buy, Legs[0].Qty, Legs[0].Price, fee: 0.045m,
            at: new DateTime(2026, 6, 1, 15, 1, 0, DateTimeKind.Utc), title: "leg 1", orderRef: "ord-1", target: Legs[0].Sell);
        Assert.That(replay.AlreadyRecorded, Is.True);
        Assert.That(replay.Result.Id, Is.EqualTo(firstBuyId));
        Assert.That(replay.Balance.Cash, Is.EqualTo(50m));
        Assert.That(replay.Balance.RealizedPnl, Is.EqualTo(5.045m));
        Assert.That(Rows<Trade>(), Is.EqualTo(20));
        Assert.That(Rows<BalanceSnapshot>(), Is.EqualTo(22));

        var history = (QueryWalletHistoryResponse)svc.Any(new QueryWalletHistory { Wallet = "daytrade", Take = 5 });
        Assert.That(history.Total, Is.EqualTo(22));
        Assert.That(history.Results, Has.Count.EqualTo(5));
        Assert.That(history.Results[0].Kind, Is.EqualTo(nameof(TransferKind.Sweep)));
        Assert.That(history.Results[0].Amount, Is.EqualTo(5.045m));

        var buys = (QueryWalletHistoryResponse)svc.Any(new QueryWalletHistory { Wallet = "daytrade", Kind = "buy" });
        Assert.That(buys.Total, Is.EqualTo(10));
        var trades = (QueryTradesResponse)svc.Any(new QueryTrades
        {
            Wallet = "daytrade",
            Symbol = "btc-usd",
            Side = TradeSide.Buy,
        });
        Assert.That(trades.Total, Is.EqualTo(10));
        Assert.That(trades.Results[0].Title, Is.EqualTo("leg 10"));
        Assert.That(trades.Results[0].TargetPrice, Is.EqualTo(Legs[^1].Sell));
        Assert.That(trades.Results[0].Wallet, Is.EqualTo("daytrade"));
    }

    [Test]
    public void Repeating_the_same_values_does_not_write_again()
    {
        var created = Create("alpha", "Alpha", venue: "Coinbase", note: "desk");
        var again = Create("alpha", "Alpha", venue: "Coinbase", note: "desk");
        Assert.That(again.AlreadyExists, Is.True);
        Assert.That(again.Result.Id, Is.EqualTo(created.Result.Id));
        var omittedCurrency = (CreateWalletResponse)svc.Any(new CreateWallet
        {
            Key = "alpha",
            Name = "Alpha",
            Mode = WalletMode.Paper,
            Venue = "Coinbase",
            Note = "desk",
        });
        Assert.That(omittedCurrency.AlreadyExists, Is.True);
        Assert.That(Rows<Wallet>(), Is.EqualTo(1));

        var clash = Fails(() => Create("alpha", "Other"));
        Assert.That(clash.Status, Is.EqualTo(409));
        Assert.That(clash.ErrorCode, Is.EqualTo("DifferentValues"));
        Assert.That(Rows<Wallet>(), Is.EqualTo(1));

        var at = new DateTime(2026, 6, 2, 15, 0, 0, DateTimeKind.Utc);
        Move("alpha", "dep", TransferKind.Deposit, 20m, at);
        var deposit = Move("alpha", "dep", TransferKind.Deposit, 20m, at);
        Assert.That(deposit.AlreadyRecorded, Is.True);
        Assert.That(Rows<Transfer>(), Is.EqualTo(1));
        var depositClash = Fails(() => Move("alpha", "dep", TransferKind.Deposit, 21m, at));
        Assert.That(depositClash.ErrorCode, Is.EqualTo("DifferentValues"));
        Assert.That(Balances("alpha").Cash, Is.EqualTo(20m));

        var buy = Fill("alpha", "t1", TradeSide.Buy, 1m, 5m, fee: 0.1m, at: at, note: "first");
        var buyAgain = Fill("alpha", "t1", TradeSide.Buy, 1m, 5m, fee: 0.1m, at: at, note: "first");
        Assert.That(buyAgain.AlreadyRecorded, Is.True);
        Assert.That(buyAgain.Result.Id, Is.EqualTo(buy.Result.Id));
        Assert.That(Rows<Trade>(), Is.EqualTo(1));
        var snapshots = Rows<BalanceSnapshot>();
        var omittedTime = Fill("alpha", "t1", TradeSide.Buy, 1m, 5m, fee: 0.1m, note: "first");
        Assert.That(omittedTime.AlreadyRecorded, Is.True);
        Assert.That(Rows<BalanceSnapshot>(), Is.EqualTo(snapshots));
        var tradeClash = Fails(() => Fill("alpha", "t1", TradeSide.Buy, 1m, 5m, fee: 0.1m, at: at, note: "changed"));
        Assert.That(tradeClash.ErrorCode, Is.EqualTo("DifferentValues"));
        Assert.That(Rows<Trade>(), Is.EqualTo(1));

        var markAt = new DateTime(2026, 6, 2, 16, 0, 0, DateTimeKind.Utc);
        var mark = (RecordPriceMarkResponse)svc.Any(new RecordPriceMark
        {
            Symbol = "btc-usd",
            Price = 6m,
            At = markAt,
            Wallet = "alpha",
            ClientId = "m1",
            Source = "agent",
        });
        var markAgain = (RecordPriceMarkResponse)svc.Any(new RecordPriceMark
        {
            Symbol = "BTC-USD",
            Price = 6m,
            At = markAt,
            Wallet = "alpha",
            ClientId = "m1",
            Source = "agent",
        });
        Assert.That(markAgain.AlreadyRecorded, Is.True);
        Assert.That(markAgain.Result.Id, Is.EqualTo(mark.Result.Id));
        Assert.That(Rows<PriceMark>(), Is.EqualTo(1));
        var markClash = Fails(() => svc.Any(new RecordPriceMark
        {
            Symbol = "BTC-USD",
            Price = 7m,
            At = markAt,
            Wallet = "alpha",
            ClientId = "m1",
        }));
        Assert.That(markClash.ErrorCode, Is.EqualTo("DifferentValues"));
        Assert.That(Rows<PriceMark>(), Is.EqualTo(1));

        svc.Any(new RecordPriceMark { Symbol = "ETH-USD", Price = 1m });
        svc.Any(new RecordPriceMark { Symbol = "ETH-USD", Price = 2m });
        Assert.That(Rows<PriceMark>(), Is.EqualTo(3));
    }

    [Test]
    public void Rejects_oversell_sweep_above_cash_overspend_and_unknown_wallet()
    {
        Create("alpha");
        Move("alpha", "dep", TransferKind.Deposit, 10m);
        Fill("alpha", "buy", TradeSide.Buy, 1m, 4m);

        var oversell = Fails(() => Fill("alpha", "sell", TradeSide.Sell, 2m, 4m));
        Assert.That(oversell.Status, Is.EqualTo(400));
        Assert.That(oversell.ErrorCode, Is.EqualTo("Oversell"));
        Assert.That(oversell.ToString(), Does.Contain("exceeds the open quantity"));
        Assert.That(Rows<Trade>(), Is.EqualTo(1));
        Assert.That(Balances("alpha").Cash, Is.EqualTo(6m));

        var sweep = Fails(() => Move("alpha", "too-much", TransferKind.Sweep, 6.01m));
        Assert.That(sweep.ErrorCode, Is.EqualTo("InsufficientCash"));
        Assert.That(sweep.ToString(), Does.Contain("Sweep"));
        Assert.That(Balances("alpha").Swept, Is.EqualTo(0m));
        var exact = Move("alpha", "all", TransferKind.Sweep, 6m);
        Assert.That(exact.Balance.Cash, Is.EqualTo(0m));
        Assert.That(exact.Balance.Swept, Is.EqualTo(6m));
        Assert.That(Fails(() => Move("alpha", "more", TransferKind.Sweep, 0.01m)).ErrorCode, Is.EqualTo("InsufficientCash"));

        Create("funded");
        Move("funded", "dep", TransferKind.Deposit, 10m);
        var overspend = Fails(() => Fill("funded", "big", TradeSide.Buy, 1m, 11m));
        Assert.That(overspend.ErrorCode, Is.EqualTo("InsufficientCash"));
        Assert.That(overspend.ToString(), Does.Contain("more than the cash"));
        Assert.That(Rows<Trade>(), Is.EqualTo(1));
        Assert.That(Balances("funded").Cash, Is.EqualTo(10m));

        var withdrawal = Fails(() => Move("funded", "out", TransferKind.Withdrawal, 11m));
        Assert.That(withdrawal.ToString(), Does.Contain("Withdrawal"));
        var withdrawn = Move("funded", "out", TransferKind.Withdrawal, 4m);
        Assert.That(withdrawn.Balance.Cash, Is.EqualTo(6m));
        Assert.That(withdrawn.Balance.Swept, Is.EqualTo(0m));
        Assert.That(withdrawn.Balance.RealizedPnl, Is.EqualTo(0m));

        Create("open");
        var negative = Fill("open", "buy", TradeSide.Buy, 1m, 11m);
        Assert.That(negative.Balance.Cash, Is.EqualTo(-11m));
        Assert.That(negative.Balance.CashIsNegative, Is.True);
        Assert.That(negative.Balance.HasDeposits, Is.False);
        Move("open", "dep", TransferKind.Deposit, 5m);
        Assert.That(Balances("open").Cash, Is.EqualTo(-6m));
        Assert.That(Balances("open").HasDeposits, Is.True);
        Assert.That(Fails(() => Fill("open", "more", TradeSide.Buy, 1m, 1m)).ErrorCode, Is.EqualTo("InsufficientCash"));
        Assert.That(Balances("open").Cash, Is.EqualTo(-6m));

        foreach (var action in new Action[]
        {
            () => Fill("missing", "t", TradeSide.Buy, 1m, 1m),
            () => Move("missing", "t", TransferKind.Deposit, 1m),
            () => svc.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 1m, Wallet = "missing" }),
            () => svc.Any(new GetWalletBalances { Wallet = "missing" }),
            () => svc.Any(new QueryWalletHistory { Wallet = "missing" }),
            () => svc.Any(new UpdateWallet { Key = "missing", Name = "Nope" }),
            () => svc.Any(new GetWallet { Key = "missing" }),
            () => svc.Any(new QueryTrades { Wallet = "missing" }),
        })
        {
            var error = Fails(action);
            Assert.That(error.Status, Is.EqualTo(404));
            Assert.That(error.ErrorCode, Is.EqualTo("UnknownWallet"));
        }
    }

    [Test]
    public void Two_wallets_keep_the_same_symbol_separate()
    {
        Create("alpha");
        Create("beta");
        Move("alpha", "dep", TransferKind.Deposit, 100m);
        Move("beta", "dep", TransferKind.Deposit, 100m);
        Fill("alpha", "shared", TradeSide.Buy, 1m, 10m);
        Fill("beta", "shared", TradeSide.Buy, 2m, 20m);
        Assert.That(Rows<Trade>(), Is.EqualTo(2));

        Fill("alpha", "exit", TradeSide.Sell, 1m, 10m);
        var alpha = Balances("alpha");
        var beta = Balances("beta");
        Assert.That(alpha.Positions, Is.Empty);
        Assert.That(alpha.RealizedPnl, Is.EqualTo(0m));
        Assert.That(alpha.Cash, Is.EqualTo(100m));
        Assert.That(beta.Positions, Has.Count.EqualTo(1));
        Assert.That(beta.Positions[0].Qty, Is.EqualTo(2m));
        Assert.That(beta.Positions[0].CostBasis, Is.EqualTo(40m));
        Assert.That(beta.Cash, Is.EqualTo(60m));
        Assert.That(beta.RealizedPnl, Is.EqualTo(0m));

        var at = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);
        svc.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 30m, At = at, Wallet = "beta", ClientId = "beta-mark" });
        Assert.That(Balances("beta").Positions[0].LastMarkPrice, Is.EqualTo(30m));
        Assert.That(Balances("beta").Positions[0].UnrealizedPnl, Is.EqualTo(20m));
        Assert.That(Balances("alpha").Positions, Is.Empty);

        using var db = factory.Open();
        var alphaRow = db.Single<Position>(x => x.WalletId == db.Single<Wallet>(w => w.Key == "alpha").Id);
        Assert.That(alphaRow.LastMarkPrice, Is.Null);
        Assert.That(alphaRow.Qty, Is.EqualTo(0m));
    }

    [Test]
    public void Price_marks_prefer_the_newest_and_wallet_specific_on_a_tie()
    {
        Create("alpha");
        Create("beta");
        Move("alpha", "dep", TransferKind.Deposit, 100m);
        Move("beta", "dep", TransferKind.Deposit, 100m);
        Fill("alpha", "buy", TradeSide.Buy, 1m, 10m);
        Fill("beta", "buy", TradeSide.Buy, 1m, 10m);
        Assert.That(Balances("alpha").ValuedAtCost, Is.True);
        Assert.That(Balances("alpha").UnrealizedPnl, Is.Null);
        Assert.That(Balances("alpha").PositionsValue, Is.EqualTo(10m));
        Assert.That(Balances("alpha").TotalValue, Is.EqualTo(100m));

        var at = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
        svc.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 11m, At = at, Wallet = "alpha", ClientId = "a-tie" });
        svc.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 10m, At = at, ClientId = "global-tie" });
        Assert.That(Balances("alpha").Positions[0].LastMarkPrice, Is.EqualTo(11m));
        Assert.That(Balances("alpha").Positions[0].UnrealizedPnl, Is.EqualTo(1m));
        Assert.That(Balances("alpha").Positions[0].UnrealizedPnlPercent, Is.EqualTo(10m));
        Assert.That(Balances("alpha").PositionsValue, Is.EqualTo(11m));
        Assert.That(Balances("alpha").TotalValue, Is.EqualTo(101m));
        Assert.That(Balances("alpha").ValuedAtCost, Is.False);
        Assert.That(Balances("beta").Positions[0].LastMarkPrice, Is.EqualTo(10m));
        Assert.That(Balances("beta").PositionsValue, Is.EqualTo(10m));

        var later = at.AddMinutes(1);
        svc.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 12m, At = later, ClientId = "global-new" });
        Assert.That(Balances("alpha").Positions[0].LastMarkPrice, Is.EqualTo(12m));
        Assert.That(Balances("beta").Positions[0].LastMarkPrice, Is.EqualTo(12m));
        Assert.That(Balances("alpha").UnrealizedPnl, Is.EqualTo(2m));

        svc.Any(new RecordPriceMark { Symbol = "BTC-USD", Price = 13m, At = later, Wallet = "beta", ClientId = "b-tie" });
        Assert.That(Balances("beta").Positions[0].LastMarkPrice, Is.EqualTo(13m));
        Assert.That(Balances("beta").Positions[0].UnrealizedPnl, Is.EqualTo(3m));
        Assert.That(Balances("alpha").Positions[0].LastMarkPrice, Is.EqualTo(12m));

        svc.Any(new RecordPriceMark { Symbol = "ETH-USD", Price = 99m, At = later, Wallet = "beta", ClientId = "other-symbol" });
        Assert.That(Balances("beta").Positions[0].LastMarkPrice, Is.EqualTo(13m));
    }

    [Test]
    public void Price_mark_batch_is_atomic_and_idempotent()
    {
        Create("alpha");
        var at = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var batch = (RecordPriceMarksResponse)svc.Any(new RecordPriceMarks
        {
            Marks =
            [
                new PriceMarkWrite { Symbol = "btc-usd", Price = 10m, ClientId = "b1", At = at },
                new PriceMarkWrite { Symbol = "ETH-USD", Price = 20m, ClientId = "b2", At = at, Wallet = "alpha" },
            ],
        });
        Assert.That(batch.Results.Select(x => x.AlreadyRecorded), Is.EqualTo(new[] { false, false }));

        var mixed = (RecordPriceMarksResponse)svc.Any(new RecordPriceMarks
        {
            Marks =
            [
                new PriceMarkWrite { Symbol = "BTC-USD", Price = 10m, ClientId = "b1", At = at },
                new PriceMarkWrite { Symbol = "SOL-USD", Price = 3m, ClientId = "b3", At = at },
            ],
        });
        Assert.That(mixed.Results[0].AlreadyRecorded, Is.True);
        Assert.That(mixed.Results[0].Result.Id, Is.EqualTo(batch.Results[0].Result.Id));
        Assert.That(mixed.Results[1].AlreadyRecorded, Is.False);
        Assert.That(Rows<PriceMark>(), Is.EqualTo(3));

        var clash = Fails(() => svc.Any(new RecordPriceMarks
        {
            Marks =
            [
                new PriceMarkWrite { Symbol = "SOL-USD", Price = 99m, ClientId = "b3", At = at },
                new PriceMarkWrite { Symbol = "XRP-USD", Price = 1m, ClientId = "b4", At = at },
            ],
        }));
        Assert.That(clash.ErrorCode, Is.EqualTo("DifferentValues"));
        Assert.That(clash.ToString(), Does.Contain("Price mark 1:"));
        Assert.That(Rows<PriceMark>(), Is.EqualTo(3));

        var unknown = Fails(() => svc.Any(new RecordPriceMarks
        {
            Marks =
            [
                new PriceMarkWrite { Symbol = "XRP-USD", Price = 1m, ClientId = "b5", At = at },
                new PriceMarkWrite { Symbol = "XRP-USD", Price = 1m, Wallet = "missing", ClientId = "b6", At = at },
            ],
        }));
        Assert.That(unknown.ErrorCode, Is.EqualTo("UnknownWallet"));
        Assert.That(unknown.ToString(), Does.Contain("Price mark 2:"));
        Assert.That(Rows<PriceMark>(), Is.EqualTo(3));

        var applicable = (QueryPriceMarksResponse)svc.Any(new QueryPriceMarks { Wallet = "alpha", Symbol = "BTC-USD" });
        Assert.That(applicable.Results.Select(x => x.Id), Does.Contain(batch.Results[0].Result.Id));
        var eth = (QueryPriceMarksResponse)svc.Any(new QueryPriceMarks { Wallet = "alpha", Symbol = "ETH-USD" });
        Assert.That(eth.Results.Single().Wallet, Is.EqualTo("alpha"));
    }

    [Test]
    public void Sells_match_oldest_lots_first_and_keep_fee_in_cost()
    {
        Create("alpha");
        Move("alpha", "dep", TransferKind.Deposit, 100m);
        Fill("alpha", "a", TradeSide.Buy, 1m, 10m);
        Fill("alpha", "b", TradeSide.Buy, 1m, 30m);
        var sold = Fill("alpha", "c", TradeSide.Sell, 1m, 20m);
        Assert.That(sold.Balance.RealizedPnl, Is.EqualTo(10m));
        Assert.That(sold.Balance.Positions[0].Qty, Is.EqualTo(1m));
        Assert.That(sold.Balance.Positions[0].CostBasis, Is.EqualTo(30m));
        Assert.That(sold.Balance.Positions[0].AvgPrice, Is.EqualTo(30m));
        Assert.That(sold.Balance.Cash, Is.EqualTo(80m));

        Create("fees");
        Move("fees", "dep", TransferKind.Deposit, 100m);
        var bought = Fill("fees", "buy", TradeSide.Buy, 2m, 10m, fee: 2m);
        Assert.That(bought.Balance.Positions[0].CostBasis, Is.EqualTo(22m));
        Assert.That(bought.Balance.Positions[0].AvgPrice, Is.EqualTo(11m));
        var partial = Fill("fees", "sell", TradeSide.Sell, 1m, 11m);
        Assert.That(partial.Balance.RealizedPnl, Is.EqualTo(0m));
        Assert.That(partial.Balance.Positions[0].Qty, Is.EqualTo(1m));
        Assert.That(partial.Balance.Positions[0].CostBasis, Is.EqualTo(11m));
        Assert.That(partial.Balance.Fees, Is.EqualTo(2m));
        Assert.That(partial.Balance.Cash, Is.EqualTo(89m));

        Create("round");
        Move("round", "dep", TransferKind.Deposit, 20m);
        Fill("round", "buy", TradeSide.Buy, 1m, 10m, fee: 1m);
        var closed = Fill("round", "sell", TradeSide.Sell, 1m, 12m, fee: 1m);
        Assert.That(closed.Balance.RealizedPnl, Is.EqualTo(0m));
        Assert.That(closed.Balance.Cash, Is.EqualTo(20m));
        Assert.That(closed.Balance.Fees, Is.EqualTo(2m));

        Create("dust");
        Fill("dust", "buy", TradeSide.Buy, 3m, 1m, fee: 0.01m);
        Fill("dust", "s1", TradeSide.Sell, 1m, 1m);
        Fill("dust", "s2", TradeSide.Sell, 1m, 1m);
        var last = Fill("dust", "s3", TradeSide.Sell, 1m, 1m);
        Assert.That(last.Balance.Positions, Is.Empty);
        Assert.That(last.Balance.RealizedPnl, Is.EqualTo(-0.01m));
        Assert.That(last.Balance.Fees, Is.EqualTo(0.01m));
        using var db = factory.Open();
        var flat = db.Single<Position>(x => x.Symbol == "BTC-USD" && x.WalletId == db.Single<Wallet>(w => w.Key == "dust").Id);
        Assert.That(flat.Qty, Is.EqualTo(0m));
        Assert.That(flat.CostBasis, Is.EqualTo(0m));
    }

    [Test]
    public void Quantity_and_price_keep_eight_decimal_places()
    {
        Create("alpha");
        var qty = 0.12345678m;
        var price = 123456789.12345678m;
        var fill = Fill("alpha", "precise", TradeSide.Buy, qty, price, fee: 0.00000001m);
        Assert.That(fill.Result.Qty, Is.EqualTo(qty));
        Assert.That(fill.Result.Price, Is.EqualTo(price));
        Assert.That(fill.Balance.Positions[0].Qty, Is.EqualTo(qty));
        using var db = factory.Open();
        var stored = db.Single<Trade>(x => x.ClientId == "precise");
        Assert.That(stored.Qty, Is.EqualTo(qty));
        Assert.That(stored.Price, Is.EqualTo(price));
        Assert.That(stored.Fee, Is.EqualTo(0.00000001m));
    }

    [Test]
    public void Wallet_updates_change_name_note_order_and_archive_only()
    {
        Create("alpha", "Alpha");
        Create("beta", "Beta");
        var updated = (UpdateWalletResponse)svc.Any(new UpdateWallet
        {
            Key = "alpha",
            Name = "Alpha Two",
            Note = "desk",
            SortOrder = 5,
            IsArchived = true,
        });
        Assert.That(updated.Result.Key, Is.EqualTo("alpha"));
        Assert.That(updated.Result.Mode, Is.EqualTo(WalletMode.Paper));
        Assert.That(updated.Result.Name, Is.EqualTo("Alpha Two"));
        Assert.That(updated.Result.Note, Is.EqualTo("desk"));
        Assert.That(updated.Result.SortOrder, Is.EqualTo(5));
        Assert.That(updated.Result.IsArchived, Is.True);

        var visible = (QueryWalletsResponse)svc.Any(new QueryWallets());
        Assert.That(visible.Results.Select(x => x.Key), Is.EqualTo(new[] { "beta" }));
        var all = (QueryWalletsResponse)svc.Any(new QueryWallets { IncludeArchived = true });
        Assert.That(all.Results.Select(x => x.Key), Is.EqualTo(new[] { "beta", "alpha" }));

        var cleared = (UpdateWalletResponse)svc.Any(new UpdateWallet { Key = "alpha", Note = "" });
        Assert.That(cleared.Result.Note, Is.Null);
        Assert.That(cleared.Result.Name, Is.EqualTo("Alpha Two"));
        Assert.That(Fails(() => Create("alpha", "Alpha")).ErrorCode, Is.EqualTo("DifferentValues"));
    }

    [Test]
    public void History_and_trade_queries_filter_and_page()
    {
        Create("alpha");
        var t0 = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
        Move("alpha", "dep", TransferKind.Deposit, 50m, t0);
        Fill("alpha", "buy", TradeSide.Buy, 1m, 10m, at: t0.AddMinutes(1), symbol: "ETH-USD");
        Fill("alpha", "sell", TradeSide.Sell, 1m, 12m, at: t0.AddMinutes(2), symbol: "ETH-USD");

        var page = (QueryWalletHistoryResponse)svc.Any(new QueryWalletHistory { Wallet = "alpha", Take = 1 });
        Assert.That(page.Total, Is.EqualTo(3));
        Assert.That(page.Results.Single().Kind, Is.EqualTo("Sell"));
        var middle = (QueryWalletHistoryResponse)svc.Any(new QueryWalletHistory
        {
            Wallet = "alpha",
            From = t0.AddMinutes(1),
            To = t0.AddMinutes(1),
        });
        Assert.That(middle.Results.Select(x => x.Kind), Is.EqualTo(new[] { "Buy" }));
        var badRange = Fails(() => svc.Any(new QueryWalletHistory { Wallet = "alpha", From = t0.AddDays(1), To = t0 }));
        Assert.That(badRange.ToString(), Does.Contain("From is after to"));
        var badKind = Fails(() => svc.Any(new QueryWalletHistory { Wallet = "alpha", Kind = "nope" }));
        Assert.That(badKind.ToString(), Does.Contain("Unknown history kind"));

        Create("beta");
        Fill("beta", "other", TradeSide.Buy, 1m, 3m, at: t0, symbol: "BTC-USD");
        var sells = (QueryTradesResponse)svc.Any(new QueryTrades { Side = TradeSide.Sell });
        Assert.That(sells.Total, Is.EqualTo(1));
        Assert.That(sells.Results[0].Wallet, Is.EqualTo("alpha"));
        var eth = (QueryTradesResponse)svc.Any(new QueryTrades { Wallet = "alpha", Symbol = "eth-usd" });
        Assert.That(eth.Total, Is.EqualTo(2));
    }

    [Test]
    public void Invalid_amounts_are_rejected()
    {
        Create("alpha");
        Assert.That(Fails(() => Fill("alpha", "t", TradeSide.Buy, 0m, 1m)).ToString(), Does.Contain("Quantity"));
        Assert.That(Fails(() => Fill("alpha", "t", TradeSide.Buy, 1m, 0m)).ToString(), Does.Contain("Price"));
        Assert.That(Fails(() => Fill("alpha", "t", TradeSide.Buy, 1m, 1m, fee: -0.01m)).ToString(), Does.Contain("Fee"));
        Assert.That(Fails(() => Move("alpha", "t", TransferKind.Deposit, 0m)).ToString(), Does.Contain("Amount"));
        Assert.That(Fails(() => svc.Any(new CreateWallet { Key = " ", Name = "A", Mode = WalletMode.Paper })).ToString(), Does.Contain("Wallet key"));
        Assert.That(Fails(() => svc.Any(new CreateWallet { Key = "next", Name = "A" })).ToString(), Does.Contain("mode"));
        Assert.That(Rows<Wallet>(), Is.EqualTo(1));
        Assert.That(Rows<Trade>(), Is.EqualTo(0));
    }

    [Test]
    public void Ledger_services_require_an_authenticated_admin()
    {
        foreach (var type in LedgerRequests)
        {
            var admin = type.GetCustomAttributes(typeof(ValidateIsAdminAttribute), inherit: true);
            Assert.That(admin, Is.Not.Empty, type.Name);
            var error = Invoke(Activator.CreateInstance(type)!, session: null);
            Assert.That(error.Status, Is.EqualTo(401), type.Name);
        }

        var employee = Invoke(new QueryWallets(), Session("employee", "Employee"));
        Assert.That(employee.Status, Is.EqualTo(403));

        var allowed = InvokeOk<QueryWalletsResponse>(new QueryWallets(), Session("admin", RoleNames.Admin));
        Assert.That(allowed.Results, Is.Empty);

        var pastAuth = Invoke(new CreateWallet(), Session("admin", RoleNames.Admin));
        Assert.That(pastAuth.Status, Is.EqualTo(400));
    }

    CreateWalletResponse Create(string key, string? name = null, WalletMode mode = WalletMode.Paper, string? venue = null, string? note = null) =>
        (CreateWalletResponse)svc.Any(new CreateWallet
        {
            Key = key,
            Name = name ?? key,
            Mode = mode,
            Venue = venue,
            Note = note,
        });

    RecordTransferResponse Move(string wallet, string clientId, TransferKind kind, decimal amount, DateTime? at = null) =>
        (RecordTransferResponse)svc.Any(new RecordTransfer
        {
            Wallet = wallet,
            ClientId = clientId,
            Kind = kind,
            Amount = amount,
            At = at,
        });

    RecordTradeResponse Fill(
        string wallet,
        string clientId,
        TradeSide side,
        decimal qty,
        decimal price,
        decimal fee = 0m,
        DateTime? at = null,
        string symbol = "BTC-USD",
        string? note = null,
        decimal? target = null,
        string? title = null,
        string? orderRef = null) =>
        (RecordTradeResponse)svc.Any(new RecordTrade
        {
            Wallet = wallet,
            ClientId = clientId,
            Symbol = symbol,
            Side = side,
            Qty = qty,
            Price = price,
            Fee = fee,
            ExecutedAt = at,
            Note = note,
            TargetPrice = target,
            Title = title,
            OrderRef = orderRef,
        });

    WalletBalances Balances(string wallet) =>
        ((GetWalletBalancesResponse)svc.Any(new GetWalletBalances { Wallet = wallet })).Result;

    int Rows<T>()
    {
        using var db = factory.Open();
        return (int)db.Count<T>();
    }

    static HttpError Fails(Action action)
    {
        var error = Assert.Throws<HttpError>(() => action());
        Assert.That(error, Is.Not.Null);
        return error!;
    }

    HttpError Invoke(object dto, AuthUserSession? session)
    {
        var req = Request(dto, session);
        try
        {
            appHost.ApplyRequestFiltersAsync(req, req.Response, dto).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var filtered = ex as HttpError ?? ex.InnerException as HttpError;
            if (filtered != null)
                return filtered;
            throw;
        }

        object result;
        try
        {
            result = appHost.ServiceController.Execute(dto, req);
        }
        catch (Exception ex)
        {
            var failed = ex as HttpError ?? ex.InnerException as HttpError;
            if (failed != null)
                return failed;
            throw;
        }

        if (result is HttpError http)
            return http;

        var status = result.GetType().GetProperty("ResponseStatus")?.GetValue(result) as ResponseStatus;
        Assert.Fail($"Expected an auth error for {dto.GetType().Name}, got {result.GetType().Name} {status?.ErrorCode} {status?.Message}");
        throw new InvalidOperationException("Expected an auth error.");
    }

    T InvokeOk<T>(object dto, AuthUserSession session)
    {
        var req = Request(dto, session);
        appHost.ApplyRequestFiltersAsync(req, req.Response, dto).GetAwaiter().GetResult();
        var result = appHost.ServiceController.Execute(dto, req);
        Assert.That(result, Is.InstanceOf<T>(), result?.ToString());
        return (T)result!;
    }

    static IRequest Request(object dto, AuthUserSession? session)
    {
        var req = new BasicRequest(dto);
        if (session != null)
            req.Items[Keywords.Session] = session;
        return req;
    }

    static AuthUserSession Session(string name, string role) => new()
    {
        IsAuthenticated = true,
        UserName = name,
        DisplayName = name,
        Roles = [role],
    };

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
