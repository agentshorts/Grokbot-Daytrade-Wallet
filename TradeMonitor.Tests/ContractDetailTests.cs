using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using ServiceStack;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

public class ContractDetailTests
{
    const string Ticker = "KXBTCD-26NOV1617-T64999.99";

    [Test]
    public void Contract_read_is_an_admin_get()
    {
        var route = typeof(GetPredictionContract).GetCustomAttributes<RouteAttribute>().Single();
        Assert.That(route.Path, Is.EqualTo("/api/predictions/contract"));
        Assert.That(route.Verbs, Is.EqualTo("GET"));
        Assert.That(typeof(GetPredictionContract).GetCustomAttributes<ValidateIsAdminAttribute>(), Is.Not.Empty);
    }

    [TestCase("22,40,30,45,35", "5", 3)]
    [TestCase("22,40,28,39,27", "5", 3)]
    [TestCase("22,30,40", "5", 0)]
    [TestCase("10,15,10", "5", 1)]
    [TestCase("40,22,35", "5", 1)]
    public void Reversal_marks_land_on_the_extreme_and_match_the_count(string prices, string threshold, int expected)
    {
        var series = prices.Split(',').Select(item => decimal.Parse(item, CultureInfo.InvariantCulture)).ToList();
        var size = decimal.Parse(threshold, CultureInfo.InvariantCulture);
        var turns = OscillationMath.Turns(series, size);
        Assert.That(turns, Has.Count.EqualTo(expected));
        Assert.That(OscillationMath.CountReversals(series, size), Is.EqualTo(expected));
        foreach (var turn in turns)
            Assert.That(series[turn.Index], Is.EqualTo(turn.Price));
    }

    [Test]
    public void Eastern_close_uses_daylight_time_in_the_summer()
    {
        Assert.That(EasternTime.Format(new DateTime(2026, 7, 16, 21, 0, 0, DateTimeKind.Utc)), Is.EqualTo("5:00 PM EDT, Jul 16"));
        Assert.That(EasternTime.Format(null), Is.Null);
    }

    [Test]
    public void Known_zigzag_marks_the_peak_and_the_trough()
    {
        var turns = OscillationMath.Turns([22, 40, 30, 45, 35], 5);
        Assert.That(turns.Select(turn => (turn.Index, turn.Price, turn.Extreme)), Is.EqualTo(new[]
        {
            (1, 40m, "high"),
            (2, 30m, "low"),
            (3, 45m, "high"),
        }));
    }

    [Test]
    public void Kalshi_market_reads_both_sides_volume_and_the_strike()
    {
        const string json = """
            {"market":{
              "title":"Bitcoin price today at 5pm EST?",
              "subtitle":"$64,999.99 or above",
              "status":"active",
              "yes_bid":54,"yes_ask":55,"no_bid":45,"no_ask":46,
              "last_price_dollars":"0.5600",
              "volume_fp":"128.00",
              "open_interest":40,
              "floor_strike":64999.99,
              "close_time":"2026-11-16T22:00:00Z"
            }}
            """;
        var book = PredictionBars.KalshiMarket(json);
        Assert.That(book.YesBid, Is.EqualTo(0.54m));
        Assert.That(book.YesAsk, Is.EqualTo(0.55m));
        Assert.That(book.NoBid, Is.EqualTo(0.45m));
        Assert.That(book.NoAsk, Is.EqualTo(0.46m));
        Assert.That(book.Last, Is.EqualTo(0.56m));
        Assert.That(book.Volume, Is.EqualTo(128m));
        Assert.That(book.OpenInterest, Is.EqualTo(40m));
        Assert.That(book.Title, Is.EqualTo("Bitcoin price today at 5pm EST?"));
        Assert.That(book.Subtitle, Is.EqualTo("$64,999.99 or above"));
        Assert.That(book.Status, Is.EqualTo("active"));
        Assert.That(book.Strike, Is.EqualTo(64999.99m));
    }

    [Test]
    public void Broken_market_json_is_an_empty_book()
    {
        Assert.That(PredictionBars.KalshiMarket("not-json"), Is.EqualTo(MarketBook.Empty));
    }

    [Test]
    public void Yes_uses_the_book_and_no_is_implied_when_the_source_has_only_yes()
    {
        var quote = new PredictionQuote { Price = 0.56m, Last = 0.56m, NoImplied = true };
        var yes = SideQuotes.Yes(quote);
        var no = SideQuotes.No(quote);
        Assert.That(yes.Cents, Is.EqualTo(56m));
        Assert.That(yes.Label, Is.EqualTo("last"));
        Assert.That(no.Cents, Is.EqualTo(44m));
        Assert.That(no.Label, Is.EqualTo("implied"));
        Assert.That(no.Implied, Is.True);
    }

    [Test]
    public void Both_sides_of_the_book_are_labeled_bid_ask()
    {
        var quote = new PredictionQuote
        {
            YesBid = 0.54m,
            YesAsk = 0.55m,
            NoBid = 0.45m,
            NoAsk = 0.46m,
            Last = 0.56m,
            Price = 0.56m,
        };
        var yes = SideQuotes.Yes(quote);
        var no = SideQuotes.No(quote);
        Assert.That(yes.Label, Is.EqualTo("bid/ask"));
        Assert.That(yes.BidCents, Is.EqualTo(54m));
        Assert.That(yes.AskCents, Is.EqualTo(55m));
        Assert.That(yes.Cents, Is.EqualTo(56m));
        Assert.That(no.Label, Is.EqualTo("bid/ask"));
        Assert.That(no.BidCents, Is.EqualTo(45m));
        Assert.That(no.AskCents, Is.EqualTo(46m));
        Assert.That(no.Implied, Is.False);
    }

    [Test]
    public void Detail_marks_reversals_high_and_low_and_an_open_holding()
    {
        var mapped = OscillationTickers.Map(Ticker + "-KALSHI")!.Value;
        var quote = new PredictionQuote
        {
            Source = "kalshi",
            Price = 0.35m,
            Last = 0.35m,
            YesBid = 0.34m,
            YesAsk = 0.36m,
            NoBid = 0.64m,
            NoAsk = 0.66m,
            Volume = 12m,
            OpenInterest = 4m,
            Title = "Bitcoin price today?",
            Subtitle = "$64,999.99 or above",
            Status = "active",
            Strike = 64999.99m,
            CloseAt = new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc),
            Bars =
            [
                new PredictionBar { At = 100, Close = 0.22m, High = 0.22m, Low = 0.22m },
                new PredictionBar { At = 160, Close = 0.40m, High = 0.40m, Low = 0.22m },
                new PredictionBar { At = 220, Close = 0.30m, High = 0.40m, Low = 0.30m },
                new PredictionBar { At = 280, Close = 0.45m, High = 0.45m, Low = 0.30m },
                new PredictionBar { At = 340, Close = 0.35m, High = 0.45m, Low = 0.35m },
            ],
        };
        var held = Book(
            "pm-" + mapped.ProductId,
            "BTC 65k",
            false,
            (mapped.ProductId, 4m, 1.60m),
            new Trade
            {
                Id = 7,
                Symbol = mapped.ProductId,
                Side = TradeSide.Buy,
                Kind = TradeKind.Fill,
                Qty = 4m,
                Price = 0.40m,
                ExecutedAt = DateTimeOffset.FromUnixTimeSeconds(160).UtcDateTime,
            });
        var archived = Book("pm-" + mapped.ProductId + "-old", "old", true, (mapped.ProductId, 2m, 1m));
        var other = Book("coinbase-btc", "spot", false, ("BTC-USD", 1m, 10m));
        var from = DateTimeOffset.FromUnixTimeSeconds(100).UtcDateTime;
        var to = DateTimeOffset.FromUnixTimeSeconds(400).UtcDateTime;

        var detail = ContractReport.Build(mapped, quote, [held, archived, other], 6, 5, false, from, to, null);

        Assert.That(detail.Title, Is.EqualTo("Bitcoin price today?"));
        Assert.That(detail.Subtitle, Is.EqualTo("$64,999.99 or above"));
        Assert.That(detail.Underlying, Is.EqualTo("BTC"));
        Assert.That(detail.Strike, Is.EqualTo(64999.99m));
        Assert.That(detail.Direction, Is.EqualTo("above"));
        Assert.That(detail.Status, Is.EqualTo("active"));
        Assert.That(detail.CloseCentral, Is.EqualTo("4:00 PM CST, Nov 16"));
        Assert.That(detail.CloseEastern, Is.EqualTo("5:00 PM EST, Nov 16"));
        Assert.That(detail.YesLabel, Is.EqualTo("bid/ask"));
        Assert.That(detail.NoLabel, Is.EqualTo("bid/ask"));
        Assert.That(detail.NoImplied, Is.False);
        Assert.That(detail.NoLineImplied, Is.True);
        Assert.That(detail.Reversals, Is.EqualTo(3));
        Assert.That(detail.HighCents, Is.EqualTo(45m));
        Assert.That(detail.LowCents, Is.EqualTo(22m));
        Assert.That(detail.RangeCents, Is.EqualTo(23m));
        Assert.That(detail.Volume, Is.EqualTo(12m));
        Assert.That(detail.OpenInterest, Is.EqualTo(4m));
        Assert.That(detail.Points.Select(point => point.YesCents), Is.EqualTo(new[] { 22m, 40m, 30m, 45m, 35m }));
        Assert.That(detail.Points.Select(point => point.NoCents), Is.EqualTo(new[] { 78m, 60m, 70m, 55m, 65m }));
        Assert.That(detail.Marks.Select(mark => mark.Text), Is.EquivalentTo(new[] { "High", "Low", "Reversal", "Reversal", "Buy 4 @ 40c" }));
        Assert.That(detail.Marks.Single(mark => mark.Text == "High").Kind, Is.EqualTo("high"));
        Assert.That(detail.Marks.Single(mark => mark.Text == "Low").Kind, Is.EqualTo("low"));
        Assert.That(detail.Holdings, Has.Count.EqualTo(1));
        Assert.That(detail.Holdings[0].Wallet, Is.EqualTo("pm-" + mapped.ProductId));
        Assert.That(detail.Holdings[0].Name, Is.EqualTo("BTC 65k"));
        Assert.That(detail.Holdings[0].Side, Is.EqualTo("YES"));
        Assert.That(detail.Holdings[0].Qty, Is.EqualTo(4m));
        Assert.That(detail.Holdings[0].AvgCost, Is.EqualTo(0.40m));
        Assert.That(detail.Holdings[0].UnrealizedPnl, Is.EqualTo(4m * 0.35m - 1.60m));
        Assert.That(detail.Holdings[0].Fills.Select(fill => fill.Side), Is.EqualTo(new[] { "buy" }));
    }

    [Test]
    public void Ad_hoc_ticker_has_no_holding_and_implies_no_from_yes()
    {
        var mapped = OscillationTickers.Map("KXETHD-26NOV1617-B2500.5")!.Value;
        var quote = new PredictionQuote
        {
            Source = "coinbase",
            Price = 0.20m,
            Last = 0.20m,
            NoImplied = true,
            Bars = [new PredictionBar { At = 10, Close = 0.20m, High = 0.20m, Low = 0.20m }],
        };
        var detail = ContractReport.Build(
            mapped,
            quote,
            [],
            168,
            5,
            true,
            DateTime.UnixEpoch,
            new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc));

        Assert.That(detail.Holdings, Is.Empty);
        Assert.That(detail.Underlying, Is.EqualTo("ETH"));
        Assert.That(detail.Direction, Is.EqualTo("below"));
        Assert.That(detail.Strike, Is.EqualTo(2500.5m));
        Assert.That(detail.Title, Is.EqualTo("ETH below $2,500.50"));
        Assert.That(detail.NoLabel, Is.EqualTo("implied"));
        Assert.That(detail.NoCents, Is.EqualTo(80m));
        Assert.That(detail.AllAvailable, Is.True);
        Assert.That(detail.LookbackHours, Is.EqualTo(168m));
        Assert.That(detail.CloseCentral, Is.EqualTo("4:00 PM CST, Nov 16"));
        Assert.That(detail.Status, Is.EqualTo("closed"));
    }

    [Test]
    public void A_fill_outside_the_window_stays_on_the_holding_and_off_the_chart()
    {
        var mapped = OscillationTickers.Map(Ticker)!.Value;
        var book = Book(
            "pm-" + mapped.ProductId,
            "BTC",
            false,
            (mapped.Ticker, 1m, 0.25m),
            new Trade
            {
                Id = 1,
                Symbol = mapped.Ticker,
                Side = TradeSide.Buy,
                Kind = TradeKind.Fill,
                Qty = 1m,
                Price = 0.25m,
                ExecutedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new Trade
            {
                Id = 2,
                Symbol = mapped.Ticker,
                Side = TradeSide.Sell,
                Kind = TradeKind.Settlement,
                Qty = 1m,
                Price = 0m,
                ExecutedAt = new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc),
            });
        var detail = ContractReport.Build(
            mapped,
            new PredictionQuote { Price = 0.30m, Last = 0.30m, Bars = [new PredictionBar { At = 100, Close = 0.30m, High = 0.30m, Low = 0.30m }] },
            [book],
            6,
            5,
            false,
            new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc),
            null);

        Assert.That(detail.Holdings[0].Fills, Has.Count.EqualTo(1));
        Assert.That(detail.Marks.Any(mark => mark.Kind is "buy" or "sell"), Is.False);
    }

    static WalletBook Book(string key, string name, bool archived, (string Symbol, decimal Qty, decimal Cost) position, params Trade[] trades)
    {
        return new WalletBook
        {
            Wallet = new Wallet
            {
                Key = key,
                Name = name,
                Kind = key.StartsWith("pm-", StringComparison.Ordinal) ? WalletKind.PredictionMarket : WalletKind.Standard,
                IsArchived = archived,
            },
            Trades = trades.ToList(),
            Replay = new LedgerReplay
            {
                States =
                [
                    new LedgerState
                    {
                        Symbols = new Dictionary<string, SymbolSnap>(StringComparer.Ordinal)
                        {
                            [position.Symbol] = new SymbolSnap { Symbol = position.Symbol, Qty = position.Qty, Cost = position.Cost },
                        },
                    },
                ],
            },
        };
    }
}
