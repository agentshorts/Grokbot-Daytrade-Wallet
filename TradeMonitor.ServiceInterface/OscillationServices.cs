using ServiceStack;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

// Read-only oscillation report. Wallet rows come from open pm-* books.
// Prices come from public market data. Nothing here writes a wallet, a trade, or an order.
public class OscillationServices(ServiceStack.Data.IDbConnectionFactory dbFactory, ILedgerClock clock, IPredictionQuotes quotes, IContractCloses closes) : Service
{
    public async Task<object> Any(GetPredictionOscillation request)
    {
        var hours = OscillationWindow.Hours(request.LookbackHours);
        var threshold = OscillationWindow.Cents(request.ThresholdCents);
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var from = now.AddHours(-(double)hours);

        List<OpenContract> contracts;
        using (var db = dbFactory.Open())
            contracts = OscillationContracts.Select(LedgerData.Load(db, now));

        var rows = new PredictionOscillationRow[contracts.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, contracts.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (index, token) => rows[index] = await Describe(contracts[index], from, now, threshold, token).ConfigureAwait(false)).ConfigureAwait(false);

        var ranked = rows
            .OrderByDescending(row => row.Reversals)
            .ThenByDescending(row => row.RangeCents)
            .ThenBy(row => row.Ticker, StringComparer.Ordinal)
            .ToList();

        PredictionOscillationRow? lookup = null;
        string? lookupError = null;
        if (!string.IsNullOrWhiteSpace(request.Ticker))
        {
            var mapped = OscillationTickers.Map(request.Ticker);
            if (mapped == null)
            {
                lookupError = "Enter a prediction ticker or product id, such as KXBTCD-26NOV1617-T64999.99-KALSHI.";
            }
            else
            {
                var known = contracts.FirstOrDefault(contract => contract.Ticker.Ticker == mapped.Value.Ticker);
                var contract = known ?? new OpenContract(mapped.Value, null, mapped.Value.Ticker, 0);
                lookup = await Describe(contract, from, now, threshold, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return new GetPredictionOscillationResponse
        {
            LookbackHours = hours,
            ThresholdCents = threshold,
            Results = ranked,
            Lookup = lookup,
            LookupError = lookupError,
        };
    }

    public async Task<object> Any(GetPredictionContract request)
    {
        var mapped = OscillationTickers.Map(request.Ticker);
        if (mapped == null)
            throw new HttpError(400, "InvalidTicker", "Enter a prediction ticker or product id, such as KXBTCD-26NOV1617-T64999.99-KALSHI.");

        var all = request.AllAvailable == true;
        var hours = OscillationWindow.Span(request.LookbackHours, all);
        var threshold = OscillationWindow.Cents(request.ThresholdCents);
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var from = now.AddHours(-(double)hours);

        List<WalletBook> books;
        using (var db = dbFactory.Open())
            books = LedgerData.Load(db, now);

        PredictionQuote quote;
        try
        {
            quote = await quotes.Load(mapped.Value, from, now, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            quote = PredictionQuote.Failed("Price history is unavailable.");
        }

        return new GetPredictionContractResponse
        {
            Result = ContractReport.Build(mapped.Value, quote, books, hours, threshold, all, from, now, SafeClose(mapped.Value.Ticker)),
        };
    }

    async Task<PredictionOscillationRow> Describe(OpenContract contract, DateTime from, DateTime to, decimal threshold, CancellationToken cancellationToken)
    {
        PredictionQuote quote;
        try
        {
            quote = await quotes.Load(contract.Ticker, from, to, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            quote = PredictionQuote.Failed("Price history is unavailable.");
        }

        var row = OscillationReport.Describe(contract, quote, threshold);
        if (row.CloseAt != null)
            return row;
        row.CloseAt = SafeClose(contract.Ticker.Ticker);
        row.CloseCentral = CentralTime.Format(row.CloseAt);
        return row;
    }

    DateTime? SafeClose(string ticker)
    {
        try
        {
            return closes.CloseAt(ticker);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
