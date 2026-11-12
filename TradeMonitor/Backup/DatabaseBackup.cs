using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TradeMonitor;

/// <summary>
/// Copies App_Data/*.db to dated files and keeps a fixed number of dates.
/// </summary>
public static class DatabaseBackup
{
    public const int DefaultKeep = 14;

    public sealed record Result(IReadOnlyList<string> Created, IReadOnlyList<string> Removed);

    public static Result Copy(string appDataDirectory, string backupDirectory, int keep = DefaultKeep, DateTime? utcNow = null)
    {
        if (keep < 1)
            throw new ArgumentOutOfRangeException(nameof(keep), "Retention must be at least 1.");

        Directory.CreateDirectory(appDataDirectory);
        Directory.CreateDirectory(backupDirectory);

        var stamp = (utcNow ?? DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var created = new List<string>();
        foreach (var source in Directory.EnumerateFiles(appDataDirectory, "*.db"))
        {
            var name = Path.GetFileNameWithoutExtension(source);
            var destination = Path.Combine(backupDirectory, name + "-" + stamp + ".db");
            CopyDatabase(source, destination);
            created.Add(destination);
        }

        var removed = Prune(backupDirectory, keep);
        return new Result(created, removed);
    }

    public static IReadOnlyList<string> Prune(string backupDirectory, int keep)
    {
        if (keep < 1)
            throw new ArgumentOutOfRangeException(nameof(keep), "Retention must be at least 1.");
        if (!Directory.Exists(backupDirectory))
            return [];

        var parsed = new List<(string Path, DateTime Date)>();
        foreach (var file in Directory.EnumerateFiles(backupDirectory, "*.db"))
        {
            if (TryReadStamp(Path.GetFileName(file), out var date))
                parsed.Add((file, date));
        }

        var drop = new HashSet<DateTime>(parsed
            .Select(item => item.Date)
            .Distinct()
            .OrderByDescending(date => date)
            .Skip(keep));

        var removed = new List<string>();
        foreach (var item in parsed)
        {
            if (!drop.Contains(item.Date))
                continue;
            File.Delete(item.Path);
            removed.Add(item.Path);
        }

        return removed;
    }

    public static bool TryReadStamp(string fileName, out DateTime date)
    {
        date = default;
        if (fileName.Length < 14 || !fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = fileName[..^3];
        if (stem.Length < 11 || stem[^11] != '-')
            return false;

        return DateTime.TryParseExact(
            stem[^10..],
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    static void CopyDatabase(string source, string destination)
    {
        var partial = destination + ".partial";
        if (File.Exists(partial))
            File.Delete(partial);

        // ReadWrite so a WAL checkpoint can run. ReadOnly backup fails on a database
        // that still has frames in the write-ahead log.
        var fromBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };
        var toBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = partial,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };

        using (var from = new SqliteConnection(fromBuilder.ConnectionString))
        using (var to = new SqliteConnection(toBuilder.ConnectionString))
        {
            from.Open();
            to.Open();
            from.BackupDatabase(to);
        }

        File.Move(partial, destination, overwrite: true);
    }
}
