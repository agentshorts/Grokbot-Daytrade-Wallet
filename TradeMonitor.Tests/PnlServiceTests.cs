using System.Diagnostics;
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
public class PnlServiceTests
{
    readonly string path = Path.Combine(Path.GetTempPath(), "trademonitor-pnl-" + Guid.NewGuid().ToString("N") + ".db");
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
        var migrator = new Migrator(factory, typeof(Migration1000).Assembly);
        var migrated = migrator.Run();
        Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
        clock = new FixedClock { UtcNow = new DateTime(2026, 6, 4, 16, 30, 0, DateTimeKind.Utc) };
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
        clock.UtcNow = new DateTime(2026, 6, 4, 16, 30, 0, DateTimeKind.Utc);
        using var db = factory.Open();
        db.DeleteAll<BalanceSnapshot>();
        db.DeleteAll<Position>();
        db.DeleteAll<PriceMark>();
        db.DeleteAll<Transfer>();
        db.DeleteAll<Trade>();
        db.DeleteAll<Wallet>();
    }

    [Test]
    public void Read_endpoints_match_the_worked_ledger()
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 4, 23, 59, 59, DateTimeKind.Utc);
        var paper = Create("paper", "Paper", WalletMode.Paper);
        var real = Create("real", "Real", WalletMode.Real);
        Move("paper", "dep", 200m, new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc));
        var buyA = Fill("paper", "alpha", TradeSide.Buy, 2m, 10m, 2m, new DateTime(2026, 6, 1, 15, 1, 0, DateTimeKind.Utc), title: "alpha", target: 18m);
        Fill("paper", "beta", TradeSide.Buy, 2m, 20m, 2m, new DateTime(2026, 6, 1, 15, 2, 0, DateTimeKind.Utc), title: "beta");
        ledger.Any(new RecordPriceMark
        {
            Symbol = "btc-usd",
            Price = 15m,
            At = new DateTime(2026, 6, 1, 15, 3, 0, DateTimeKind.Utc),
            ClientId = "mark-15",
        });
        var span = Fill("paper", "span", TradeSide.Sell, 3m, 20m, 3m, new DateTime(2026, 6, 2, 15, 0, 0, DateTimeKind.Utc));
        ledger.Any(new RecordPriceMark
        {
            Symbol = "BTC-USD",
            Price = 99m,
            At = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc),
            Wallet = "real",
            ClientId = "other-wallet",
        });
        Fill("paper", "flat", TradeSide.Sell, 1m, 21m, 0m, new DateTime(2026, 6, 3, 15, 0, 0, DateTimeKind.Utc));
        var sol = Fill("paper", "reopen", TradeSide.Buy, 2m, 5m, 1m, new DateTime(2026, 6, 4, 15, 0, 0, DateTimeKind.Utc),
            symbol: "SOL-USD", title: "reopen", note: "no mark", target: 9m);
        Move("real", "dep", 50m, new DateTime(2026, 6, 4, 16, 0, 0, DateTimeKind.Utc));
        var eth = Fill("real", "eth", TradeSide.Buy, 1m, 10m, 0m, new DateTime(2026, 6, 4, 16, 1, 0, DateTimeKind.Utc),
            symbol: "ETH-USD", title: "ether", target: 15m);
        ledger.Any(new RecordPriceMark
        {
            Symbol = "ETH-USD",
            Price = 12m,
            At = new DateTime(2026, 6, 4, 16, 20, 0, DateTimeKind.Utc),
            Wallet = "real",
            ClientId = "eth-mark",
        });

        var summary = (GetSummaryResponse)pnl.Any(new GetSummary { From = from, To = to });
        Assert.That(summary.Totals.TotalValue, Is.EqualTo(266m));
        Assert.That(summary.Totals.Cash, Is.EqualTo(243m));
        Assert.That(summary.Totals.OpenExposure, Is.EqualTo(12m));
        Assert.That(summary.Totals.UnmarkedCost, Is.EqualTo(11m));
        Assert.That(summary.Totals.PositionsValue, Is.EqualTo(23m));
        Assert.That(summary.Totals.RealizedPnl, Is.EqualTo(14m));
        Assert.That(summary.Totals.UnrealizedPnl, Is.Null);
        Assert.That(summary.Totals.Pnl, Is.Null);
        Assert.That(summary.Totals.Fees, Is.EqualTo(8m));
        Assert.That(summary.Totals.Deposits, Is.EqualTo(250m));
        Assert.That(summary.Totals.ValueChange, Is.EqualTo(266m));
        Assert.That(summary.Totals.TradeCount, Is.EqualTo(6));
        Assert.That(summary.Totals.Wins, Is.EqualTo(1));
        Assert.That(summary.Totals.Scratches, Is.EqualTo(1));
        Assert.That(summary.Totals.WinRate, Is.EqualTo(100m));
        Assert.That(summary.Totals.ValuedAtCost, Is.True);
        Assert.That(summary.Totals.OpenPositions, Is.EqualTo(2));
        Assert.That(summary.Wallets.Select(x => x.Key), Is.EqualTo(new[] { "paper", "real" }));

        var paperCard = summary.Wallets.Single(x => x.Key == "paper");
        var realCard = summary.Wallets.Single(x => x.Key == "real");
        Assert.That(paperCard.TotalValue, Is.EqualTo(214m));
        Assert.That(paperCard.Cash, Is.EqualTo(203m));
        Assert.That(paperCard.UnrealizedPnl, Is.Null);
        Assert.That(paperCard.Pnl, Is.Null);
        Assert.That(paperCard.RealizedPnl, Is.EqualTo(14m));
        Assert.That(paperCard.Fees, Is.EqualTo(8m));
        Assert.That(paperCard.UnmarkedCost, Is.EqualTo(11m));
        Assert.That(paperCard.OpenPositions, Is.EqualTo(1));
        Assert.That(paperCard.Sparkline[^1].TotalValue, Is.EqualTo(214m));
        Assert.That(realCard.TotalValue, Is.EqualTo(52m));
        Assert.That(realCard.Cash, Is.EqualTo(40m));
        Assert.That(realCard.UnrealizedPnl, Is.EqualTo(2m));
        Assert.That(realCard.Pnl, Is.EqualTo(2m));
        Assert.That(realCard.OpenExposure, Is.EqualTo(12m));
        Assert.That(realCard.ValuedAtCost, Is.False);
        Assert.That(realCard.LastActivityAt, Is.EqualTo(new DateTime(2026, 6, 4, 16, 20, 0, DateTimeKind.Utc)));

        Assert.That(summary.Modes.Single(x => x.Mode == "all").Totals.TotalValue, Is.EqualTo(266m));
        Assert.That(summary.Modes.Single(x => x.Mode == "paper").Totals.RealizedPnl, Is.EqualTo(14m));
        Assert.That(summary.Modes.Single(x => x.Mode == "paper").Totals.UnrealizedPnl, Is.Null);
        Assert.That(summary.Modes.Single(x => x.Mode == "real").Totals.Pnl, Is.EqualTo(2m));
        Assert.That(summary.Modes.Single(x => x.Mode == "real").Totals.TotalValue, Is.EqualTo(52m));

        var paperOnly = (GetSummaryResponse)pnl.Any(new GetSummary { From = from, To = to, Mode = "Paper" });
        Assert.That(paperOnly.Wallets.Select(x => x.Key), Is.EqualTo(new[] { "paper" }));
        Assert.That(paperOnly.Totals.TotalValue, Is.EqualTo(214m));
        Assert.That(paperOnly.Modes, Has.Count.EqualTo(3));

        var days = (PnlByDayResponse)pnl.Any(new PnlByDay { From = from, To = to });
        Assert.That(days.Results, Has.Count.EqualTo(4));
        Assert.That(days.Results[0].Pnl, Is.EqualTo(-4m));
        Assert.That(days.Results[0].Deposits, Is.EqualTo(200m));
        Assert.That(days.Results[1].Pnl, Is.EqualTo(12m));
        Assert.That(days.Results[1].RealizedPnl, Is.EqualTo(14m));
        Assert.That(days.Results[2].Pnl, Is.EqualTo(6m));
        Assert.That(days.Results[3].Pnl, Is.Null);
        Assert.That(days.Results[3].Fees, Is.EqualTo(1m));
        Assert.That(days.Results[3].Deposits, Is.EqualTo(50m));
        Assert.That(days.Results[3].TotalValue, Is.EqualTo(266m));
        Assert.That(days.Results[3].OpenExposure, Is.EqualTo(12m));
        Assert.That(days.Results[3].UnmarkedCost, Is.EqualTo(11m));
        var paperDays = days.Wallets.Single(x => x.Wallet == "paper").Days;
        Assert.That(paperDays[1].Pnl, Is.EqualTo(12m));
        Assert.That(paperDays[3].Pnl, Is.Null);
        Assert.That(paperDays[3].ValueChange, Is.EqualTo(0m));

        var equity = (GetEquityResponse)pnl.Any(new GetEquity { From = from, To = to });
        Assert.That(equity.Wallets, Has.Count.EqualTo(2));
        var markPoint = equity.Wallets.Single(x => x.Wallet == "paper").Points
            .Single(x => x.At == new DateTime(2026, 6, 1, 15, 3, 0, DateTimeKind.Utc));
        Assert.That(markPoint.TotalValue, Is.EqualTo(196m));
        Assert.That(markPoint.UnrealizedPnl, Is.EqualTo(-4m));
        Assert.That(equity.Total[^1].TotalValue, Is.EqualTo(266m));
        Assert.That(equity.Total[^1].UnrealizedPnl, Is.Null);
        Assert.That(equity.Total[0].At, Is.EqualTo(from));
        Assert.That(equity.Total[0].TotalValue, Is.EqualTo(0m));

        var symbols = (GetPnlBySymbolResponse)pnl.Any(new GetPnlBySymbol { From = from, To = to });
        Assert.That(symbols.Results.Select(x => x.Symbol), Is.EqualTo(new[] { "BTC-USD", "ETH-USD", "SOL-USD" }));
        Assert.That(symbols.Results[0].Pnl, Is.EqualTo(14m));
        Assert.That(symbols.Results[1].Pnl, Is.EqualTo(2m));
        Assert.That(symbols.Results[2].Pnl, Is.Null);
        Assert.That(symbols.Results[2].ValuedAtCost, Is.True);
        var top = (GetPnlBySymbolResponse)pnl.Any(new GetPnlBySymbol { From = from, To = to, Take = 1 });
        Assert.That(top.Results.Single().Symbol, Is.EqualTo("BTC-USD"));

        var fees = (GetFeesResponse)pnl.Any(new GetFees { From = from, To = to });
        Assert.That(fees.Total, Is.EqualTo(8m));
        Assert.That(fees.Days.Select(x => x.Fees), Is.EqualTo(new[] { 4m, 3m, 0m, 1m }));

        var positions = (QueryPositionsResponse)pnl.Any(new QueryPositions());
        Assert.That(positions.MarkStaleAfterMinutes, Is.EqualTo(15));
        var solRow = positions.Results.Single(x => x.Symbol == "SOL-USD");
        var ethRow = positions.Results.Single(x => x.Symbol == "ETH-USD");
        var btcRow = positions.Results.Single(x => x.Symbol == "BTC-USD");
        Assert.That(solRow.Status, Is.EqualTo("open"));
        Assert.That(solRow.Qty, Is.EqualTo(2m));
        Assert.That(solRow.AvgPrice, Is.EqualTo(5.5m));
        Assert.That(solRow.CostBasis, Is.EqualTo(11m));
        Assert.That(solRow.UnrealizedPnl, Is.Null);
        Assert.That(solRow.ValuedAtCost, Is.True);
        Assert.That(solRow.LastMarkPrice, Is.Null);
        Assert.That(solRow.MarkIsStale, Is.False);
        Assert.That(solRow.TargetPrice, Is.EqualTo(9m));
        Assert.That(solRow.DistanceToTarget, Is.Null);
        Assert.That(ethRow.UnrealizedPnl, Is.EqualTo(2m));
        Assert.That(ethRow.UnrealizedPnlPercent, Is.EqualTo(20m));
        Assert.That(ethRow.LastMarkPrice, Is.EqualTo(12m));
        Assert.That(ethRow.MarkIsStale, Is.False);
        Assert.That(ethRow.TargetPrice, Is.EqualTo(15m));
        Assert.That(ethRow.DistanceToTarget, Is.EqualTo(3m));
        Assert.That(ethRow.DistanceToTargetPercent, Is.EqualTo(25m));
        Assert.That(btcRow.Status, Is.EqualTo("closed"));
        Assert.That(btcRow.Qty, Is.EqualTo(0m));
        Assert.That(btcRow.RealizedPnl, Is.EqualTo(14m));
        Assert.That(btcRow.FeesPaid, Is.EqualTo(7m));
        Assert.That(btcRow.LastMarkPrice, Is.EqualTo(15m));
        Assert.That(btcRow.MarkIsStale, Is.True);
        Assert.That(((QueryPositionsResponse)pnl.Any(new QueryPositions { Status = "closed" })).Results.Select(x => x.Symbol),
            Is.EqualTo(new[] { "BTC-USD" }));

        var won = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = "paper", Outcome = "won" });
        Assert.That(won.Results.Select(x => x.ClientId), Is.EquivalentTo(new[] { "alpha", "span" }));
        var lost = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = "paper", Outcome = "lost" });
        Assert.That(lost.Results.Single().ClientId, Is.EqualTo("beta"));
        Assert.That(lost.Results[0].Pnl, Is.EqualTo(-2m));
        var open = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = "paper", Outcome = "open" });
        Assert.That(open.Results.Single().ClientId, Is.EqualTo("reopen"));
        Assert.That(open.Results[0].Pnl, Is.Null);
        Assert.That(open.Results[0].PriceKind, Is.EqualTo("unknown"));
        Assert.That(open.Results[0].CurrentOrExitPrice, Is.Null);
        Assert.That(open.Results[0].BuyPrice, Is.EqualTo(5m));
        Assert.That(open.Results[0].ValuedAtCost, Is.True);

        var spanRow = ((QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = "paper", Q = "span" })).Results.Single();
        Assert.That(spanRow.MatchedCost, Is.EqualTo(43m));
        Assert.That(spanRow.Proceeds, Is.EqualTo(57m));
        Assert.That(spanRow.Pnl, Is.EqualTo(14m));
        Assert.That(spanRow.BuyPrice, Is.EqualTo((2m * 10m + 1m * 20m) / 3m));
        Assert.That(spanRow.CurrentOrExitPrice, Is.EqualTo(20m));
        Assert.That(spanRow.PriceKind, Is.EqualTo("exit"));
        Assert.That(spanRow.Fee, Is.EqualTo(3m));
        Assert.That(spanRow.Mode, Is.EqualTo(WalletMode.Paper));

        var page = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = "paper", Take = 1, Skip = 1 });
        Assert.That(page.Total, Is.EqualTo(5));
        Assert.That(page.Results.Single().ClientId, Is.EqualTo("flat"));
        var byPnl = (QueryTradesResponse)ledger.Any(new QueryTrades { Wallet = "paper", OrderBy = "pnl" });
        Assert.That(byPnl.Results[0].ClientId, Is.EqualTo("alpha"));
        Assert.That(byPnl.Results[^1].ClientId, Is.EqualTo("reopen"));
        var realTrades = (QueryTradesResponse)ledger.Any(new QueryTrades { Mode = "real" });
        Assert.That(realTrades.Results.Single().ClientId, Is.EqualTo("eth"));
        Assert.That(realTrades.Results[0].CurrentOrExitPrice, Is.EqualTo(12m));
        Assert.That(realTrades.Results[0].PriceKind, Is.EqualTo("mark"));
        Assert.That(realTrades.Results[0].Pnl, Is.EqualTo(2m));

        var detail = (GetTradeResponse)pnl.Any(new GetTrade { Id = span.Result.Id });
        Assert.That(detail.Result.Pnl, Is.EqualTo(14m));
        Assert.That(detail.Fills, Has.Count.EqualTo(2));
        Assert.That(detail.Fills.Select(x => x.Cost), Is.EqualTo(new[] { 22m, 21m }));
        Assert.That(detail.Fills.Sum(x => x.RealizedPnl), Is.EqualTo(14m));
        Assert.That(detail.HasPriceMarks, Is.True);
        Assert.That(detail.PriceMarks.Select(x => x.Price), Is.EqualTo(new[] { 15m }));
        Assert.That(detail.Markers.Select(x => x.Side), Does.Contain(TradeSide.Buy));
        Assert.That(detail.Markers.Select(x => x.Side), Does.Contain(TradeSide.Sell));

        var bare = (GetTradeResponse)pnl.Any(new GetTrade { Id = sol.Result.Id });
        Assert.That(bare.HasPriceMarks, Is.False);
        Assert.That(bare.PriceMarks, Is.Empty);
        Assert.That(bare.Fills, Is.Empty);
        Assert.That(bare.Result.Pnl, Is.Null);
        Assert.That(bare.Result.Note, Is.EqualTo("no mark"));
        Assert.That(bare.Result.TargetPrice, Is.EqualTo(9m));

        Assert.That(buyA.Result.Id, Is.GreaterThan(0));
        Assert.That(eth.Result.Symbol, Is.EqualTo("ETH-USD"));
        Assert.That(paper.Result.Mode, Is.EqualTo(WalletMode.Paper));
        Assert.That(real.Result.Mode, Is.EqualTo(WalletMode.Real));

        using (var db = factory.Open())
        {
            var saved = db.Select<Position>().Select(Copy).ToList();
            var shots = db.Select<BalanceSnapshot>().Select(CopyShot).ToList();
            var row = db.Select<Position>().First(x => x.Symbol == "SOL-USD");
            row.Qty = 999m;
            db.Update(row);
            var realRow = db.Select<Position>().First(x => x.Symbol == "ETH-USD");
            realRow.RealizedPnl = 123m;
            db.Update(realRow);

            var partial = (RebuildProjectionsResponse)ledger.Any(new RebuildProjections { Wallet = "paper" });
            Assert.That(partial.Wallets, Is.EqualTo(1));
            Assert.That(db.Single<Position>(x => x.Symbol == "SOL-USD").Qty, Is.EqualTo(2m));
            Assert.That(db.Single<Position>(x => x.Symbol == "ETH-USD").RealizedPnl, Is.EqualTo(123m));

            var full = (RebuildProjectionsResponse)ledger.Any(new RebuildProjections());
            Assert.That(full.Wallets, Is.EqualTo(2));
            Assert.That(full.Positions, Is.EqualTo(saved.Count));
            Assert.That(db.Select<Position>().Select(Copy), Is.EquivalentTo(saved));
            Assert.That(db.Select<BalanceSnapshot>().Select(CopyShot), Is.EquivalentTo(shots));
        }

        var again = (GetSummaryResponse)pnl.Any(new GetSummary { From = from, To = to });
        Assert.That(again.Totals.TotalValue, Is.EqualTo(266m));
        Assert.That(again.Totals.RealizedPnl, Is.EqualTo(14m));
        Assert.That(again.Totals.UnrealizedPnl, Is.Null);
    }

    [Test]
    public void Thirty_days_for_five_wallets_returns_summary_and_equity_within_200ms()
    {
        var wallets = new List<Wallet>();
        for (var i = 0; i < 5; i++)
            wallets.Add(Create("w" + i, "Wallet " + i, i < 3 ? WalletMode.Paper : WalletMode.Real).Result);

        var start = new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        using (var db = factory.Open())
        {
            for (var day = 0; day < 30; day++)
            {
                var at = start.AddDays(day);
                foreach (var wallet in wallets)
                {
                    db.Insert(new Trade
                    {
                        WalletId = wallet.Id,
                        ClientId = wallet.Key + "-" + day + "-b",
                        Symbol = "BTC-USD",
                        Side = TradeSide.Buy,
                        Qty = 1m,
                        Price = 10m,
                        Fee = 0m,
                        ExecutedAt = at,
                        RecordedAt = at,
                    });
                    db.Insert(new Trade
                    {
                        WalletId = wallet.Id,
                        ClientId = wallet.Key + "-" + day + "-s",
                        Symbol = "BTC-USD",
                        Side = TradeSide.Sell,
                        Qty = 1m,
                        Price = 11m,
                        Fee = 0m,
                        ExecutedAt = at.AddMinutes(1),
                        RecordedAt = at,
                    });
                }

                db.Insert(new PriceMark
                {
                    Symbol = "BTC-USD",
                    Price = 11m,
                    At = at.AddMinutes(2),
                    RecordedAt = at,
                });
            }
        }

        var rebuilt = (RebuildProjectionsResponse)ledger.Any(new RebuildProjections());
        Assert.That(rebuilt.Wallets, Is.EqualTo(5));
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 30, 23, 59, 59, DateTimeKind.Utc);
        pnl.Any(new GetSummary { From = from, To = to });
        pnl.Any(new GetEquity { From = from, To = to });

        var summaryWatch = Stopwatch.StartNew();
        var summary = (GetSummaryResponse)pnl.Any(new GetSummary { From = from, To = to });
        summaryWatch.Stop();
        var equityWatch = Stopwatch.StartNew();
        var equity = (GetEquityResponse)pnl.Any(new GetEquity { From = from, To = to });
        equityWatch.Stop();

        Assert.That(summary.Wallets, Has.Count.EqualTo(5));
        Assert.That(summary.Modes.Select(x => x.Mode), Is.EqualTo(new[] { "all", "paper", "real" }));
        Assert.That(summary.Totals.RealizedPnl, Is.EqualTo(150m));
        Assert.That(equity.Wallets, Has.Count.EqualTo(5));
        Assert.That(equity.Total, Is.Not.Empty);
        Assert.That(summaryWatch.ElapsedMilliseconds, Is.LessThan(200), $"summary took {summaryWatch.ElapsedMilliseconds} ms");
        Assert.That(equityWatch.ElapsedMilliseconds, Is.LessThan(200), $"equity took {equityWatch.ElapsedMilliseconds} ms");
    }

    [Test]
    public void Rejects_bad_filters_unknown_rows_and_oversized_ranges()
    {
        Create("paper", "Paper", WalletMode.Paper);
        var missingWallet = Fails(() => pnl.Any(new GetSummary { Wallet = "missing" }));
        Assert.That(missingWallet.Status, Is.EqualTo(404));
        Assert.That(missingWallet.ErrorCode, Is.EqualTo("UnknownWallet"));
        var missingTrade = Fails(() => pnl.Any(new GetTrade { Id = 42 }));
        Assert.That(missingTrade.Status, Is.EqualTo(404));
        Assert.That(missingTrade.ErrorCode, Is.EqualTo("UnknownTrade"));
        Assert.That(Fails(() => pnl.Any(new PnlByDay { Mode = "both" })).ToString(), Does.Contain("Mode must be all, paper, or real."));
        Assert.That(Fails(() => pnl.Any(new GetEquity
        {
            From = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        })).ToString(), Does.Contain("From is after to."));
        Assert.That(Fails(() => pnl.Any(new PnlByDay
        {
            From = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        })).ToString(), Does.Contain("cannot exceed 10 years"));
        Assert.That(Fails(() => ledger.Any(new QueryTrades { Outcome = "maybe" })).ToString(), Does.Contain("Outcome"));
        Assert.That(Fails(() => ledger.Any(new QueryTrades { OrderBy = "magic" })).ToString(), Does.Contain("Order by"));
        Assert.That(Fails(() => pnl.Any(new QueryPositions { Status = "flat" })).ToString(), Does.Contain("Status"));
        Assert.That(Fails(() => ledger.Any(new RebuildProjections { Wallet = "missing" })).ErrorCode, Is.EqualTo("UnknownWallet"));
    }

    CreateWalletResponse Create(string key, string name, WalletMode mode) =>
        (CreateWalletResponse)ledger.Any(new CreateWallet { Key = key, Name = name, Mode = mode });

    RecordTransferResponse Move(string wallet, string clientId, decimal amount, DateTime at) =>
        (RecordTransferResponse)ledger.Any(new RecordTransfer
        {
            Wallet = wallet,
            ClientId = clientId,
            Kind = TransferKind.Deposit,
            Amount = amount,
            At = at,
        });

    RecordTradeResponse Fill(
        string wallet,
        string clientId,
        TradeSide side,
        decimal qty,
        decimal price,
        decimal fee,
        DateTime at,
        string symbol = "BTC-USD",
        string? title = null,
        string? note = null,
        decimal? target = null) =>
        (RecordTradeResponse)ledger.Any(new RecordTrade
        {
            Wallet = wallet,
            ClientId = clientId,
            Symbol = symbol,
            Side = side,
            Qty = qty,
            Price = price,
            Fee = fee,
            ExecutedAt = at,
            Title = title,
            Note = note,
            TargetPrice = target,
        });

    static string Copy(Position row) =>
        string.Join("|", row.WalletId, row.Symbol, row.Qty, row.CostBasis, row.AvgPrice, row.RealizedPnl, row.FeesPaid,
            row.LastMarkPrice, row.LastMarkAt, row.UpdatedAt);

    static string CopyShot(BalanceSnapshot row) =>
        string.Join("|", row.WalletId, row.At.ToString("O"), row.Cash, row.PositionsValue, row.TotalValue, row.Swept);

    static HttpError Fails(Action action)
    {
        var error = Assert.Throws<HttpError>(() => action());
        Assert.That(error, Is.Not.Null);
        return error!;
    }

    sealed class FixedClock : ILedgerClock
    {
        public DateTime UtcNow { get; set; }
    }
}
