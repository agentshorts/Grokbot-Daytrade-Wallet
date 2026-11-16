using ServiceStack;
using TradeMonitor.ServiceModel;

namespace TradeMonitor.ServiceInterface;

public class StepLadderServices(PriceBars bars) : Service
{
    public object Any(GetStepLadder request)
    {
        var symbol = request.Symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
            throw new HttpError(400, "InvalidRequest", "Symbol is required.");
        if (!CandleInterval.TryParse(request.Interval, emptyIsDefault: true, out var interval, out var error))
            throw new HttpError(400, "InvalidInterval", error);

        var stepIncrease = request.StepIncrease ?? StepLadder.DefaultStepIncrease;
        var dormantThreshold = request.DormantThreshold ?? StepLadder.DefaultDormantThreshold;
        var dormantPeriod = request.DormantPeriod ?? StepLadder.DefaultDormantPeriod;
        if (stepIncrease <= 0)
            throw new HttpError(400, "InvalidRequest", "Step increase must be greater than 0.");
        if (dormantThreshold <= 0)
            throw new HttpError(400, "InvalidRequest", "Dormant threshold must be greater than 0.");
        if (dormantPeriod < 1)
            throw new HttpError(400, "InvalidRequest", "Dormant period must be a whole number of at least 1.");

        var page = bars.Window(symbol, interval, request.From, request.To, CandleInterval.Cap);
        var study = StepLadder.Compute(page.Candles, stepIncrease, dormantThreshold, dormantPeriod);
        return new GetStepLadderResponse
        {
            Symbol = symbol,
            Interval = page.Key,
            StepIncrease = stepIncrease,
            DormantThreshold = dormantThreshold,
            DormantPeriod = dormantPeriod,
            Bars = study.Select(bar => new StepLadderBarDto
            {
                Time = bar.Time,
                Volume = bar.Volume,
                Vol24 = bar.Vol24,
                Step = bar.Step,
                Dormant = bar.Dormant,
            }).ToList(),
        };
    }
}
