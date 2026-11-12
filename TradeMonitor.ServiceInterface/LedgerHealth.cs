using System.Data;
using System.Globalization;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace TradeMonitor.ServiceInterface;

public sealed class LedgerHealth
{
    public string Status { get; set; } = "ok";

    public string Database { get; set; } = "ok";

    public string? LastWriteAt { get; set; }

    public string Version { get; set; } = "";
}

/// <summary>
/// Database status and the latest ledger write. The endpoint stays on the loopback host.
/// </summary>
public static class LedgerHealthReport
{
    static readonly (string Table, string Column)[] Writes =
    [
        ("Wallet", "CreatedAt"),
        ("Trade", "RecordedAt"),
        ("Transfer", "RecordedAt"),
        ("PriceMark", "RecordedAt"),
    ];

    public static LedgerHealth Create(IDbConnectionFactory dbFactory, string version)
    {
        try
        {
            using var db = dbFactory.Open();
            db.SqlScalar<int>("SELECT 1");
            return new LedgerHealth
            {
                Status = "ok",
                Database = "ok",
                LastWriteAt = FormatUtc(LatestWrite(db)),
                Version = version,
            };
        }
        catch (Exception)
        {
            return new LedgerHealth
            {
                Status = "unavailable",
                Database = "unavailable",
                Version = version,
            };
        }
    }

    public static string? FormatUtc(DateTime? value)
    {
        if (value == null)
            return null;

        var utc = value.Value.Kind == DateTimeKind.Local
            ? value.Value.ToUniversalTime()
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        var format = utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? "yyyy-MM-dd'T'HH:mm:ss'Z'"
            : "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
        return utc.ToString(format, CultureInfo.InvariantCulture);
    }

    static DateTime? LatestWrite(IDbConnection db)
    {
        DateTime? latest = null;
        foreach (var (table, column) in Writes)
        {
            var value = MaxTimestamp(db, table, column);
            if (value == null)
                continue;

            var utc = value.Value.Kind == DateTimeKind.Local
                ? value.Value.ToUniversalTime()
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
            if (latest == null || utc > latest)
                latest = utc;
        }

        return latest;
    }

    static DateTime? MaxTimestamp(IDbConnection db, string table, string column)
    {
        if (!db.TableExists(table))
            return null;

        return db.SqlScalar<DateTime?>($"SELECT MAX({column}) FROM {table}");
    }
}
