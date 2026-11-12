using NUnit.Framework;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;
using ServiceStack.Testing;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class PredictionMarketTests
{
    static readonly DateTime BuyAt = new(2026, 11, 12, 14, 23, 0, DateTimeKind.Utc);
    static readonly DateTime SellAt = new(2026, 11, 12, 14, 27, 0, DateTimeKind.Utc);
    const string Example = "pm-KXBTCD-26NOV1210-T71199.99-KALSHI";
    const string ExampleSymbol = "KXBTCD-26NOV1210-T71199.99-KALSHI";

    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-pm-" + Guid.NewGuid().ToString("N") + ".db");
    ServiceStackHost appHost = null!;
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LedgerServices ledger = null!;
    PnlServices pnl = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AppHost.RegisterKey();
        LedgerDialect.Configure(SqliteDialect.Provider);
        factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        var migrated = new Migrator(factory, typeof(Migration1000).Assembly).Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
        clock = new FixedClock { UtcNow = SellAt };
        appHost = new BasicAppHost(typeof(LedgerServices).Assembly)
        {
            ConfigureContainer = container =>
            {
                container.Register<IDbConnectionFactory>(factory);
                container.Register<ILedgerClock>(clock);
            },
        };
        appHost.Init();
        ledger = new LedgerServices(factory, clock);
        pnl = new PnlServices(factory, clock);
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
        clock.UtcNow = SellAt;
        using var db = factory.Open();
        db.DeleteAll<BalanceSnapshot>();
        db.DeleteAll<Position>();
        db.DeleteAll<PriceMark>();
        db.DeleteAll<Transfer>();
        db.DeleteAll<Trade>();
        db.DeleteAll<PaperOrder>();
        db.DeleteAll<Wallet>();
    }

    [Test]
    public void Product_detection_uses_type_venue_suffix_and_hand_wallets()
    {
        Assert.That(PredictionMarkets.IsProductId(ExampleSymbol), Is.True);
        Assert.That(PredictionMarkets.IsProductId("btc-usd"), Is.False);
        Assert.That(PredictionMarkets.IsHandWallet(Example), Is.True);
        Assert.That(PredictionMarkets.IsHandWallet("PM-OTHER"), Is.True);
        Assert.That(PredictionMarkets.IsHandWallet("coinbase-btc"), Is.False);
        Assert.That(PredictionMarkets.IsHandWallet("kalshi-real"), Is.False);
        Assert.That(PredictionMarkets.FieldsLookPrediction("PREDICTION_MARKET", null, null), Is.True);
        Assert.That(PredictionMarkets.FieldsLookPrediction(null, "KALSHI", null), Is.True);
        Assert.That(PredictionMarkets.FieldsLookPrediction("SPOT", "COINBASE", null), Is.False);
        Assert.That(PredictionMarkets.ProductLooksPrediction(
            new CbProduct("0.01", null, null, null, "KALSHI"), "PX-EVENT-2"), Is.True);
        Assert.That(PredictionMarkets.ProductLooksPrediction(null, ExampleSymbol), Is.True);
        Assert.That(PredictionMarkets.IsKalshiSlug("coinbase-kxbtcd-26nov1212-t71699-99-kalshi"), Is.True);
    }

    [Test]
    public void Pm_key_is_a_prediction_wallet_and_standard_is_rejected()
    {
        var created = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = Example,
            Name = "BTC 71199.99",
            Mode = WalletMode.Real,
            Venue = "Coinbase",
        });
        Assert.That(created.Result.Kind, Is.EqualTo(WalletKind.PredictionMarket));
        Assert.That(created.AlreadyExists, Is.False);

        var again = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = Example,
            Name = "BTC 71199.99",
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            Kind = WalletKind.PredictionMarket,
        });
        Assert.That(again.AlreadyExists, Is.True);

        var error = Assert.Throws<HttpError>(() => ledger.Any(new CreateWallet
        {
            Key = "pm-OTHER",
            Name = "Other",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
        }));
        Assert.That(error!.Message, Does.Contain("prediction"));

        var spot = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = "coinbase-btc",
            Name = "Coinbase BTC",
            Mode = WalletMode.Real,
        });
        Assert.That(spot.Result.Kind, Is.EqualTo(WalletKind.Standard));
    }

    [Test]
    public void Example_round_trip_realizes_minus_0_50_after_fees()
    {
        // 8.25 * 0.90 + 0.20 fee = 7.625 cost. 8.25 * 0.88 - 0.135 fee = 7.125 proceeds. Realized -0.50.
        var row = BookExample(sellPrice: 0.88m, sellFee: 0.135m, kind: TradeKind.Fill);
        Assert.That(row.Bought, Is.EqualTo(8.25m));
        Assert.That(row.Sold, Is.EqualTo(8.25m));
        Assert.That(row.Fees, Is.EqualTo(0.335m));
        Assert.That(row.RealizedPnl, Is.EqualTo(-0.50m));
        Assert.That(row.Settlement, Is.EqualTo("closed"));
        Assert.That(row.Symbol, Is.EqualTo(ExampleSymbol));
    }

    [Test]
    public void Settlement_at_zero_realizes_the_full_cost()
    {
        var row = BookExample(sellPrice: 0m, sellFee: 0m, kind: TradeKind.Settlement);
        Assert.That(row.RealizedPnl, Is.EqualTo(-7.625m));
        Assert.That(row.Fees, Is.EqualTo(0.20m));
        Assert.That(row.Settlement, Is.EqualTo("settled-0"));
        Assert.That(row.Sold, Is.EqualTo(8.25m));
    }

    [Test]
    public void Settlement_at_one_realizes_the_winning_payout()
    {
        var row = BookExample(sellPrice: 1m, sellFee: 0m, kind: TradeKind.Settlement);
        Assert.That(row.RealizedPnl, Is.EqualTo(0.625m));
        Assert.That(row.Fees, Is.EqualTo(0.20m));
        Assert.That(row.Settlement, Is.EqualTo("settled-1"));
    }

    [Test]
    public void Prediction_view_totals_prediction_wallets_and_skips_archived_ones()
    {
        BookExample(sellPrice: 0.88m, sellFee: 0.135m, kind: TradeKind.Fill);
        var winner = "pm-KXBTCD-26NOV1211-T70000-KALSHI";
        Open(winner, "BTC 70000");
        Deposit(winner, "cash", 100m);
        Fill(winner, "buy", TradeSide.Buy, 2m, 0.40m, 0.05m, BuyAt, TradeKind.Fill, "KXBTCD-26NOV1211-T70000-KALSHI");
        Fill(winner, "sell", TradeSide.Sell, 2m, 1m, 0m, SellAt, TradeKind.Settlement, "KXBTCD-26NOV1211-T70000-KALSHI");
        Open("coinbase-btc", "Coinbase BTC");
        Deposit("coinbase-btc", "cash", 100m);
        Fill("coinbase-btc", "buy", TradeSide.Buy, 1m, 10m, 0m, BuyAt, TradeKind.Fill, "BTC-USD");

        var view = (GetPredictionPnlResponse)pnl.Any(new GetPredictionPnl());
        Assert.That(view.Wallets.Select(row => row.Wallet), Is.EquivalentTo(new[] { Example, winner }));
        Assert.That(view.Total.Wallets, Is.EqualTo(2));
        Assert.That(view.Total.Bought, Is.EqualTo(10.25m));
        Assert.That(view.Total.Sold, Is.EqualTo(10.25m));
        Assert.That(view.Total.Fees, Is.EqualTo(0.385m));
        Assert.That(view.Total.RealizedPnl, Is.EqualTo(0.65m));

        ledger.Any(new UpdateWallet { Key = winner, IsArchived = true });
        var visible = (GetPredictionPnlResponse)pnl.Any(new GetPredictionPnl());
        Assert.That(visible.Wallets.Select(row => row.Wallet), Is.EqualTo(new[] { Example }));
        var restored = (GetPredictionPnlResponse)pnl.Any(new GetPredictionPnl { IncludeArchived = true });
        Assert.That(restored.Wallets.Select(row => row.Wallet), Is.EquivalentTo(new[] { Example, winner }));
    }

    [Test]
    public void Archive_hides_sync_contract_wallets_and_can_run_twice()
    {
        using (var db = factory.Open())
            SeedSyncRows(db);

        using (var db = factory.Open())
        {
            var first = PredictionMarketArchive.Apply(db);
            Assert.That(first.Tagged, Is.EqualTo(1));
            Assert.That(first.Archived, Is.EqualTo(2));
            Assert.That(first.ExcludedTransfers, Is.EqualTo(2));
            Assert.That(first.Lines, Does.Contain("tagged prediction wallet " + Example));
            Assert.That(first.Lines.Any(line => line.StartsWith("archived prediction wallet coinbase-kxbtcd-26nov1212-t71699-99-kalshi", StringComparison.Ordinal)), Is.True);
            Assert.That(first.Lines, Does.Contain("excluded transfer cb-fund-src-trade9 on coinbase-usdc"));
            Assert.That(first.Lines, Does.Contain("excluded transfer cb-proceeds-dst-trade9 on coinbase-usdc"));

            var second = PredictionMarketArchive.Apply(db);
            Assert.That(second.Tagged, Is.EqualTo(0));
            Assert.That(second.Archived, Is.EqualTo(0));
            Assert.That(second.ExcludedTransfers, Is.EqualTo(0));
            Assert.That(second.Lines, Is.Empty);
        }

        using var read = factory.Open();
        var hand = read.Single<Wallet>(wallet => wallet.Key == Example);
        Assert.That(hand.Kind, Is.EqualTo(WalletKind.PredictionMarket));
        Assert.That(hand.IsArchived, Is.False);
        Assert.That(read.Count<Trade>(trade => trade.WalletId == hand.Id), Is.EqualTo(1));

        var contract = read.Single<Wallet>(wallet => wallet.Key == "coinbase-kxbtcd-26nov1212-t71699-99-kalshi");
        var shell = read.Single<Wallet>(wallet => wallet.Key == "coinbase-kxbtcd-26nov1211-t70000-00-kalshi");
        Assert.That(contract.IsArchived, Is.True);
        Assert.That(shell.IsArchived, Is.True);
        Assert.That(read.Count<Trade>(trade => trade.WalletId == contract.Id), Is.EqualTo(2));
        Assert.That(read.Count<Transfer>(transfer => transfer.WalletId == contract.Id), Is.EqualTo(1));
        Assert.That(read.Count<PriceMark>(mark => mark.WalletId == contract.Id), Is.EqualTo(1));

        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "coinbase-btc").IsArchived, Is.False);
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "kalshi-real").IsArchived, Is.False);
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "kalshi-real").Kind, Is.EqualTo(WalletKind.Standard));
        Assert.That(read.Single<Transfer>(transfer => transfer.ClientId == "cb-fund-src-spot1").Excluded, Is.False);
        Assert.That(read.Single<Transfer>(transfer => transfer.ClientId == "seed-usdc").Excluded, Is.False);
        Assert.That(read.Single<Transfer>(transfer => transfer.ClientId == "cb-fund-src-trade9").Excluded, Is.True);
        Assert.That(read.Single<Transfer>(transfer => transfer.ClientId == "cb-proceeds-dst-trade9").Excluded, Is.True);

        var cash = ((GetWalletBalancesResponse)ledger.Any(new GetWalletBalances { Wallet = "coinbase-usdc" })).Result;
        Assert.That(cash.Cash, Is.EqualTo(99m));
        var history = (QueryWalletHistoryResponse)ledger.Any(new QueryWalletHistory { Wallet = "coinbase-usdc", Take = 50 });
        Assert.That(history.Results.Select(row => row.ClientId), Is.EquivalentTo(new[] { "cb-fund-src-spot1", "seed-usdc" }));
        var included = (QueryWalletHistoryResponse)ledger.Any(new QueryWalletHistory
        {
            Wallet = "coinbase-usdc",
            Take = 50,
            IncludeExcluded = true,
        });
        Assert.That(included.Results.Select(row => row.ClientId), Does.Contain("cb-fund-src-trade9").And.Contain("cb-proceeds-dst-trade9"));

        var trades = (QueryTradesResponse)ledger.Any(new QueryTrades { Take = 50 });
        Assert.That(trades.Results.Select(row => row.ClientId), Does.Not.Contain("cb-fill-trade9"));
        Assert.That(trades.Results.Select(row => row.ClientId), Does.Contain("hand-buy"));
        var named = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = contract.Key, Take = 50 });
        Assert.That(named.Results.Select(row => row.ClientId), Does.Contain("cb-fill-trade9"));

        var marks = (QueryPriceMarksResponse)ledger.Any(new QueryPriceMarks { Take = 50 });
        Assert.That(marks.Results.Select(row => row.ClientId), Does.Not.Contain("cb-mark-bad"));
        var walletMarks = (QueryPriceMarksResponse)ledger.Any(new QueryPriceMarks { Wallet = contract.Key, Take = 50 });
        Assert.That(walletMarks.Results.Select(row => row.ClientId), Does.Contain("cb-mark-bad"));

        var summary = (GetSummaryResponse)pnl.Any(new GetSummary());
        Assert.That(summary.Wallets.Select(row => row.Key), Does.Not.Contain(contract.Key));
        Assert.That(summary.Wallets.Select(row => row.Key), Does.Contain(Example));

        contract.IsArchived = false;
        read.Update(contract);
        var sweep = read.Single<Transfer>(transfer => transfer.ClientId == "cb-fund-src-trade9");
        sweep.Excluded = false;
        read.Update(sweep);
        ledger.Any(new RebuildProjections { Wallet = "coinbase-usdc" });
        var back = (GetSummaryResponse)pnl.Any(new GetSummary());
        Assert.That(back.Wallets.Select(row => row.Key), Does.Contain(contract.Key));
        var restoredCash = ((GetWalletBalancesResponse)ledger.Any(new GetWalletBalances { Wallet = "coinbase-usdc" })).Result;
        Assert.That(restoredCash.Cash, Is.EqualTo(89m));
    }

    [Test]
    public void Migration_1004_archives_rows_that_were_already_stored()
    {
        using (var db = factory.Open())
        {
            SeedSyncRows(db);
            db.ExecuteSql("ALTER TABLE Wallet DROP COLUMN Kind");
            db.ExecuteSql("ALTER TABLE Transfer DROP COLUMN Excluded");
            db.ExecuteSql("DELETE FROM Migration WHERE Name = 'Migration1004'");
        }

        var migrated = new Migrator(factory, typeof(Migration1000).Assembly).Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");

        using var read = factory.Open();
        Assert.That(read.ColumnExists<Wallet>(wallet => wallet.Kind), Is.True);
        Assert.That(read.ColumnExists<Transfer>(transfer => transfer.Excluded), Is.True);
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == Example).Kind, Is.EqualTo(WalletKind.PredictionMarket));
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == Example).IsArchived, Is.False);
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "coinbase-kxbtcd-26nov1212-t71699-99-kalshi").IsArchived, Is.True);
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "coinbase-btc").IsArchived, Is.False);
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "kalshi-real").IsArchived, Is.False);
        Assert.That(read.Single<Transfer>(transfer => transfer.ClientId == "cb-fund-src-trade9").Excluded, Is.True);
        Assert.That(read.Single<Transfer>(transfer => transfer.ClientId == "cb-fund-src-spot1").Excluded, Is.False);
        Assert.That(read.Count<Trade>(), Is.EqualTo(5));

        var again = new Migrator(factory, typeof(Migration1000).Assembly).Run();
        Assert.That(again.Succeeded, Is.True, again.Error?.ToString() ?? "second run failed");
        Assert.That(read.Single<Wallet>(wallet => wallet.Key == "coinbase-btc").IsArchived, Is.False);
        Assert.That(read.Count<Trade>(), Is.EqualTo(5));
    }

    PredictionPnlRow BookExample(decimal sellPrice, decimal sellFee, TradeKind kind)
    {
        Open(Example, "BTC 71199.99");
        Deposit(Example, "cash", 100m);
        Fill(Example, "buy", TradeSide.Buy, 8.25m, 0.90m, 0.20m, BuyAt, TradeKind.Fill, ExampleSymbol);
        Fill(Example, "sell", TradeSide.Sell, 8.25m, sellPrice, sellFee, SellAt, kind, ExampleSymbol);
        var view = (GetPredictionPnlResponse)pnl.Any(new GetPredictionPnl());
        return view.Wallets.Single(row => row.Wallet == Example);
    }

    void Open(string key, string name) =>
        ledger.Any(new CreateWallet
        {
            Key = key,
            Name = name,
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            BaseCurrency = "USD",
        });

    void Deposit(string wallet, string clientId, decimal amount) =>
        ledger.Any(new RecordTransfer
        {
            Wallet = wallet,
            ClientId = clientId,
            Kind = TransferKind.Deposit,
            Amount = amount,
            At = BuyAt.AddMinutes(-1),
        });

    void Fill(string wallet, string clientId, TradeSide side, decimal qty, decimal price, decimal fee, DateTime at, TradeKind kind, string symbol) =>
        ledger.Any(new RecordTrade
        {
            Wallet = wallet,
            ClientId = clientId,
            Symbol = symbol,
            Side = side,
            Qty = qty,
            Price = price,
            Fee = fee,
            Kind = kind,
            ExecutedAt = at,
        });

    static void SeedSyncRows(System.Data.IDbConnection db)
    {
        var at = BuyAt;
        var handId = (int)db.Insert(new Wallet
        {
            Key = Example,
            Name = "Hand",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
            BaseCurrency = "USD",
            CreatedAt = at,
        }, selectIdentity: true);
        db.Insert(new Trade
        {
            WalletId = handId,
            ClientId = "hand-buy",
            Symbol = ExampleSymbol,
            Side = TradeSide.Buy,
            Qty = 1m,
            Price = 0.5m,
            Fee = 0m,
            ExecutedAt = at,
            RecordedAt = at,
        });

        var contractId = (int)db.Insert(new Wallet
        {
            Key = "coinbase-kxbtcd-26nov1212-t71699-99-kalshi",
            Name = "Coinbase KXBTCD",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
            BaseCurrency = "USD",
            Venue = "Coinbase",
            CreatedAt = at,
        }, selectIdentity: true);
        db.Insert(new Trade
        {
            WalletId = contractId,
            ClientId = "cb-fill-trade9",
            Symbol = "KXBTCD-26NOV1212-T71699.99-KALSHI",
            Side = TradeSide.Sell,
            Qty = 8.25m,
            Price = 0.88m,
            Fee = 0.1m,
            ExecutedAt = SellAt,
            RecordedAt = SellAt,
        });
        db.Insert(new Trade
        {
            WalletId = contractId,
            ClientId = "cb-fill-trade8",
            Symbol = "KXBTCD-26NOV1212-T71699.99-KALSHI",
            Side = TradeSide.Buy,
            Qty = 8.25m,
            Price = 0.90m,
            Fee = 0.1m,
            ExecutedAt = at,
            RecordedAt = at,
        });
        db.Insert(new Transfer
        {
            WalletId = contractId,
            ClientId = "cb-fund-trade9",
            Kind = TransferKind.Deposit,
            Amount = 1m,
            Currency = "USD",
            At = at,
            RecordedAt = at,
        });
        db.Insert(new PriceMark
        {
            ClientId = "cb-mark-bad",
            Symbol = "KXBTCD-26NOV1212-T71699.99-KALSHI",
            Price = 0.88m,
            At = SellAt,
            Source = "walletsync",
            WalletId = contractId,
            RecordedAt = SellAt,
        });

        db.Insert(new Wallet
        {
            Key = "coinbase-kxbtcd-26nov1211-t70000-00-kalshi",
            Name = "Coinbase shell",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
            BaseCurrency = "USD",
            CreatedAt = at,
        });

        var cashId = (int)db.Insert(new Wallet
        {
            Key = "coinbase-usdc",
            Name = "Coinbase USDC",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
            BaseCurrency = "USD",
            CreatedAt = at,
        }, selectIdentity: true);
        db.Insert(new Transfer
        {
            WalletId = cashId,
            ClientId = "seed-usdc",
            Kind = TransferKind.Deposit,
            Amount = 100m,
            Currency = "USD",
            At = at,
            RecordedAt = at,
        });
        db.Insert(new Transfer
        {
            WalletId = cashId,
            ClientId = "cb-fund-src-trade9",
            Kind = TransferKind.Withdrawal,
            Amount = 10m,
            Currency = "USD",
            At = at,
            RecordedAt = at,
        });
        db.Insert(new Transfer
        {
            WalletId = cashId,
            ClientId = "cb-proceeds-dst-trade9",
            Kind = TransferKind.Deposit,
            Amount = 4m,
            Currency = "USD",
            At = SellAt,
            RecordedAt = SellAt,
        });
        db.Insert(new Transfer
        {
            WalletId = cashId,
            ClientId = "cb-fund-src-spot1",
            Kind = TransferKind.Withdrawal,
            Amount = 1m,
            Currency = "USD",
            At = at,
            RecordedAt = at,
        });

        var spotId = (int)db.Insert(new Wallet
        {
            Key = "coinbase-btc",
            Name = "Coinbase BTC",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
            BaseCurrency = "USD",
            CreatedAt = at,
        }, selectIdentity: true);
        db.Insert(new Trade
        {
            WalletId = spotId,
            ClientId = "cb-fill-spot1",
            Symbol = "BTC-USD",
            Side = TradeSide.Buy,
            Qty = 0.01m,
            Price = 100m,
            Fee = 0m,
            ExecutedAt = at,
            RecordedAt = at,
        });

        var kalshiId = (int)db.Insert(new Wallet
        {
            Key = "kalshi-real",
            Name = "Kalshi",
            Mode = WalletMode.Real,
            Kind = WalletKind.Standard,
            BaseCurrency = "USD",
            CreatedAt = at,
        }, selectIdentity: true);
        db.Insert(new Trade
        {
            WalletId = kalshiId,
            ClientId = "kalshi-fill",
            Symbol = "KXBTCD-26NOV1212-T71699.99-KALSHI",
            Side = TradeSide.Buy,
            Qty = 1m,
            Price = 0.4m,
            Fee = 0m,
            ExecutedAt = at,
            RecordedAt = at,
        });
    }

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
