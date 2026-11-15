using Microsoft.Extensions.Options;
using TradeMonitor.ServiceInterface;

namespace TradeMonitor;

// Refreshes tracked symbols off the live tick path. A missing or disabled model does no work.
public sealed class ForecastRefreshHostedService(ForecastCache cache, PriceBars bars, TrackedSymbolBook tracked, IOptions<ForecastOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Enabled && cache.RunnerReady)
                    Refresh(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // The next pass retries. The price feed does not wait on this loop.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    void Refresh(CancellationToken cancel)
    {
        var horizon = Math.Clamp(options.Value.HorizonBars, 1, ForecastOptions.MaxHorizon);
        foreach (var symbol in tracked.PollSymbols())
        {
            cancel.ThrowIfCancellationRequested();
            var page = bars.Window(symbol, CandleInterval.FiveMinutes, null, null, CandleInterval.Cap);
            if (page.Candles.Count == 0)
                continue;
            var cached = cache.Get(symbol, page.Key, horizon);
            if (cached.Steps.Count > 0 && cached.CandleTime == page.Candles[^1].Start)
                continue;
            cache.Refresh(symbol, page.Key, page.Candles, horizon, page.Seconds, cancel);
        }
    }
}
