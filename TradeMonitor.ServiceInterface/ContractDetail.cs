using System.Globalization;
using System.Text.RegularExpressions;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public readonly record struct QuotedSide(decimal? Cents, decimal? BidCents, decimal? AskCents, string Label, bool Implied);

// Picks the quote the source actually published. Bid and ask win when both exist.
// A last trade is next. The candle close is the fallback. NO is 1 minus YES when the book has no NO side.
public static class SideQuotes
{
    public static QuotedSide Yes(PredictionQuote quote)
    {
        var bid = Cents(quote.YesBid);
        var ask = Cents(quote.YesAsk);
        var last = Cents(quote.Last);
        var close = Cents(quote.Price);
        if (bid != null && ask != null)
            return new QuotedSide(last ?? Mid(bid.Value, ask.Value) ?? close, bid, ask, "bid/ask", false);
        if (last != null)
            return new QuotedSide(last, bid, ask, "last", false);
        if (close != null)
            return new QuotedSide(close, null, null, "close", false);
        return new QuotedSide(null, null, null, "", false);
    }

    public static QuotedSide No(PredictionQuote quote)
    {
        var bid = Cents(quote.NoBid);
        var ask = Cents(quote.NoAsk);
        if (bid != null && ask != null)
            return new QuotedSide(Mid(bid.Value, ask.Value), bid, ask, "bid/ask", false);
        if (bid != null)
            return new QuotedSide(bid, bid, null, "bid", false);
        if (ask != null)
            return new QuotedSide(ask, null, ask, "ask", false);
        var yes = Yes(quote);
        if (yes.Cents != null)
            return new QuotedSide(100m - yes.Cents.Value, null, null, "implied", true);
        return new QuotedSide(null, null, null, "", true);
    }

    static decimal? Cents(decimal? dollars) => dollars == null ? null : dollars.Value * 100m;

    static decimal? Mid(decimal bid, decimal ask) => ask >= bid ? (bid + ask) / 2m : null;
}

public readonly record struct ContractShape(string? Underlying, decimal? Strike, string Direction);

public static class ContractIdentity
{
    static readonly Regex Strike = new(@"-([TB])(\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ContractShape Read(string series, string ticker)
    {
        decimal? strike = null;
        var direction = "";
        var match = Strike.Match(ticker.ToUpperInvariant());
        if (match.Success && decimal.TryParse(match.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            strike = parsed;
            direction = match.Groups[1].Value == "B" ? "below" : "above";
        }

        return new ContractShape(Asset(series), strike, direction);
    }

    static string? Asset(string series)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KXBTCD"] = "BTC",
            ["KXETHD"] = "ETH",
            ["KXSOLD"] = "SOL",
            ["KXDOGED"] = "DOGE",
            ["KXXRPD"] = "XRP",
            ["KXBNBD"] = "BNB",
            ["KXAVAXD"] = "AVAX",
            ["KXLINKD"] = "LINK",
            ["KXADAD"] = "ADA",
            ["KXLTCD"] = "LTC",
        };
        if (known.TryGetValue(series, out var asset))
            return asset;
        var daily = Regex.Match(series, @"^KX([A-Z]{2,5})D$");
        if (daily.Success)
            return daily.Groups[1].Value;
        var bare = Regex.Match(series, @"^KX([A-Z]{2,5})$");
        return bare.Success ? bare.Groups[1].Value : null;
    }
}

// Open pm-* wallets only. Archived books and flat positions are left out. Nothing here is written.
public static class ContractHoldings
{
    public static List<PredictionHolding> Open(IEnumerable<WalletBook> books, string ticker, decimal? mark)
    {
        var found = new List<PredictionHolding>();
        foreach (var book in books)
        {
            var wallet = book.Wallet;
            if (wallet.Kind != WalletKind.PredictionMarket || wallet.IsArchived || !PredictionMarkets.IsHandWallet(wallet.Key))
                continue;
            var symbols = book.Replay.States.LastOrDefault()?.Symbols;
            if (symbols == null)
                continue;
            foreach (var symbol in symbols.Values)
            {
                if (symbol.Qty <= 0)
                    continue;
                var mapped = OscillationTickers.Map(symbol.Symbol) ?? OscillationTickers.Map(wallet.Key);
                if (mapped == null || mapped.Value.Ticker != ticker)
                    continue;
                var qty = symbol.Qty;
                found.Add(new PredictionHolding
                {
                    Wallet = wallet.Key,
                    Name = string.IsNullOrWhiteSpace(wallet.Name) ? wallet.Key : wallet.Name,
                    Side = "YES",
                    Qty = qty,
                    AvgCost = qty == 0 ? 0 : symbol.Cost / qty,
                    UnrealizedPnl = mark == null ? symbol.Unrealized : qty * mark.Value - symbol.Cost,
                    Fills = Fills(book, ticker),
                });
            }
        }

        return found
            .OrderBy(row => row.Wallet, StringComparer.Ordinal)
            .ToList();
    }

    static List<PredictionFill> Fills(WalletBook book, string ticker)
    {
        return book.Trades
            .Where(trade => trade.Kind == TradeKind.Fill && trade.Qty > 0 && Same(trade.Symbol, ticker))
            .OrderBy(trade => trade.ExecutedAt)
            .ThenBy(trade => trade.Id)
            .Select(trade => new PredictionFill
            {
                TradeId = trade.Id,
                Side = trade.Side == TradeSide.Sell ? "sell" : "buy",
                Qty = trade.Qty,
                Price = trade.Price,
                At = DateTime.SpecifyKind(trade.ExecutedAt, DateTimeKind.Utc),
            })
            .ToList();
    }

    static bool Same(string symbol, string ticker)
    {
        var mapped = OscillationTickers.Map(symbol);
        return mapped != null && mapped.Value.Ticker == ticker;
    }
}

public static class ContractReport
{
    public static PredictionContractDetail Build(
        MappedTicker ticker,
        PredictionQuote quote,
        IEnumerable<WalletBook> books,
        decimal lookbackHours,
        decimal thresholdCents,
        bool allAvailable,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime? closeFallback)
    {
        var shape = ContractIdentity.Read(ticker.Series, ticker.Ticker);
        var strike = quote.Strike ?? shape.Strike;
        var close = quote.CloseAt ?? closeFallback;
        var yes = SideQuotes.Yes(quote);
        var no = SideQuotes.No(quote);
        var bars = quote.Bars ?? [];
        var swing = OscillationMath.Measure(bars.Select(bar => bar.High).ToList(), bars.Select(bar => bar.Low).ToList());
        var turns = OscillationMath.Turns(bars.Select(bar => bar.Close * 100m).ToList(), thresholdCents);
        var holdings = ContractHoldings.Open(books, ticker.Ticker, quote.Price);
        var shaped = new ContractShape(shape.Underlying, strike, shape.Direction);

        return new PredictionContractDetail
        {
            Ticker = ticker.Ticker,
            ProductId = ticker.ProductId,
            Series = ticker.Series,
            Title = Title(ticker, quote, shaped),
            Subtitle = string.IsNullOrWhiteSpace(quote.Subtitle) ? null : quote.Subtitle.Trim(),
            Underlying = shape.Underlying,
            Strike = strike,
            Direction = shape.Direction,
            Status = Status(quote, close, toUtc),
            CloseAt = close,
            CloseCentral = CentralTime.Format(close),
            CloseEastern = EasternTime.Format(close),
            YesCents = yes.Cents,
            YesBidCents = yes.BidCents,
            YesAskCents = yes.AskCents,
            YesLabel = yes.Label,
            NoCents = no.Cents,
            NoBidCents = no.BidCents,
            NoAskCents = no.AskCents,
            NoLabel = no.Label,
            NoImplied = no.Implied,
            NoLineImplied = true,
            HighCents = Cents(swing.High),
            LowCents = Cents(swing.Low),
            RangeCents = swing.Cents,
            RangePercent = swing.Percent,
            Reversals = turns.Count,
            Oscillating = OscillationMath.IsOscillating(turns.Count),
            Volume = quote.Volume,
            OpenInterest = quote.OpenInterest,
            LookbackHours = lookbackHours,
            ThresholdCents = thresholdCents,
            AllAvailable = allAvailable,
            Source = string.IsNullOrEmpty(quote.Source) ? "none" : quote.Source,
            Error = bars.Count == 0 ? quote.Error ?? "No price history in this window." : quote.Error,
            Points = bars.Select(Point).ToList(),
            Marks = Marks(bars, turns, holdings, fromUtc, toUtc),
            Holdings = holdings,
        };
    }

    static PredictionChartPoint Point(PredictionBar bar)
    {
        var yes = bar.Close * 100m;
        var no = 100m - yes;
        if (no < 0)
            no = 0;
        if (no > 100)
            no = 100;
        return new PredictionChartPoint { At = Unix(bar.At), YesCents = yes, NoCents = no };
    }

    static List<PredictionChartMark> Marks(
        List<PredictionBar> bars,
        List<PriceTurn> turns,
        List<PredictionHolding> holdings,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var marks = new List<PredictionChartMark>();
        if (bars.Count > 0)
        {
            var highIndex = 0;
            var lowIndex = 0;
            for (var i = 1; i < bars.Count; i++)
            {
                if (bars[i].High > bars[highIndex].High)
                    highIndex = i;
                if (bars[i].Low < bars[lowIndex].Low)
                    lowIndex = i;
            }

            var used = new HashSet<int>();
            if (highIndex == lowIndex)
            {
                marks.Add(Bar(bars[highIndex], "high", bars[highIndex].High == bars[lowIndex].Low ? "High" : "High / Low"));
                used.Add(highIndex);
            }
            else
            {
                marks.Add(Bar(bars[highIndex], "high", "High"));
                marks.Add(Bar(bars[lowIndex], "low", "Low"));
                used.Add(highIndex);
                used.Add(lowIndex);
            }

            foreach (var turn in turns)
            {
                if (turn.Index < 0 || turn.Index >= bars.Count || !used.Add(turn.Index))
                    continue;
                marks.Add(Bar(bars[turn.Index], turn.Extreme == "low" ? "low" : "high", "Reversal"));
            }
        }

        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        foreach (var fill in holdings.SelectMany(row => row.Fills))
        {
            var at = DateTime.SpecifyKind(fill.At, DateTimeKind.Utc);
            if (at < from || at > to)
                continue;
            var sell = fill.Side == "sell";
            marks.Add(new PredictionChartMark
            {
                At = at,
                Kind = sell ? "sell" : "buy",
                Text = (sell ? "Sell " : "Buy ") + Qty(fill.Qty) + " @ " + CentsText(fill.Price) + "c",
            });
        }

        return marks;
    }

    static PredictionChartMark Bar(PredictionBar bar, string kind, string text) => new()
    {
        At = Unix(bar.At),
        Kind = kind,
        Text = text,
    };

    static string Title(MappedTicker ticker, PredictionQuote quote, ContractShape shape)
    {
        if (!string.IsNullOrWhiteSpace(quote.Title))
            return quote.Title.Trim();
        if (shape.Underlying != null && shape.Strike != null && shape.Direction.Length > 0)
            return shape.Underlying + " " + shape.Direction + " " + Money(shape.Strike.Value);
        return ticker.Ticker;
    }

    static string? Status(PredictionQuote quote, DateTime? close, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(quote.Status))
            return quote.Status.Trim().ToLowerInvariant();
        if (close == null)
            return null;
        return close <= DateTime.SpecifyKind(now, DateTimeKind.Utc) ? "closed" : "open";
    }

    static string Money(decimal value)
    {
        var digits = value == decimal.Truncate(value) ? 0 : 2;
        return value.ToString("C" + digits, CultureInfo.GetCultureInfo("en-US"));
    }

    static string Qty(decimal qty) => qty.ToString("0.########", CultureInfo.InvariantCulture);

    static string CentsText(decimal dollars)
    {
        var cents = decimal.Round(dollars * 100m, 1);
        return cents == decimal.Truncate(cents)
            ? decimal.Truncate(cents).ToString(CultureInfo.InvariantCulture)
            : cents.ToString("0.0", CultureInfo.InvariantCulture);
    }

    static decimal? Cents(decimal? dollars) => dollars == null ? null : dollars.Value * 100m;

    static DateTime Unix(long seconds) => DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime, DateTimeKind.Utc);
}
