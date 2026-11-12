using System.Diagnostics;

namespace TradeMonitor.ServiceInterface;

public static class CoinbaseSyncLoop
{
    public static readonly TimeSpan ServerDownDelay = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PermissionInterval = TimeSpan.FromHours(1);

    public static async Task EnsureViewOnlyAsync(ICoinbaseReadApi coinbase, CancellationToken cancellationToken)
    {
        var permissions = await coinbase.PermissionsAsync(cancellationToken);
        if (permissions.CanTrade || permissions.CanTransfer)
            throw new CoinbasePermissionException(
                "Coinbase key can trade or transfer. Sync stopped. A view-only key is required.");
    }

    public static async Task<int> RunAsync(
        CoinbaseSyncer syncer,
        Func<CancellationToken, Task>? checkPermissions,
        Func<TimeSpan, CancellationToken, Task> sleep,
        int? maxCycles,
        CoinbaseSyncStatus status,
        ILedgerClock clock,
        TimeSpan interval,
        CoinbaseSyncLog log,
        CancellationToken cancellationToken)
    {
        var cycles = 0;
        var failures = 0;
        DateTime? lastPermissionCheck = null;
        while (maxCycles == null || cycles < maxCycles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cycles++;
            var watch = Stopwatch.StartNew();
            try
            {
                if (checkPermissions != null && PermissionDue(lastPermissionCheck, clock))
                {
                    await checkPermissions(cancellationToken);
                    lastPermissionCheck = AsUtc(clock.UtcNow);
                }
                var (fills, marks) = await syncer.CycleAsync(cancellationToken);
                failures = 0;
                status.LastSyncUtc = AsUtc(clock.UtcNow);
                status.LastError = null;
                if (fills != 0 || marks != 0)
                    log.Info("cycle ok: " + fills + " new fills, " + marks + " marks");
            }
            catch (LedgerUnavailableException ex)
            {
                status.LastError = "Ledger unavailable (" + ex.Message + ").";
                log.Error(status.LastError + " Sleeping 30 minutes, then backfilling.");
                syncer.MarkLedgerStale();
                syncer.SaveState();
                if (maxCycles == null || cycles < maxCycles)
                    await sleep(ServerDownDelay, cancellationToken);
                continue;
            }
            catch (CoinbaseDownException ex)
            {
                failures++;
                var seconds = Math.Min(60 * (1 << Math.Min(failures, 4)), 900);
                status.LastError = "Coinbase unreachable (" + ex.Message + ").";
                log.Error(status.LastError + " Retrying in " + seconds + "s.");
                if (maxCycles == null || cycles < maxCycles)
                    await sleep(TimeSpan.FromSeconds(seconds), cancellationToken);
                continue;
            }
            catch (CoinbaseRejectedException ex)
            {
                status.LastError = "Coinbase rejected a read (" + ex.Message + ").";
                log.Error(status.LastError + " Retrying in 5 minutes.");
                if (maxCycles == null || cycles < maxCycles)
                    await sleep(TimeSpan.FromMinutes(5), cancellationToken);
                continue;
            }
            catch (CoinbasePermissionException ex)
            {
                status.LastError = ex.Message;
                log.Error(ex.Message);
                return cycles;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                status.LastError = "Coinbase sync failed (" + ex.GetType().Name + ").";
                log.Error(status.LastError);
            }

            if (maxCycles == null || cycles < maxCycles)
            {
                var remaining = interval - watch.Elapsed;
                if (remaining < TimeSpan.FromSeconds(1))
                    remaining = TimeSpan.FromSeconds(1);
                await sleep(remaining, cancellationToken);
            }
        }
        return cycles;
    }

    static bool PermissionDue(DateTime? lastCheck, ILedgerClock clock)
    {
        if (lastCheck == null)
            return true;
        return AsUtc(clock.UtcNow) - lastCheck.Value >= PermissionInterval;
    }

    static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
