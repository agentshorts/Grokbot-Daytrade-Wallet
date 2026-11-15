using System.Net;
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
public class LiveAcceptanceTests
{
    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-live-" + Guid.NewGuid().ToString("N") + ".db");
    OrmLiteConnectionFactory factory = null!;
    FixedClock clock = null!;
    LedgerServices ledger = null!;
    PnlServices pnl = null!;
    LiveMarketCache market = null!;
    PaperOrderBook orders = null!;
    LiveSimulationStore simulations = null!;
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
        pnl = new PnlServices(factory, clock);
        market = new LiveMarketCache();
        orders = new PaperOrderBook(factory, clock);
        simulations = new LiveSimulationStore(factory, clock);
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
    public void Paper_wallet_quick_sell_and_average_sell_agree_across_the_book()
    {
        var created = (CreatePaperWalletResponse)live.Any(new CreatePaperWallet { Name = "Lots Desk", StartingCash = 1000m });
        Assert.That(created.Result.Mode, Is.EqualTo(WalletMode.Paper));
        Assert.That(created.Result.Key, Is.EqualTo("lots-desk"));
        Assert.That(created.Cash, Is.EqualTo(1000m));
        var again = (CreatePaperWalletResponse)live.Any(new CreatePaperWallet { Name = "Lots Desk", StartingCash = 50m });
        Assert.That(again.Result.Key, Is.EqualTo("lots-desk-2"));

        var key = created.Result.Key;
        Buy(key, 10, 3);
        Buy(key, 4, 4);
        Buy(key, 2, 12);
        Tick(15);
        var book = Book(key);
        Assert.That(book.Lots.Select(x => (x.Qty, x.BuyPrice, x.UnrealizedPnl, x.UnrealizedPnlPercent)), Is.EqualTo(new[]
        {
            (10m, 3m, 120m, 400m),
            (4m, 4m, 44m, 275m),
            (2m, 12m, 6m, 25m),
        }));
        Assert.That(book.Average.OpenQty, Is.EqualTo(16m));
        Assert.That(book.Average.AvgCost, Is.EqualTo(4.375m));
        Assert.That(book.Average.Profit, Is.EqualTo(170m));
        Assert.That(book.Cash, Is.EqualTo(930m));

        var lot = book.Lots[0].TradeId;
        var sold = Place(key, TradeSide.Sell, LiveOrderType.Market, 10, null, CostingMethod.Lot, lot);
        Assert.That(sold.RealizedPnl, Is.EqualTo(120m));
        Assert.That(sold.Cash, Is.EqualTo(1080m));
        Assert.That(sold.Lots.Select(x => (x.Qty, x.BuyPrice, x.Cost)), Is.EqualTo(new[] { (4m, 4m, 16m), (2m, 12m, 24m) }));

        var averaged = Place(key, TradeSide.Sell, LiveOrderType.Market, 3, null, CostingMethod.Average, null);
        Assert.That(averaged.RealizedPnl, Is.EqualTo(145m));
        Assert.That(averaged.Lots.Select(x => (x.Qty, x.BuyPrice, x.Cost)), Is.EqualTo(new[] { (2m, 4m, 8m), (1m, 12m, 12m) }));
        var leftover = averaged.Lots.Single(x => x.BuyPrice == 4m);
        var closed = Place(key, TradeSide.Sell, LiveOrderType.Market, leftover.Qty, null, CostingMethod.Lot, leftover.TradeId);
        Assert.That(closed.RealizedPnl, Is.EqualTo(167m));
        Assert.That(closed.Lots.Select(x => (x.Qty, x.BuyPrice, x.Cost)), Is.EqualTo(new[] { (1m, 12m, 12m) }));
        Assert.That(closed.Cash, Is.EqualTo(1000m - 70m + 150m + 45m + 30m));

        var summary = (GetSummaryResponse)pnl.Any(new GetSummary { Wallet = key });
        var balances = ((GetWalletBalancesResponse)ledger.Any(new GetWalletBalances { Wallet = key })).Result;
        var trades = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = key, Take = 50 });
        var sells = trades.Results.Where(x => x.Side == TradeSide.Sell).Sum(x => x.RealizedPnl);
        var buys = trades.Results.Where(x => x.Side == TradeSide.Buy).Sum(x => x.RealizedPnl);
        Assert.That(summary.Totals.RealizedPnl, Is.EqualTo(167m));
        Assert.That(balances.RealizedPnl, Is.EqualTo(167m));
        Assert.That(balances.Positions.Single().RealizedPnl, Is.EqualTo(167m));
        Assert.That(sells, Is.EqualTo(167m));
        Assert.That(buys, Is.EqualTo(167m));
        Assert.That(trades.Results.Single(x => x.Qty == 10 && x.Side == TradeSide.Sell).Costing, Is.EqualTo(CostingMethod.Lot));
        Assert.That(trades.Results.Single(x => x.Qty == 3 && x.Side == TradeSide.Sell).Costing, Is.EqualTo(CostingMethod.Average));
    }

    [Test]
    public void Selling_all_at_average_realizes_170()
    {
        var key = Open("Average out");
        Buy(key, 10, 3);
        Buy(key, 4, 4);
        Buy(key, 2, 12);
        Tick(15);
        var quote = Book(key);
        Assert.That(quote.Average.Profit, Is.EqualTo(170m));
        var sold = Place(key, TradeSide.Sell, LiveOrderType.Market, 16, null, CostingMethod.Average, null);
        Assert.That(sold.RealizedPnl, Is.EqualTo(170m));
        Assert.That(sold.Lots, Is.Empty);
        Assert.That(sold.Average.OpenQty, Is.EqualTo(0m));
    }

    [Test]
    public void Resting_orders_fill_at_the_public_price_and_survive_a_new_book()
    {
        var key = Open("Resting");
        Buy(key, 10, 3);
        Buy(key, 4, 4);
        Buy(key, 2, 12);
        Tick(15);
        var lots = Book(key).Lots;
        var cheap = lots.Single(x => x.BuyPrice == 3m).TradeId;
        var mid = lots.Single(x => x.BuyPrice == 4m).TradeId;

        var rested = Place(key, TradeSide.Sell, LiveOrderType.Limit, 10, 20, CostingMethod.Lot, cheap);
        Assert.That(rested.Orders, Has.Count.EqualTo(1));
        Assert.That(rested.Orders[0].TriggerPrice, Is.EqualTo(20m));
        var orderId = rested.Orders[0].Id;

        var reopened = new PaperOrderBook(factory, clock).Read(key, "BTC-USD", 15, null);
        Assert.That(reopened.Orders.Single().Id, Is.EqualTo(orderId));

        Tick(20);
        var filled = (LiveBookResponse)live.Any(new GetLiveBook { Wallet = key, Symbol = "BTC-USD" });
        Assert.That(filled.Orders, Is.Empty);
        Assert.That(filled.RealizedPnl, Is.EqualTo(170m));
        Assert.That(filled.Lots.Select(x => x.BuyPrice), Is.EquivalentTo(new[] { 4m, 12m }));
        using (var db = factory.Open())
        {
            var row = db.SingleById<PaperOrder>(orderId);
            Assert.That(row.Status, Is.EqualTo(LiveOrderStatus.Filled));
            Assert.That(row.FilledPrice, Is.EqualTo(20m));
        }

        Tick(15);
        var stop = Place(key, TradeSide.Sell, LiveOrderType.StopLoss, 4, 10, CostingMethod.Lot, mid);
        Assert.That(stop.Orders, Has.Count.EqualTo(1));
        Tick(9);
        var stopped = Book(key);
        Assert.That(stopped.Orders, Is.Empty);
        Assert.That(stopped.RealizedPnl, Is.EqualTo(190m));
        Assert.That(stopped.Lots.Select(x => x.BuyPrice), Is.EqualTo(new[] { 12m }));
    }

    [Test]
    public void Take_profit_average_fills_when_price_crosses_and_a_cancel_does_not()
    {
        var key = Open("Targets");
        Buy(key, 10, 3);
        Buy(key, 4, 4);
        Buy(key, 2, 12);
        Tick(12);
        var armed = Place(key, TradeSide.Sell, LiveOrderType.TakeProfit, 16, 15, CostingMethod.Average, null);
        Assert.That(armed.Orders, Has.Count.EqualTo(1));
        Assert.That(armed.RealizedPnl, Is.EqualTo(0m));
        Tick(15);
        var filled = Book(key);
        Assert.That(filled.Orders, Is.Empty);
        Assert.That(filled.RealizedPnl, Is.EqualTo(170m));
        Assert.That(filled.Lots, Is.Empty);

        var other = Open("Cancel me");
        Buy(other, 1, 3);
        Tick(15);
        var stop = Place(other, TradeSide.Sell, LiveOrderType.StopLoss, 1, 10, CostingMethod.Fifo, null);
        var cancelled = (LiveBookResponse)live.Any(new CancelPaperOrder { Id = stop.Orders.Single().Id });
        Assert.That(cancelled.Orders, Is.Empty);
        Tick(9);
        var after = Book(other);
        Assert.That(after.RealizedPnl, Is.EqualTo(0m));
        Assert.That(after.Lots, Has.Count.EqualTo(1));
        using var db = factory.Open();
        Assert.That(db.Count<Trade>(x => x.Side == TradeSide.Sell), Is.EqualTo(1));
    }

    [Test]
    public void Market_sell_uses_fifo_and_cash_rejects_leave_no_order()
    {
        var key = Open("Fifo");
        Buy(key, 1, 3);
        Buy(key, 1, 4);
        Tick(10);
        var sold = Place(key, TradeSide.Sell, LiveOrderType.Market, 1, null, CostingMethod.Fifo, null);
        Assert.That(sold.RealizedPnl, Is.EqualTo(7m));
        Assert.That(sold.Lots.Single().BuyPrice, Is.EqualTo(4m));

        var thin = Open("Thin", 10m);
        Tick(3);
        var error = Assert.Throws<HttpError>(() => Place(thin, TradeSide.Buy, LiveOrderType.Market, 10, null, CostingMethod.Fifo, null));
        Assert.That(error!.ErrorCode, Is.EqualTo("InsufficientCash"));
        using var db = factory.Open();
        Assert.That(db.Count<Trade>(x => x.Symbol == "BTC-USD" && x.Side == TradeSide.Buy && x.Qty == 10), Is.EqualTo(0));
        Assert.That(db.Count<PaperOrder>(x => x.Status == LiveOrderStatus.Open), Is.EqualTo(0));
    }

    [Test]
    public void Real_wallets_reject_writes_and_simulation_persists_nothing()
    {
        var real = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = "coinbase",
            Name = "Coinbase",
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            BaseCurrency = "USD",
        });
        ledger.Any(new RecordTransfer { Wallet = real.Result.Key, ClientId = "seed", Kind = TransferKind.Deposit, Amount = 100m });
        Tick(10);
        var rejected = Assert.Throws<HttpError>(() => Place(real.Result.Key, TradeSide.Buy, LiveOrderType.Market, 1, null, CostingMethod.Fifo, null));
        Assert.That(rejected!.ErrorCode, Is.EqualTo("RealWallet"));

        var opened = (LiveBookResponse)live.Any(new OpenLiveSimulation { Wallet = real.Result.Key, Symbol = "BTC-USD" });
        Assert.That(opened.Simulated, Is.True);
        Assert.That(opened.Cash, Is.EqualTo(100m));
        var bought = (LiveBookResponse)live.Any(new PlaceSimulationOrder
        {
            Id = opened.SimulationId,
            Symbol = "BTC-USD",
            Side = TradeSide.Buy,
            Type = LiveOrderType.Market,
            Qty = 1,
        });
        Assert.That(bought.Cash, Is.EqualTo(90m));
        Assert.That(bought.Simulated, Is.True);
        var reset = (LiveBookResponse)live.Any(new ResetLiveSimulation { Id = opened.SimulationId, Symbol = "BTC-USD" });
        Assert.That(reset.Cash, Is.EqualTo(100m));
        Assert.That(reset.Lots, Is.Empty);

        var paper = Assert.Throws<HttpError>(() => live.Any(new OpenLiveSimulation { Wallet = Open("Nope"), Symbol = "BTC-USD" }));
        Assert.That(paper!.ErrorCode, Is.EqualTo("PaperWallet"));

        using var db = factory.Open();
        Assert.That(db.Count<Trade>(), Is.EqualTo(0));
        Assert.That(db.Count<PaperOrder>(), Is.EqualTo(0));
        Assert.That(db.Count<Transfer>(x => x.ClientId != "seed" && x.ClientId != "open:nope"), Is.EqualTo(0));
    }

    [Test]
    public void Public_payloads_parse_without_a_network_call()
    {
        var products = CoinbaseMessages.Products("""
            [
              {"id":"BTC-USD","base_currency":"BTC","quote_currency":"USD","status":"online","display_name":"BTC/USD"},
              {"id":"BTC-EUR","base_currency":"BTC","quote_currency":"EUR","status":"online"},
              {"product_id":"ETH-USD","base_currency_id":"ETH","quote_currency_id":"USD","product_type":"SPOT","base_name":"Ether"},
              {"id":"SOL-USD","base_currency":"SOL","quote_currency":"USD","trading_disabled":true}
            ]
            """);
        Assert.That(products.Select(x => x.ProductId), Is.EqualTo(new[] { "BTC-USD", "ETH-USD" }));

        var advanced = CoinbaseMessages.Ticks("""
            {"channel":"ticker","timestamp":"2026-06-01T15:00:00Z","events":[{"tickers":[{"product_id":"BTC-USD","price":"15.25"}]}]}
            """);
        Assert.That(advanced.Single().Price, Is.EqualTo(15.25m));
        var exchange = CoinbaseMessages.Ticks("""{"type":"ticker","product_id":"ETH-USD","price":"4.375","time":"2026-06-01T15:00:01Z"}""");
        Assert.That(exchange.Single().Price, Is.EqualTo(4.375m));

        var candles = CoinbaseMessages.Candles("""[[1717248000,"10","12","11","11.5","3"],[1717247700,"9","11","10","10.5","2"]]""");
        Assert.That(candles.Select(x => x.Close), Is.EqualTo(new[] { 10.5m, 11.5m }));
        Assert.That(CoinbaseMessages.GranularityKey("15m"), Is.EqualTo("15m"));

        var handler = new StubHandler("""[{"id":"SOL-USD","base_currency":"SOL","quote_currency":"USD","status":"online"}]""");
        var client = new CoinbasePublicClient(new HttpClient(handler));
        var body = client.GetProducts(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(handler.LastUrl, Is.EqualTo("https://api.exchange.coinbase.com/products"));
        Assert.That(handler.UserAgent, Does.Contain("TradeMonitor"));
        Assert.That(CoinbaseMessages.Products(body).Single().ProductId, Is.EqualTo("SOL-USD"));
    }

    [Test]
    public void Chart_marks_include_saved_fills_and_flag_simulation_fills()
    {
        var key = Open("Marks");
        ledger.Any(new RecordTrade
        {
            Wallet = key,
            ClientId = "buy-fee",
            Symbol = "BTC-USD",
            Side = TradeSide.Buy,
            Qty = 2,
            Price = 10,
            Fee = 0.25m,
            ExecutedAt = clock.UtcNow,
        });
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Tick(12);
        Place(key, TradeSide.Sell, LiveOrderType.Market, 1, null, CostingMethod.Fifo, null);
        var marks = (GetLiveTradesResponse)live.Any(new GetLiveTrades { Wallet = key, Symbol = "BTC-USD" });
        Assert.That(marks.Results.Select(x => (x.Side, x.Qty, x.Price, x.Fee, x.Kind, x.Simulated)), Is.EqualTo(new[]
        {
            (TradeSide.Buy, 2m, 10m, 0.25m, TradeKind.Fill, false),
            (TradeSide.Sell, 1m, 12m, 0m, TradeKind.Fill, false),
        }));

        var real = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = "marked",
            Name = "Marked",
            Mode = WalletMode.Real,
            BaseCurrency = "USD",
        });
        ledger.Any(new RecordTransfer { Wallet = real.Result.Key, ClientId = "cash", Kind = TransferKind.Deposit, Amount = 100m });
        ledger.Any(new RecordTrade
        {
            Wallet = real.Result.Key,
            ClientId = "saved",
            Symbol = "ETH-USD",
            Side = TradeSide.Buy,
            Qty = 1,
            Price = 5,
            Fee = 0.1m,
            ExecutedAt = clock.UtcNow,
        });
        var opened = (LiveBookResponse)live.Any(new OpenLiveSimulation { Wallet = real.Result.Key, Symbol = "ETH-USD" });
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        market.PublishTick(new LiveTick { ProductId = "ETH-USD", Price = 6, At = clock.UtcNow });
        live.Any(new PlaceSimulationOrder
        {
            Id = opened.SimulationId,
            Symbol = "ETH-USD",
            Side = TradeSide.Buy,
            Type = LiveOrderType.Market,
            Qty = 1,
        });
        var sim = (GetLiveTradesResponse)live.Any(new GetLiveTrades { SimulationId = opened.SimulationId, Symbol = "ETH-USD" });
        Assert.That(sim.Results.Select(x => (x.Side, x.Price, x.Fee, x.Simulated)), Is.EqualTo(new[]
        {
            (TradeSide.Buy, 5m, 0.1m, false),
            (TradeSide.Buy, 6m, 0m, true),
        }));
        using var db = factory.Open();
        Assert.That(db.Count<Trade>(x => x.Symbol == "ETH-USD"), Is.EqualTo(1));
    }

    string Open(string name, decimal cash = 1000m) =>
        ((CreatePaperWalletResponse)live.Any(new CreatePaperWallet { Name = name, StartingCash = cash })).Result.Key;

    void Tick(decimal price) =>
        market.PublishTick(new LiveTick { ProductId = "BTC-USD", Price = price, At = clock.UtcNow });

    void Buy(string wallet, decimal qty, decimal price)
    {
        Tick(price);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Place(wallet, TradeSide.Buy, LiveOrderType.Market, qty, null, CostingMethod.Fifo, null);
    }

    LiveBookResponse Place(string wallet, TradeSide side, LiveOrderType type, decimal qty, decimal? trigger, CostingMethod costing, int? lotId) =>
        (LiveBookResponse)live.Any(new PlacePaperOrder
        {
            Wallet = wallet,
            Symbol = "btc-usd",
            Side = side,
            Type = type,
            Qty = qty,
            TriggerPrice = trigger,
            Costing = costing,
            LotId = lotId,
        });

    LiveBookResponse Book(string wallet) =>
        (LiveBookResponse)live.Any(new GetLiveBook { Wallet = wallet, Symbol = "BTC-USD" });

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }

    sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
