using System.Globalization;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Zigzag on a price series. The first move that establishes a direction is not a reversal.
// A later move counts when it travels at least `threshold` against the current extreme.
// Prices and the threshold use the same unit. Callers pass cents.
public static class OscillationMath
{
    public static int CountReversals(IReadOnlyList<decimal>? prices, decimal threshold)
    {
        if (prices == null || prices.Count < 2 || threshold <= 0)
            return 0;

        var runHigh = prices[0];
        var runLow = prices[0];
        var direction = 0;
        var reversals = 0;

        for (var i = 1; i < prices.Count; i++)
        {
            var price = prices[i];
            if (direction == 0)
            {
                var extreme = 0;
                if (price >= runHigh)
                {
                    runHigh = price;
                    extreme = 1;
                }

                if (price <= runLow)
                {
                    runLow = price;
                    extreme = -1;
                }

                if (extreme != 0 && runHigh - runLow >= threshold)
                    direction = extreme;
                continue;
            }

            if (direction > 0)
            {
                if (price >= runHigh)
                {
                    runHigh = price;
                    continue;
                }

                if (runHigh - price >= threshold)
                {
                    reversals++;
                    direction = -1;
                    runHigh = price;
                    runLow = price;
                }

                continue;
            }

            if (price <= runLow)
            {
                runLow = price;
                continue;
            }

            if (price - runLow >= threshold)
            {
                reversals++;
                direction = 1;
                runHigh = price;
                runLow = price;
            }
        }

        return reversals;
    }

    public static bool IsOscillating(int reversals) => reversals >= 2;

    // High and low are dollar prices (0 to 1). Cents are (high - low) * 100.
    // Percent is that span divided by the low, so a move from 22c to 40c is 18c and about 82%.
    public static SwingRange Measure(IReadOnlyList<decimal>? highs, IReadOnlyList<decimal>? lows)
    {
        if (highs == null || lows == null || highs.Count == 0 || lows.Count == 0)
            return new SwingRange(null, null, 0, null);

        var high = highs.Max();
        var low = lows.Min();
        if (high < low)
            (high, low) = (low, high);
        var cents = (high - low) * 100m;
        decimal? percent = low > 0 ? (high - low) / low * 100m : null;
        return new SwingRange(high, low, cents, percent);
    }

    public static List<decimal> Sparkline(IReadOnlyList<decimal> values, int maxPoints = 48)
    {
        if (values.Count == 0)
            return [];
        if (maxPoints < 2 || values.Count <= maxPoints)
            return values.ToList();

        var line = new List<decimal>(maxPoints);
        var last = values.Count - 1;
        for (var i = 0; i < maxPoints; i++)
        {
            var index = (int)((long)i * last / (maxPoints - 1));
            line.Add(values[index]);
        }

        return line;
    }
}

public readonly record struct SwingRange(decimal? High, decimal? Low, decimal Cents, decimal? Percent);

// Coinbase product ids end in -KALSHI. Kalshi's ticker is that id without the suffix,
// and the series is the segment before the first hyphen.
public static class OscillationTickers
{
    public static MappedTicker? Map(string? input)
    {
        var ticker = ContractTickers.KalshiTicker(input);
        if (string.IsNullOrEmpty(ticker))
            return null;
        var dash = ticker.IndexOf('-');
        if (dash <= 0)
            return null;
        return new MappedTicker(ticker[..dash], ticker, ticker + "-KALSHI");
    }
}

public readonly record struct MappedTicker(string Series, string Ticker, string ProductId);

public static class OscillationWindow
{
    public const decimal DefaultHours = 6;
    public const decimal DefaultCents = 5;

    public static decimal Hours(decimal? value)
    {
        if (value is null or <= 0)
            return DefaultHours;
        if (value < 1)
            return 1;
        if (value > 168)
            return 168;
        return decimal.Round(value.Value, 2);
    }

    public static decimal Cents(decimal? value)
    {
        if (value is null or <= 0)
            return DefaultCents;
        if (value > 100)
            return 100;
        return decimal.Round(value.Value, 2);
    }

    // Coinbase returns at most 350 candles. One minute fits about five hours; six hours uses five minutes.
    public static string CoinbaseGranularity(TimeSpan span)
    {
        if (span.TotalMinutes <= 300)
            return "ONE_MINUTE";
        if (span.TotalHours <= 24)
            return "FIVE_MINUTE";
        if (span.TotalHours <= 72)
            return "FIFTEEN_MINUTE";
        return "ONE_HOUR";
    }

    // Kalshi allows 1, 60, or 1440 minutes, and at most 5000 periods per request.
    public static int KalshiPeriodMinutes(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        return minutes <= 5000 ? 1 : 60;
    }
}

public static class CentralTime
{
    public static string? Format(DateTime? utc)
    {
        if (utc == null)
            return null;
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }

        var instant = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(instant, zone);
        var abbr = zone.GetUtcOffset(instant) == TimeSpan.FromHours(-5) ? "CDT" : "CST";
        var clock = local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var day = local.ToString("MMM d", CultureInfo.InvariantCulture);
        return clock + " " + abbr + ", " + day;
    }
}

public sealed record OpenContract(MappedTicker Ticker, string? Wallet, string Name, decimal Qty);

// Default list: hand-logged pm-* wallets that are not archived and still have open quantity.
public static class OscillationContracts
{
    public static List<OpenContract> Select(IEnumerable<WalletBook> books)
    {
        var found = new List<OpenContract>();
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
                if (mapped == null)
                    continue;
                found.Add(new OpenContract(mapped.Value, wallet.Key, string.IsNullOrWhiteSpace(wallet.Name) ? mapped.Value.Ticker : wallet.Name, symbol.Qty));
            }
        }

        return found
            .GroupBy(row => row.Ticker.Ticker, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return first with { Qty = group.Sum(row => row.Qty) };
            })
            .ToList();
    }
}

public static class OscillationReport
{
    public static PredictionOscillationRow Describe(OpenContract contract, PredictionQuote quote, decimal thresholdCents)
    {
        var closes = quote.Bars.Select(bar => bar.Close).ToList();
        var swing = OscillationMath.Measure(quote.Bars.Select(bar => bar.High).ToList(), quote.Bars.Select(bar => bar.Low).ToList());
        var reversals = OscillationMath.CountReversals(closes.Select(price => price * 100m).ToList(), thresholdCents);
        var price = quote.Price ?? (closes.Count == 0 ? null : closes[^1]);
        return new PredictionOscillationRow
        {
            Ticker = contract.Ticker.Ticker,
            ProductId = contract.Ticker.ProductId,
            Series = contract.Ticker.Series,
            Wallet = contract.Wallet,
            Name = contract.Name,
            Qty = contract.Qty,
            PriceCents = Cents(price),
            HighCents = Cents(swing.High),
            LowCents = Cents(swing.Low),
            RangeCents = swing.Cents,
            RangePercent = swing.Percent,
            Reversals = reversals,
            Oscillating = OscillationMath.IsOscillating(reversals),
            Sparkline = OscillationMath.Sparkline(closes.Select(item => item * 100m).ToList()),
            CloseAt = quote.CloseAt,
            CloseCentral = CentralTime.Format(quote.CloseAt),
            Source = string.IsNullOrEmpty(quote.Source) ? "none" : quote.Source,
            Error = quote.Bars.Count == 0 ? quote.Error ?? "No price history in this window." : quote.Error,
        };
    }

    static decimal? Cents(decimal? dollars) => dollars == null ? null : dollars.Value * 100m;
}
