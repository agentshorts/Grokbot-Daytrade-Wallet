using System.Globalization;
using System.Net;
using NUnit.Framework;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

public class OscillationTests
{
    [TestCase("22,40,30,45,35", "5", 3)]
    [TestCase("22,40,28,39,27", "5", 3)]
    [TestCase("22,30,40", "5", 0)]
    [TestCase("22,24,21,23", "5", 0)]
    [TestCase("10,15,10", "5", 1)]
    [TestCase("10,14.9,10", "5", 0)]
    [TestCase("40,22,35", "5", 1)]
    [TestCase("22,40,37,36,30", "5", 1)]
    [TestCase("22,40,36,50", "5", 0)]
    [TestCase("22", "5", 0)]
    [TestCase("22,40,30", "0", 0)]
    public void Counts_zigzag_reversals(string prices, string threshold, int expected)
    {
        var series = prices.Split(',').Select(item => decimal.Parse(item, CultureInfo.InvariantCulture)).ToList();
        var size = decimal.Parse(threshold, CultureInfo.InvariantCulture);
        Assert.That(OscillationMath.CountReversals(series, size), Is.EqualTo(expected));
        Assert.That(OscillationMath.IsOscillating(expected), Is.EqualTo(expected >= 2));
    }

    [Test]
    public void Empty_series_has_no_reversals()
    {
        Assert.That(OscillationMath.CountReversals(null, 5), Is.EqualTo(0));
        Assert.That(OscillationMath.CountReversals([], 5), Is.EqualTo(0));
    }

    [Test]
    public void Swing_range_is_high_minus_low_in_cents_and_percent_of_the_low()
    {
        var range = OscillationMath.Measure([0.40m, 0.45m], [0.22m, 0.30m]);
        Assert.That(range.High, Is.EqualTo(0.45m));
        Assert.That(range.Low, Is.EqualTo(0.22m));
        Assert.That(range.Cents, Is.EqualTo(23m));
        Assert.That(range.Percent, Is.EqualTo((0.45m - 0.22m) / 0.22m * 100m));
    }

    [Test]
    public void Swing_from_22c_to_40c_is_18_cents()
    {
        var range = OscillationMath.Measure([0.40m], [0.22m]);
        Assert.That(range.Cents, Is.EqualTo(18m));
        Assert.That(range.Percent, Is.EqualTo(18m / 22m * 100m));
    }

    [Test]
    public void Swing_range_of_an_empty_window_is_zero()
    {
        var range = OscillationMath.Measure([], []);
        Assert.That(range.High, Is.Null);
        Assert.That(range.Cents, Is.EqualTo(0));
        Assert.That(range.Percent, Is.Null);
    }

    [Test]
    public void Swing_percent_is_absent_when_the_low_is_zero()
    {
        var range = OscillationMath.Measure([0.10m], [0m]);
        Assert.That(range.Cents, Is.EqualTo(10m));
        Assert.That(range.Percent, Is.Null);
    }

    [Test]
    public void Maps_coinbase_product_id_to_kalshi_ticker_and_series()
    {
        var mapped = OscillationTickers.Map("KXBTCD-26NOV1617-T64999.99-KALSHI");
        Assert.That(mapped, Is.Not.Null);
        Assert.That(mapped!.Value.Series, Is.EqualTo("KXBTCD"));
        Assert.That(mapped.Value.Ticker, Is.EqualTo("KXBTCD-26NOV1617-T64999.99"));
        Assert.That(mapped.Value.ProductId, Is.EqualTo("KXBTCD-26NOV1617-T64999.99-KALSHI"));

        Assert.That(OscillationTickers.Map("pm-KXBTCD-26NOV1617-T64999.99-KALSHI"), Is.EqualTo(mapped));
        Assert.That(OscillationTickers.Map("kxbtcd-26nov1617-t64999.99"), Is.EqualTo(mapped));
        Assert.That(OscillationTickers.Map("btc-usd"), Is.Null);
        Assert.That(OscillationTickers.Map("pm-win"), Is.Null);
        Assert.That(OscillationTickers.Map(null), Is.Null);
        Assert.That(OscillationTickers.Map("  "), Is.Null);
    }

    [Test]
    public void Close_time_is_shown_in_central_time()
    {
        Assert.That(CentralTime.Format(new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc)), Is.EqualTo("4:00 PM CST, Nov 16"));
        Assert.That(CentralTime.Format(new DateTime(2026, 7, 16, 21, 0, 0, DateTimeKind.Utc)), Is.EqualTo("4:00 PM CDT, Jul 16"));
        Assert.That(CentralTime.Format(null), Is.Null);
    }

    [Test]
    public void Kalshi_trade_prices_fill_gaps_and_legacy_cents_scale_to_dollars()
    {
        const string json = """
            {"candlesticks":[
              {"end_period_ts":100,"price":{"open_dollars":"0.2200","high_dollars":"0.4000","low_dollars":"0.2200","close_dollars":"0.4000"}},
              {"end_period_ts":160,"price":{"previous_dollars":"0.4000"}},
              {"end_period_ts":220,"price":{"open":30,"high":45,"low":28,"close":30}}
            ]}
            """;
        var bars = PredictionBars.FromKalshi(json);
        Assert.That(bars.Select(bar => bar.Close), Is.EqualTo(new[] { 0.40m, 0.40m, 0.30m }));
        var range = OscillationMath.Measure(bars.Select(bar => bar.High).ToList(), bars.Select(bar => bar.Low).ToList());
        Assert.That(range.High, Is.EqualTo(0.45m));
        Assert.That(range.Low, Is.EqualTo(0.22m));
        Assert.That(range.Cents, Is.EqualTo(23m));
    }

    [Test]
    public void Kalshi_quote_mids_are_used_when_nothing_has_traded()
    {
        const string json = """
            {"candlesticks":[
              {"end_period_ts":1,"price":{},"yes_bid":{"close_dollars":"0.2000"},"yes_ask":{"close_dollars":"0.2400"}},
              {"end_period_ts":2,"price":{},"yes_bid":{"close_dollars":"0.4000"},"yes_ask":{"close_dollars":"0.4400"}}
            ]}
            """;
        var bars = PredictionBars.FromKalshi(json);
        Assert.That(bars.Select(bar => bar.Close), Is.EqualTo(new[] { 0.22m, 0.42m }));
    }

    [Test]
    public void Bouncing_contract_is_flagged_and_ranked_by_the_swing()
    {
        var mapped = OscillationTickers.Map("KXBTCD-26NOV1617-T64999.99-KALSHI")!.Value;
        var quote = new PredictionQuote
        {
            Source = "kalshi",
            Price = 0.35m,
            CloseAt = new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc),
            Bars =
            [
                new PredictionBar { Close = 0.22m, High = 0.22m, Low = 0.22m },
                new PredictionBar { Close = 0.40m, High = 0.40m, Low = 0.22m },
                new PredictionBar { Close = 0.30m, High = 0.40m, Low = 0.30m },
                new PredictionBar { Close = 0.45m, High = 0.45m, Low = 0.30m },
            ],
        };
        var row = OscillationReport.Describe(new OpenContract(mapped, "pm-" + mapped.ProductId, "BTC", 4), quote, 5);
        Assert.That(row.Reversals, Is.EqualTo(2));
        Assert.That(row.Oscillating, Is.True);
        Assert.That(row.PriceCents, Is.EqualTo(35m));
        Assert.That(row.HighCents, Is.EqualTo(45m));
        Assert.That(row.LowCents, Is.EqualTo(22m));
        Assert.That(row.RangeCents, Is.EqualTo(23m));
        Assert.That(row.CloseCentral, Is.EqualTo("4:00 PM CST, Nov 16"));
        Assert.That(row.Series, Is.EqualTo("KXBTCD"));
    }

    [Test]
    public void Default_list_keeps_open_pm_wallets_and_drops_archived_flat_and_other_books()
    {
        var open = "KXBTCD-26NOV1617-T64999.99-KALSHI";
        var other = "KXBTCD-26NOV1617-T65249.99-KALSHI";
        var books = new[]
        {
            Book("pm-" + open, WalletKind.PredictionMarket, false, (open, 4m)),
            Book("pm-" + other, WalletKind.PredictionMarket, true, (other, 2m)),
            Book("pm-KXBTCD-26NOV1617-T65499.99-KALSHI", WalletKind.PredictionMarket, false, ("KXBTCD-26NOV1617-T65499.99-KALSHI", 0m)),
            Book("coinbase-btc", WalletKind.Standard, false, ("BTC-USD", 1m)),
            Book("kalshi-real", WalletKind.PredictionMarket, false, ("KXBTCD-26NOV1617-T65749.99-KALSHI", 3m)),
        };

        var selected = OscillationContracts.Select(books);

        Assert.That(selected.Select(row => row.Ticker.Ticker), Is.EqualTo(new[] { "KXBTCD-26NOV1617-T64999.99" }));
        Assert.That(selected[0].Qty, Is.EqualTo(4m));
        Assert.That(selected[0].Wallet, Is.EqualTo("pm-" + open));
    }

    [Test]
    public async Task Coinbase_miss_falls_back_to_kalshi_without_credentials()
    {
        var handler = new ScriptedHandler();
        handler.When("api.coinbase.com", HttpStatusCode.NotFound, "");
        handler.When("api.elections.kalshi.com", HttpStatusCode.OK, """
            {"candlesticks":[{"end_period_ts":100,"price":{"open_dollars":"0.2200","high_dollars":"0.4000","low_dollars":"0.2200","close_dollars":"0.4000"}}]}
            """, "/candlesticks");
        handler.When("api.elections.kalshi.com", HttpStatusCode.OK, """
            {"market":{"close_time":"2026-11-16T22:00:00Z","last_price_dollars":"0.5600","yes_bid_dollars":"0.5400","yes_ask_dollars":"0.5500"}}
            """, "/markets/");
        using var http = new HttpClient(handler);
        var quotes = new PublicPredictionQuotes(http);
        var mapped = OscillationTickers.Map("KXBTCD-26NOV1617-T64999.99-KALSHI")!.Value;

        var quote = await quotes.Load(mapped, new DateTime(2026, 11, 16, 16, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc));

        Assert.That(quote.Source, Is.EqualTo("kalshi"));
        Assert.That(quote.Price, Is.EqualTo(0.56m));
        Assert.That(quote.CloseAt, Is.EqualTo(new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc)));
        Assert.That(quote.Bars.Select(bar => bar.Close), Is.EqualTo(new[] { 0.40m }));
        Assert.That(handler.Paths[0], Does.Contain("/candles"));
        Assert.That(handler.Paths[0], Does.Contain(mapped.ProductId));
        Assert.That(handler.Paths.Any(path => path.Contains("/series/KXBTCD/markets/" + mapped.Ticker + "/candlesticks", StringComparison.Ordinal)), Is.True);
        Assert.That(handler.Requests.All(request => request.Method == HttpMethod.Get), Is.True);
        Assert.That(handler.Requests.All(request => request.Headers.Authorization == null), Is.True);
        Assert.That(handler.Requests.All(request => !request.Headers.Contains("CB-ACCESS-KEY") && !request.Headers.Contains("X-Api-Key")), Is.True);
        Assert.That(handler.Paths.All(path => !path.Contains("order", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task Coinbase_candles_win_when_the_public_product_exists()
    {
        var handler = new ScriptedHandler();
        handler.When("api.coinbase.com", HttpStatusCode.OK, """
            {"candles":[{"start":"100","low":"0.22","high":"0.40","open":"0.22","close":"0.35","volume":"1"}]}
            """, "/candles");
        handler.When("api.coinbase.com", HttpStatusCode.OK, """
            {"trades":[{"price":"0.3600","product_id":"KXBTCD-26NOV1617-T64999.99-KALSHI"}]}
            """, "/ticker");
        handler.When("api.coinbase.com", HttpStatusCode.OK, """
            {"prediction_market_product_details":{"close_time":"2026-11-16T22:00:00Z"}}
            """);
        using var http = new HttpClient(handler);
        var quotes = new PublicPredictionQuotes(http);
        var mapped = OscillationTickers.Map("pm-KXBTCD-26NOV1617-T64999.99-KALSHI")!.Value;

        var quote = await quotes.Load(mapped, new DateTime(2026, 11, 16, 16, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc));

        Assert.That(quote.Source, Is.EqualTo("coinbase"));
        Assert.That(quote.Price, Is.EqualTo(0.36m));
        Assert.That(quote.Bars[0].Low, Is.EqualTo(0.22m));
        Assert.That(quote.Bars[0].High, Is.EqualTo(0.40m));
        Assert.That(quote.CloseAt, Is.EqualTo(new DateTime(2026, 11, 16, 22, 0, 0, DateTimeKind.Utc)));
        Assert.That(handler.Requests.All(request => request.RequestUri!.Host.Contains("coinbase", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(handler.Requests.All(request => request.Headers.Authorization == null), Is.True);
    }

    static WalletBook Book(string key, WalletKind kind, bool archived, params (string Symbol, decimal Qty)[] symbols)
    {
        return new WalletBook
        {
            Wallet = new Wallet
            {
                Key = key,
                Name = key,
                Kind = kind,
                IsArchived = archived,
            },
            Replay = new LedgerReplay
            {
                States =
                [
                    new LedgerState
                    {
                        Symbols = symbols.ToDictionary(
                            row => row.Symbol,
                            row => new SymbolSnap { Symbol = row.Symbol, Qty = row.Qty },
                            StringComparer.Ordinal),
                    },
                ],
            },
        };
    }

    sealed class ScriptedHandler : HttpMessageHandler
    {
        readonly List<(string Host, string PathContains, HttpStatusCode Status, string Body)> routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Paths { get; } = [];

        public void When(string host, HttpStatusCode status, string body, string pathContains = "")
            => routes.Add((host, pathContains, status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Respond(request));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => Respond(request);

        HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Requests.Add(request);
            var path = request.RequestUri?.PathAndQuery ?? "";
            Paths.Add((request.RequestUri?.Host ?? "") + path);
            var route = routes.FirstOrDefault(item =>
                request.RequestUri?.Host.Contains(item.Host, StringComparison.OrdinalIgnoreCase) == true
                && (item.PathContains.Length == 0 || path.Contains(item.PathContains, StringComparison.Ordinal)));
            var status = route.Status == 0 ? HttpStatusCode.NotFound : route.Status;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(route.Body ?? ""),
            };
        }
    }
}
