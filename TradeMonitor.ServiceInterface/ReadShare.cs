using System.Collections.Concurrent;

namespace TradeMonitor.ServiceInterface;

// Joins callers that ask for the same read while it is already running.
// The result is not kept after that read finishes, so the next call sees new writes.
public static class ReadShare
{
    static readonly ConcurrentDictionary<object, Task<object?>> Inflight = new();

    public static T Share<T>(object key, Func<T> load)
    {
        var created = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        while (true)
        {
            if (Inflight.TryGetValue(key, out var existing))
            {
                if (!existing.IsCompleted)
                    return (T)existing.GetAwaiter().GetResult()!;
                Inflight.TryRemove(new KeyValuePair<object, Task<object?>>(key, existing));
            }

            if (!Inflight.TryAdd(key, created.Task))
                continue;

            try
            {
                var value = load();
                created.SetResult(value);
                return value;
            }
            catch (Exception ex)
            {
                created.SetException(ex);
                throw;
            }
            finally
            {
                Inflight.TryRemove(new KeyValuePair<object, Task<object?>>(key, created.Task));
            }
        }
    }
}
