namespace TradeMonitor.ServiceInterface;

public sealed class CoinbaseSyncOptions
{
    public const string Section = "CoinbaseSync";

    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 60;

    public string? StatePath { get; set; }
}

/// <summary>
/// In-memory sync status. Health reads it. It never holds key material.
/// </summary>
public sealed class CoinbaseSyncStatus
{
    public bool Enabled { get; set; }

    public DateTime? LastSyncUtc { get; set; }

    public string? LastError { get; set; }

    public CoinbaseSyncHealth ToHealth() => new()
    {
        Enabled = Enabled,
        LastSyncAt = LastSyncUtc is DateTime at ? LedgerHealthReport.FormatUtc(at) : null,
        LastError = LastError,
    };
}

public sealed class CoinbaseSyncLog
{
    public static CoinbaseSyncLog Ignore { get; } = new();

    public Action<string>? InfoSink { get; init; }

    public Action<string>? ErrorSink { get; init; }

    public void Info(string message) => InfoSink?.Invoke(message);

    public void Error(string message) => ErrorSink?.Invoke(message);
}
