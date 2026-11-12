using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class CoinbaseSyncTests
{
    static readonly DateTime T1 = new(2026, 11, 12, 14, 11, 5, 293, DateTimeKind.Utc);
    static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-cbsync-" + Guid.NewGuid().ToString("N") + ".db");
    readonly string statePath = Path.Combine(Path.GetTempPath(), "trademonitor-cbsync-" + Guid.NewGuid().ToString("N") + ".json");
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LedgerServices svc = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AppHost.RegisterKey();
        LedgerDialect.Configure(SqliteDialect.Provider);
        factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        var migrated = new Migrator(factory, typeof(Migration1000).Assembly).Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
        clock = new FixedClock { UtcNow = Now };
        svc = new LedgerServices(factory, clock);
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
        if (File.Exists(statePath))
            File.Delete(statePath);
    }

    [SetUp]
    public void SetUp()
    {
        clock.UtcNow = Now;
        using var db = factory.Open();
        db.DeleteAll<BalanceSnapshot>();
        db.DeleteAll<Position>();
        db.DeleteAll<PriceMark>();
        db.DeleteAll<Transfer>();
        db.DeleteAll<Trade>();
        db.DeleteAll<Wallet>();
        if (File.Exists(statePath))
            File.Delete(statePath);
    }

    [Test]
    public void Wallet_keys_match_the_python_names()
    {
        Assert.That(CoinbaseWallets.KeyFor("SBIT"), Is.EqualTo("coinbase-sbit"));
        Assert.That(CoinbaseWallets.KeyFor("ETHD"), Is.EqualTo("coinbase-ethd"));
        Assert.That(CoinbaseWallets.KeyFor("USDC"), Is.EqualTo("coinbase-usdc"));
        Assert.That(CoinbaseWallets.KeyFor("NUB"), Is.EqualTo("coinbase-nub"));
        Assert.That(CoinbaseWallets.KeyFor("BTC"), Is.EqualTo("coinbase-btc"));
        Assert.That(CoinbaseWallets.KeyFor("NBA"), Is.EqualTo("coinbase-nba"));
    }

    [Test]
    public async Task Known_python_fill_is_not_written_again()
    {
        SeedBooks();
        using var rig = Open();
        await rig.Syncer.CycleAsync(CancellationToken.None);
        await rig.Syncer.CycleAsync(CancellationToken.None);

        using var db = factory.Open();
        var fills = db.Select<Trade>(trade => trade.ClientId == "cb-fill-t1");
        Assert.That(fills, Has.Count.EqualTo(1));
        Assert.That(fills[0].Note, Is.EqualTo("seed"));
        Assert.That(db.Select<Transfer>(transfer => transfer.ClientId.StartsWith("cb-fund-")), Is.Empty);
        Assert.That(rig.Handler.Calls.All(call => call.Method == "GET"));
        Assert.That(rig.Handler.Calls.All(call => CoinbaseReadPaths.IsAllowed(call.Path)));
        Assert.That(rig.Handler.Calls.Any(call => call.Path == "/api/v3/brokerage/accounts"));
    }

    [Test]
    public async Task New_fill_is_recorded_once_with_funding_and_survives_restart()
    {
        SeedBooks();
        using var rig = Open();
        await rig.Syncer.CycleAsync(CancellationToken.None);
        rig.Handler.Fills.Add(Eq("t2", "2026-11-12T15:00:00.000Z"));
        rig.Handler.EthdQty = 0.1m;
        await rig.Syncer.CycleAsync(CancellationToken.None);
        await rig.Syncer.CycleAsync(CancellationToken.None);

        var restarted = new CoinbaseSyncer(rig.Client, rig.Gateway, clock, rig.Log.Sink, statePath);
        await restarted.CycleAsync(CancellationToken.None);

        using var db = factory.Open();
        var trades = db.Select<Trade>(trade => trade.ClientId == "cb-fill-t2");
        Assert.That(trades, Has.Count.EqualTo(1));
        Assert.That(trades[0].Qty, Is.EqualTo(0.1m));
        Assert.That(trades[0].Kind, Is.EqualTo(TradeKind.Fill));
        Assert.That(trades[0].Symbol, Is.EqualTo("ETHD-USD"));
        var usdc = db.Single<Wallet>(wallet => wallet.Key == "coinbase-usdc");
        var funded = db.Single<Transfer>(transfer => transfer.ClientId == "cb-fund-src-t2");
        Assert.That(usdc.Id, Is.EqualTo(10));
        Assert.That(funded.WalletId, Is.EqualTo(10));
        Assert.That(db.Select<Transfer>(transfer => transfer.ClientId == "cb-fund-t2"), Has.Count.EqualTo(1));
        Assert.That(Balances("coinbase-usdc").Cash, Is.EqualTo(96m));
        Assert.That(Balances("coinbase-ethd").Cash, Is.EqualTo(0m));
        Assert.That(db.Exists<Wallet>(wallet => wallet.Key == "coinbase-ethd"));
        Assert.That(rig.Log.Lines, Does.Contain("recorded cb-fill-t2 Buy 0.1 ETHD-USD @ 40 fee 0"));
    }

    [Test]
    public async Task Sell_moves_proceeds_to_the_cash_wallet()
    {
        SeedBooks();
        using var rig = Open();
        rig.Handler.Fills.Add(Eq("t3", "2026-11-12T15:00:00.000Z", "SELL", "41", "1", false, "SBIT"));
        await rig.Syncer.CycleAsync(CancellationToken.None);

        Assert.That(Balances("coinbase-usdc").Cash, Is.EqualTo(141m));
        Assert.That(Balances("coinbase-sbit").Cash, Is.EqualTo(0m));
        using var db = factory.Open();
        Assert.That(db.Exists<Transfer>(transfer => transfer.ClientId == "cb-proceeds-t3"));
        Assert.That(db.Exists<Transfer>(transfer => transfer.ClientId == "cb-proceeds-dst-t3"));
    }

    [Test]
    public async Task Unchanged_price_is_not_marked_again()
    {
        SeedBooks();
        using var rig = Open();
        await rig.Syncer.CycleAsync(CancellationToken.None);
        await rig.Syncer.CycleAsync(CancellationToken.None);
        Assert.That(MarkIds(), Is.EqualTo(new[] { "cb-mark-sbit-SBIT-USD-20261112T1500" }));

        rig.Handler.SbitFiat = 40.5m;
        clock.UtcNow = Now.AddMinutes(1);
        await rig.Syncer.CycleAsync(CancellationToken.None);
        Assert.That(MarkIds(), Is.EqualTo(new[]
        {
            "cb-mark-sbit-SBIT-USD-20261112T1500",
            "cb-mark-sbit-SBIT-USD-20261112T1501",
        }));
    }

    [Test]
    public async Task Rejected_fill_is_not_retried_every_cycle()
    {
        SeedBooks();
        using var rig = Open();
        var counting = new CountingGateway(rig.Gateway);
        var syncer = new CoinbaseSyncer(rig.Client, counting, clock, rig.Log.Sink, statePath);
        rig.Handler.Fills.Add(Eq("t4", "2026-11-12T15:00:00.000Z", "SELL", "41", "5", false, "SBIT"));
        await syncer.CycleAsync(CancellationToken.None);
        await syncer.CycleAsync(CancellationToken.None);

        Assert.That(syncer.State.SkippedFills.ContainsKey("cb-fill-t4"));
        Assert.That(counting.Trades, Is.EqualTo(1));
        using var db = factory.Open();
        Assert.That(db.Exists<Trade>(trade => trade.ClientId == "cb-fill-t4"), Is.False);

        var reloaded = new CoinbaseSyncer(rig.Client, counting, clock, rig.Log.Sink, statePath);
        await reloaded.CycleAsync(CancellationToken.None);
        Assert.That(counting.Trades, Is.EqualTo(1));
    }

    [Test]
    public async Task Startup_backfills_fills_since_the_last_cursor()
    {
        SeedBooks();
        using var rig = Open();
        rig.Handler.Fills.Add(Eq("t5", "2026-11-12T16:00:00.000Z"));
        rig.Handler.EthdQty = 0.1m;
        await rig.Syncer.CycleAsync(CancellationToken.None);

        using var db = factory.Open();
        Assert.That(db.Select<Trade>(trade => trade.ClientId == "cb-fill-t1"), Has.Count.EqualTo(1));
        Assert.That(db.Exists<Trade>(trade => trade.ClientId == "cb-fill-t5"));
        Assert.That(Balances("coinbase-usdc").Cash, Is.EqualTo(96m));
    }

    [Test]
    public async Task Ledger_outage_sleeps_30_minutes_then_backfills()
    {
        SeedBooks();
        using var rig = Open();
        await rig.Syncer.CycleAsync(CancellationToken.None);
        var flaky = new FlakyGateway(rig.Gateway) { Down = true };
        var syncer = new CoinbaseSyncer(rig.Client, flaky, clock, rig.Log.Sink, statePath);
        var sleeps = new List<TimeSpan>();
        var status = new CoinbaseSyncStatus { Enabled = true };
        await CoinbaseSyncLoop.RunAsync(
            syncer,
            null,
            (delay, _) =>
            {
                sleeps.Add(delay);
                rig.Handler.Fills.Add(Eq("t5", "2026-11-12T16:00:00.000Z"));
                rig.Handler.EthdQty = 0.1m;
                flaky.Down = false;
                return Task.CompletedTask;
            },
            2,
            status,
            clock,
            TimeSpan.FromSeconds(60),
            rig.Log.Sink,
            CancellationToken.None);

        Assert.That(sleeps, Is.EqualTo(new[] { TimeSpan.FromMinutes(30) }));
        using var db = factory.Open();
        Assert.That(db.Exists<Trade>(trade => trade.ClientId == "cb-fill-t5"));
        Assert.That(db.Select<Trade>(trade => trade.ClientId.StartsWith("cb-fill-")), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Cash_gap_reconciles_on_the_third_cycle()
    {
        SeedCash(100m);
        using var rig = Open();
        rig.Handler.Fills.Clear();
        rig.Handler.Cash = 110m;
        await rig.Syncer.CycleAsync(CancellationToken.None);
        await rig.Syncer.CycleAsync(CancellationToken.None);
        Assert.That(ReconcileIds(), Is.Empty);

        await rig.Syncer.CycleAsync(CancellationToken.None);
        Assert.That(ReconcileIds(), Is.EqualTo(new[] { "cb-cash-reconcile-20261112T1500" }));
        Assert.That(Balances("coinbase-usdc").Cash, Is.EqualTo(110m));
        using var db = factory.Open();
        var row = db.Single<Transfer>(transfer => transfer.ClientId == "cb-cash-reconcile-20261112T1500");
        Assert.That(row.Kind, Is.EqualTo(TransferKind.Deposit));
        Assert.That(row.Amount, Is.EqualTo(10m));
        Assert.That(row.WalletId, Is.EqualTo(10));
        Assert.That(row.Note, Does.Contain("110.0000"));

        await rig.Syncer.CycleAsync(CancellationToken.None);
        Assert.That(ReconcileIds(), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Sub_cent_cash_gap_is_not_reconciled()
    {
        SeedCash(100m);
        using var rig = Open();
        rig.Handler.Fills.Clear();
        rig.Handler.Cash = 100.004m;
        await rig.Syncer.CycleAsync(CancellationToken.None);
        await rig.Syncer.CycleAsync(CancellationToken.None);
        await rig.Syncer.CycleAsync(CancellationToken.None);
        Assert.That(ReconcileIds(), Is.Empty);
        Assert.That(Balances("coinbase-usdc").Cash, Is.EqualTo(100m));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Sync_stops_when_the_key_can_trade_or_transfer(bool canTrade, bool canTransfer)
    {
        SeedBooks();
        using var rig = Open();
        rig.Handler.CanTrade = canTrade;
        rig.Handler.CanTransfer = canTransfer;
        rig.Handler.Fills.Add(Eq("t2", "2026-11-12T15:00:00.000Z"));
        rig.Handler.EthdQty = 0.1m;
        var status = new CoinbaseSyncStatus { Enabled = true };
        var sleeps = new List<TimeSpan>();
        await CoinbaseSyncLoop.RunAsync(
            rig.Syncer,
            token => CoinbaseSyncLoop.EnsureViewOnlyAsync(rig.Client, token),
            (delay, _) =>
            {
                sleeps.Add(delay);
                return Task.CompletedTask;
            },
            2,
            status,
            clock,
            TimeSpan.FromSeconds(60),
            rig.Log.Sink,
            CancellationToken.None);

        Assert.That(sleeps, Is.Empty);
        Assert.That(status.LastError, Does.Contain("view-only"));
        Assert.That(rig.Log.Errors, Does.Contain(status.LastError));
        using var db = factory.Open();
        Assert.That(db.Exists<Trade>(trade => trade.ClientId == "cb-fill-t2"), Is.False);
        Assert.That(rig.Handler.Calls.All(call => call.Path == "/api/v3/brokerage/key_permissions"));
    }

    [Test]
    public async Task View_only_key_still_records_a_fill()
    {
        SeedBooks();
        using var rig = Open();
        rig.Handler.Fills.Add(Eq("t2", "2026-11-12T15:00:00.000Z"));
        rig.Handler.EthdQty = 0.1m;
        var status = new CoinbaseSyncStatus { Enabled = true };
        await CoinbaseSyncLoop.RunAsync(
            rig.Syncer,
            token => CoinbaseSyncLoop.EnsureViewOnlyAsync(rig.Client, token),
            (_, _) => Task.CompletedTask,
            1,
            status,
            clock,
            TimeSpan.FromSeconds(60),
            rig.Log.Sink,
            CancellationToken.None);

        Assert.That(status.LastError, Is.Null);
        Assert.That(status.LastSyncUtc, Is.EqualTo(Now));
        using var db = factory.Open();
        Assert.That(db.Exists<Trade>(trade => trade.ClientId == "cb-fill-t2"));
    }

    [Test]
    public async Task Coinbase_down_backs_off_without_crashing()
    {
        using var rig = Open(retries: 1);
        rig.Handler.Down = true;
        var sleeps = new List<TimeSpan>();
        var cycles = await CoinbaseSyncLoop.RunAsync(
            rig.Syncer,
            null,
            (delay, _) =>
            {
                sleeps.Add(delay);
                return Task.CompletedTask;
            },
            3,
            new CoinbaseSyncStatus(),
            clock,
            TimeSpan.FromSeconds(60),
            rig.Log.Sink,
            CancellationToken.None);

        Assert.That(cycles, Is.EqualTo(3));
        Assert.That(sleeps, Is.EqualTo(new[] { TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240) }));
    }

    [Test]
    public async Task Existing_wallet_ids_are_reused_by_key()
    {
        CreateWallet("coinbase-usdc", "Cash already here");
        ForceId("coinbase-usdc", 10);
        CreateWallet("coinbase-btc", "BTC already here");
        ForceId("coinbase-btc", 14);
        var state = new CoinbaseSyncState { TickersLearned = true };
        state.CbrnTicker["cbrn-SBIT"] = "SBIT";
        state.CbrnTicker["cbrn-ETHD"] = "ETHD";
        CoinbaseSyncStateFile.Save(statePath, state);

        using var rig = Open();
        rig.Handler.Spots.Add(new SpotHolding("BTC", 0.01m, 500m));
        rig.Handler.Spots.Add(new SpotHolding("NUB", 2m, 20m));
        rig.Handler.Spots.Add(new SpotHolding("NBA", 3m, 15m));
        rig.Handler.EthdQty = 1m;
        await rig.Syncer.CycleAsync(CancellationToken.None);

        using var db = factory.Open();
        Assert.That(db.Single<Wallet>(wallet => wallet.Key == "coinbase-usdc").Id, Is.EqualTo(10));
        Assert.That(db.Single<Wallet>(wallet => wallet.Key == "coinbase-usdc").Name, Is.EqualTo("Cash already here"));
        Assert.That(db.Single<Wallet>(wallet => wallet.Key == "coinbase-btc").Id, Is.EqualTo(14));
        Assert.That(db.Single<Wallet>(wallet => wallet.Key == "coinbase-btc").Name, Is.EqualTo("BTC already here"));
        Assert.That(db.Select<Wallet>(wallet => wallet.Key == "coinbase-btc"), Has.Count.EqualTo(1));
        Assert.That(db.Single<Wallet>(wallet => wallet.Key == "coinbase-sbit").Name, Is.EqualTo("Coinbase SBIT"));
        Assert.That(db.Exists<Wallet>(wallet => wallet.Key == "coinbase-ethd" && wallet.Mode == WalletMode.Real));
        Assert.That(db.Exists<Wallet>(wallet => wallet.Key == "coinbase-nub"));
        Assert.That(db.Exists<Wallet>(wallet => wallet.Key == "coinbase-nba"));
        Assert.That(db.Single<Wallet>(wallet => wallet.Key == "coinbase-nub").Venue, Is.EqualTo("Coinbase"));
    }

    SyncRig Open(int retries = 4)
    {
        var handler = new FakeCoinbaseHandler();
        handler.Fills.Add(Eq("t1", "2026-11-12T14:11:05.293Z", "BUY", "39.8", "1", false, "SBIT"));
        var signer = CoinbaseSigner.Load("organizations/example/apiKeys/example", Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray()));
        var client = new CoinbaseBrokerageClient(signer, handler, retries, (_, _) => Task.CompletedTask);
        var log = new ListLog();
        var gateway = new CoinbaseLedgerGateway(factory, clock);
        var syncer = new CoinbaseSyncer(client, gateway, clock, log.Sink, statePath);
        return new SyncRig(handler, client, gateway, syncer, log);
    }

    void SeedBooks()
    {
        CreateWallet("coinbase-usdc", "Coinbase USDC");
        ForceId("coinbase-usdc", 10);
        CreateWallet("coinbase-sbit", "Coinbase SBIT");
        svc.Any(new RecordTransfer
        {
            Wallet = "coinbase-usdc",
            ClientId = "seed-usdc",
            Kind = TransferKind.Deposit,
            Amount = 100m,
            At = T1,
        });
        svc.Any(new RecordTransfer
        {
            Wallet = "coinbase-sbit",
            ClientId = "seed-sbit-cash",
            Kind = TransferKind.Deposit,
            Amount = 39.8m,
            At = T1,
        });
        svc.Any(new RecordTrade
        {
            Wallet = "coinbase-sbit",
            ClientId = "cb-fill-t1",
            Symbol = "SBIT-USD",
            Side = TradeSide.Buy,
            Qty = 1m,
            Price = 39.8m,
            Fee = 0m,
            Kind = TradeKind.Fill,
            ExecutedAt = T1,
            Title = "SBIT buy 1",
            Note = "seed",
        });
        svc.Any(new RecordPriceMark
        {
            Symbol = "SBIT-USD",
            Price = 39.8m,
            At = T1,
            Wallet = "coinbase-sbit",
            ClientId = "seed-mark-sbit",
            Source = "seed",
        });
    }

    void SeedCash(decimal amount)
    {
        CreateWallet("coinbase-usdc", "Coinbase USDC");
        ForceId("coinbase-usdc", 10);
        svc.Any(new RecordTransfer
        {
            Wallet = "coinbase-usdc",
            ClientId = "seed-usdc",
            Kind = TransferKind.Deposit,
            Amount = amount,
            At = T1,
        });
    }

    void CreateWallet(string key, string name) =>
        svc.Any(new CreateWallet
        {
            Key = key,
            Name = name,
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            BaseCurrency = "USD",
            Note = "seed",
        });

    void ForceId(string key, int id)
    {
        using var db = factory.Open();
        db.ExecuteSql("UPDATE Wallet SET Id = " + id + " WHERE Key = '" + key + "'");
    }

    WalletBalances Balances(string key) =>
        ((GetWalletBalancesResponse)svc.Any(new GetWalletBalances { Wallet = key })).Result;

    string[] MarkIds()
    {
        using var db = factory.Open();
        return db.Select<PriceMark>()
            .Where(mark => mark.ClientId != null && mark.ClientId.StartsWith("cb-mark-", StringComparison.Ordinal))
            .OrderBy(mark => mark.ClientId, StringComparer.Ordinal)
            .Select(mark => mark.ClientId!)
            .ToArray();
    }

    string[] ReconcileIds()
    {
        using var db = factory.Open();
        return db.Select<Transfer>()
            .Where(transfer => transfer.ClientId.StartsWith("cb-cash-reconcile-", StringComparison.Ordinal))
            .Select(transfer => transfer.ClientId)
            .ToArray();
    }

    static FakeFill Eq(string id, string at, string side = "BUY", string price = "40", string size = "4", bool inQuote = true, string ticker = "ETHD") =>
        new(id, at, side, price, size, inQuote, ticker);

    sealed class SyncRig(FakeCoinbaseHandler handler, CoinbaseBrokerageClient client, CoinbaseLedgerGateway gateway, CoinbaseSyncer syncer, ListLog log) : IDisposable
    {
        public FakeCoinbaseHandler Handler { get; } = handler;
        public CoinbaseBrokerageClient Client { get; } = client;
        public CoinbaseLedgerGateway Gateway { get; } = gateway;
        public CoinbaseSyncer Syncer { get; } = syncer;
        public ListLog Log { get; } = log;
        public void Dispose() => Client.Dispose();
    }

    sealed class ListLog
    {
        public List<string> Lines { get; } = [];
        public List<string> Errors { get; } = [];
        public CoinbaseSyncLog Sink { get; }

        public ListLog()
        {
            Sink = new CoinbaseSyncLog
            {
                InfoSink = Lines.Add,
                ErrorSink = message =>
                {
                    Errors.Add(message);
                    Lines.Add(message);
                },
            };
        }
    }

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }

    sealed record FakeFill(string Id, string At, string Side, string Price, string Size, bool InQuote, string Ticker);

    sealed record SpotHolding(string Asset, decimal Qty, decimal Fiat);

    sealed class CountingGateway(ILedgerGateway inner) : ILedgerGateway
    {
        public int Trades { get; private set; }

        public CoinbaseLedgerSnapshot Load() => inner.Load();

        public WalletBalances Balances(string wallet) => inner.Balances(wallet);

        public Wallet CreateWallet(string key, string name, string note) => inner.CreateWallet(key, name, note);

        public void RecordTrade(RecordTrade request)
        {
            Trades++;
            inner.RecordTrade(request);
        }

        public void RecordTransfer(RecordTransfer request) => inner.RecordTransfer(request);

        public void RecordMarks(IReadOnlyList<PriceMarkWrite> marks) => inner.RecordMarks(marks);
    }

    sealed class FlakyGateway(ILedgerGateway inner) : ILedgerGateway
    {
        public bool Down { get; set; }

        public CoinbaseLedgerSnapshot Load()
        {
            Guard();
            return inner.Load();
        }

        public WalletBalances Balances(string wallet)
        {
            Guard();
            return inner.Balances(wallet);
        }

        public Wallet CreateWallet(string key, string name, string note)
        {
            Guard();
            return inner.CreateWallet(key, name, note);
        }

        public void RecordTrade(RecordTrade request)
        {
            Guard();
            inner.RecordTrade(request);
        }

        public void RecordTransfer(RecordTransfer request)
        {
            Guard();
            inner.RecordTransfer(request);
        }

        public void RecordMarks(IReadOnlyList<PriceMarkWrite> marks)
        {
            Guard();
            inner.RecordMarks(marks);
        }

        void Guard()
        {
            if (Down)
                throw new LedgerUnavailableException("ConnectionError");
        }
    }

    sealed class FakeCoinbaseHandler : HttpMessageHandler
    {
        public List<(string Method, string Path)> Calls { get; } = [];
        public List<FakeFill> Fills { get; } = [];
        public List<SpotHolding> Spots { get; } = [];
        public bool Down { get; set; }
        public bool CanTrade { get; set; }
        public bool CanTransfer { get; set; }
        public decimal Cash { get; set; } = 100m;
        public decimal SbitFiat { get; set; } = 40m;
        public decimal EthdQty { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            Calls.Add((request.Method.Method, path));
            if (Down)
                throw new HttpRequestException("down");
            if (path == "/api/v3/brokerage/key_permissions")
                return Task.FromResult(Json(new { can_view = true, can_trade = CanTrade, can_transfer = CanTransfer }));
            if (path == "/api/v3/brokerage/accounts")
                return Task.FromResult(Json(new { accounts = Array.Empty<object>(), has_next = false }));
            if (path == "/api/v3/brokerage/portfolios")
                return Task.FromResult(Json(new { portfolios = new[] { new { uuid = "p1" } } }));
            if (path == "/api/v3/brokerage/portfolios/p1")
                return Task.FromResult(Json(new { breakdown = Breakdown() }));
            if (path == "/api/v3/brokerage/orders/historical/fills")
            {
                var start = Query(request.RequestUri, "start_sequence_timestamp");
                var rows = Fills.Where(fill => start == null || string.CompareOrdinal(fill.At, start) >= 0).Select(Dto);
                return Task.FromResult(Json(new { fills = rows, cursor = (string?)null }));
            }
            if (path.StartsWith("/api/v3/brokerage/products/", StringComparison.Ordinal))
                return Task.FromResult(Json(new { base_increment = "0.00001", base_currency_id = "ETHD" }));
            if (path.StartsWith("/api/v3/brokerage/market/products/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        object Breakdown()
        {
            var spots = new List<object>
            {
                new { asset = "USD", is_cash = true, total_balance_fiat = Cash },
            };
            foreach (var spot in Spots)
            {
                spots.Add(new
                {
                    asset = spot.Asset,
                    is_cash = false,
                    total_balance_crypto = new { value = spot.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    total_balance_fiat = new { value = spot.Fiat.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                });
            }
            var equity = new List<object>
            {
                new
                {
                    cbrn = "cbrn-SBIT",
                    total_balance_equity = new { value = "1" },
                    total_balance_fiat = new { value = SbitFiat.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                },
            };
            if (EthdQty > 0)
            {
                equity.Add(new
                {
                    cbrn = "cbrn-ETHD",
                    total_balance_equity = new { value = EthdQty.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    total_balance_fiat = new { value = (EthdQty * 40m).ToString(System.Globalization.CultureInfo.InvariantCulture) },
                });
            }
            return new
            {
                spot_positions = spots,
                equity_positions = equity,
                prediction_markets_positions = Array.Empty<object>(),
            };
        }

        static object Dto(FakeFill fill) => new
        {
            trade_id = fill.Id,
            order_id = "o-" + fill.Id,
            trade_time = fill.At,
            trade_type = "FILL",
            price = fill.Price,
            size = fill.Size,
            commission = "0",
            product_id = "hash-" + fill.Ticker,
            size_in_quote = fill.InQuote,
            side = fill.Side,
            product_type = "EQUITY",
            product_details = new { equity_details = new { base_cbrn = "cbrn-" + fill.Ticker, ticker = fill.Ticker } },
        };

        static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };

        static string? Query(Uri? uri, string key)
        {
            var text = uri?.Query;
            if (string.IsNullOrEmpty(text))
                return null;
            foreach (var part in text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = part.IndexOf('=');
                if (split < 0)
                    continue;
                if (Uri.UnescapeDataString(part[..split]) == key)
                    return Uri.UnescapeDataString(part[(split + 1)..]);
            }
            return null;
        }
    }
}

public class CoinbaseSyncClientTests
{
    [Test]
    public async Task Http_retries_wait_2_then_4_seconds()
    {
        var slept = new List<TimeSpan>();
        using var signer = CoinbaseSigner.Load("k", Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray()));
        using var client = new CoinbaseBrokerageClient(
            signer,
            new ThrowingHandler(),
            retries: 3,
            delay: (delay, _) =>
            {
                slept.Add(delay);
                return Task.CompletedTask;
            });
        try
        {
            await client.AccountsAsync(CancellationToken.None);
            Assert.Fail("expected CoinbaseDownException");
        }
        catch (CoinbaseDownException)
        {
        }
        Assert.That(slept, Is.EqualTo(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }));
    }

    [Test]
    public void Pem_and_ed25519_keys_sign()
    {
        using var ed = CoinbaseSigner.Load("k", Convert.ToBase64String(Enumerable.Repeat((byte)2, 64).ToArray()));
        var edJwt = ed.Create("GET", "/x");
        Assert.That(HeaderAlg(edJwt), Is.EqualTo("EdDSA"));
        Assert.That(Payload(edJwt).GetProperty("uri").GetString(), Is.EqualTo("GET api.coinbase.com/x"));
        Assert.That(Payload(edJwt).GetProperty("iss").GetString(), Is.EqualTo("cdp"));

        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ec.ExportECPrivateKeyPem().Replace("\n", "\\n", StringComparison.Ordinal);
        using var es = CoinbaseSigner.Load("k", pem);
        Assert.That(HeaderAlg(es.Create("GET", "/x")), Is.EqualTo("ES256"));
    }

    [Test]
    public async Task Get_only_handler_refuses_writes_and_order_placement()
    {
        var inner = new OkHandler();
        var guard = new CoinbaseGetOnlyHandler(inner);
        using var invoker = new HttpMessageInvoker(guard, disposeHandler: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://api.coinbase.com/api/v3/brokerage/orders"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.coinbase.com/api/v3/brokerage/orders"), CancellationToken.None));
        var allowed = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.coinbase.com/api/v3/brokerage/orders/historical/fills"),
            CancellationToken.None);
        Assert.That((int)allowed.StatusCode, Is.EqualTo(200));
        Assert.That(CoinbaseReadPaths.IsAllowed("/api/v3/brokerage/orders/batch_cancel"), Is.False);
        Assert.That(CoinbaseReadPaths.IsAllowed("/api/v3/brokerage/accounts"), Is.True);
    }

    static string HeaderAlg(string jwt)
    {
        using var header = JsonDocument.Parse(Decode(jwt.Split('.')[0]));
        return header.RootElement.GetProperty("alg").GetString() ?? "";
    }

    static JsonElement Payload(string jwt)
    {
        var document = JsonDocument.Parse(Decode(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    static byte[] Decode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("down");
    }

    sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
    }
}

public class CoinbaseCredentialTests
{
    [Test]
    public void Keyring_service_target_and_compound_target_both_resolve()
    {
        var store = new MemoryStore();
        store.Items[WinVaultCoinbaseCredentialSource.Service] = new WindowsCredentialRecord("api_private_key", "secret-value");
        store.Items["api_key_name@WalletSync-Coinbase"] = new WindowsCredentialRecord("api_key_name", "organizations/example/apiKeys/example");
        var source = new WinVaultCoinbaseCredentialSource(store);

        var credential = source.Read();

        Assert.That(credential, Is.Not.Null);
        Assert.That(credential!.PrivateKey, Is.EqualTo("secret-value"));
        Assert.That(credential.KeyName, Is.EqualTo("organizations/example/apiKeys/example"));
        Assert.That(credential.ToString(), Does.Not.Contain("secret-value"));
        Assert.That(store.Items[WinVaultCoinbaseCredentialSource.Service].ToString(), Does.Not.Contain("secret-value"));
    }

    [Test]
    public void Both_entries_stored_as_compound_names_resolve()
    {
        var store = new MemoryStore();
        store.Items["api_private_key@WalletSync-Coinbase"] = new WindowsCredentialRecord("api_private_key", "secret-value");
        store.Items["api_key_name@WalletSync-Coinbase"] = new WindowsCredentialRecord("api_key_name", "name-value");
        var credential = new WinVaultCoinbaseCredentialSource(store).Read();
        Assert.That(credential!.PrivateKey, Is.EqualTo("secret-value"));
        Assert.That(credential.KeyName, Is.EqualTo("name-value"));
    }

    [Test]
    public void Service_target_is_not_used_when_the_user_name_does_not_match()
    {
        var store = new MemoryStore();
        store.Items[WinVaultCoinbaseCredentialSource.Service] = new WindowsCredentialRecord("api_private_key", "secret-value");
        Assert.That(new WinVaultCoinbaseCredentialSource(store).Read(), Is.Null);
    }

    [Test]
    public void Env_private_key_is_ignored()
    {
        var previous = Environment.GetEnvironmentVariable("COINBASE_API_PRIVATE_KEY");
        Environment.SetEnvironmentVariable("COINBASE_API_PRIVATE_KEY", "from-env-should-not-be-used");
        try
        {
            Assert.That(new WinVaultCoinbaseCredentialSource(new MemoryStore()).Read(), Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COINBASE_API_PRIVATE_KEY", previous);
        }
    }

    [Test]
    public void Credential_blobs_decode_utf16_and_utf8()
    {
        var utf16 = Encoding.Unicode.GetBytes("hello-key");
        Assert.That(CredentialBlobText.Decode(utf16), Is.EqualTo("hello-key"));
        var bom = new byte[] { 0xFF, 0xFE }.Concat(utf16).ToArray();
        Assert.That(CredentialBlobText.Decode(bom), Is.EqualTo("hello-key"));
        Assert.That(CredentialBlobText.Decode(Encoding.UTF8.GetBytes("plain-key")), Is.EqualTo("plain-key"));
    }

    [Test]
    public void Windows_store_is_unavailable_off_windows()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore();
        var store = new WindowsCredentialManagerStore();
        Assert.That(store.IsSupported, Is.False);
        Assert.Throws<InvalidOperationException>(() => store.Read("WalletSync-Coinbase"));
    }

    [Test]
    public void Gate_stays_off_in_tests_and_off_windows_unless_a_fake_source_is_injected()
    {
        var windows = new WinVaultCoinbaseCredentialSource(new MemoryStore { IsSupported = true });
        var unsupported = new WinVaultCoinbaseCredentialSource(new MemoryStore { IsSupported = false });
        var fake = new FakeSource();
        Assert.That(CoinbaseSyncGate.ShouldRun(false, false, fake), Is.False);
        Assert.That(CoinbaseSyncGate.ShouldRun(true, true, windows), Is.False);
        Assert.That(CoinbaseSyncGate.ShouldRun(true, false, unsupported), Is.False);
        Assert.That(CoinbaseSyncGate.ShouldRun(true, true, fake), Is.True);
    }

    sealed class MemoryStore : IWindowsCredentialStore
    {
        public bool IsSupported { get; set; } = true;
        public Dictionary<string, WindowsCredentialRecord> Items { get; } = new(StringComparer.Ordinal);
        public WindowsCredentialRecord? Read(string target) => Items.TryGetValue(target, out var item) ? item : null;
    }

    sealed class FakeSource : ICoinbaseCredentialSource
    {
        public bool IsSupported => true;
        public CoinbaseApiCredential? Read() => new("name", "secret");
    }
}

public class CoinbaseSyncHealthTests
{
    [Test]
    public async Task Health_reports_sync_status_without_secrets()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "trademonitor-cbhealth-" + Guid.NewGuid().ToString("N") + ".db");
        var logDir = Path.Combine(Path.GetTempPath(), "trademonitor-cbhealth-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        using (var factory = new HealthApp(dbPath, logDir))
        using (var http = factory.CreateClient())
        {
            var response = await http.GetAsync("/api/health");
            var body = await response.Content.ReadAsStringAsync();
            Assert.That((int)response.StatusCode, Is.EqualTo(200), body);
            using var document = JsonDocument.Parse(body);
            var sync = document.RootElement.GetProperty("coinbaseSync");
            Assert.That(sync.GetProperty("enabled").GetBoolean(), Is.False);
            Assert.That(sync.GetProperty("lastSyncAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(sync.GetProperty("lastError").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body, Does.Not.Contain("api_private_key"));
            Assert.That(body, Does.Not.Contain("BEGIN"));
        }

        // The request log stores its monthly sqlite file in a process-wide named connection.
        // Drop it so the next host opens its own directory.
        foreach (var name in OrmLiteConnectionFactory.NamedConnections.Keys.Where(key => key.StartsWith("requests_", StringComparison.Ordinal)).ToList())
            OrmLiteConnectionFactory.NamedConnections.Remove(name);

        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var file = dbPath + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
        if (Directory.Exists(logDir))
            Directory.Delete(logDir, recursive: true);
    }

    sealed class HealthApp(string dbPath, string logDir) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=" + dbPath + ";Cache=Shared");
            builder.UseSetting("RequestLogs:Dir", logDir);
            builder.UseSetting(McpConfiguredKey.ConfigPath, "");
        }
    }
}
