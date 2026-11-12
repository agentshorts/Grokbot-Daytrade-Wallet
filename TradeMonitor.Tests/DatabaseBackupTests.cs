using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace TradeMonitor.Tests;

public class DatabaseBackupTests
{
    string root = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "trademonitor-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public void Dated_copy_keeps_the_newest_dates_and_the_rows()
    {
        var appData = Path.Combine(root, "App_Data");
        var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(backups);
        CreateDatabase(Path.Combine(appData, "app.db"), "paper");
        CreateDatabase(Path.Combine(appData, "extra.db"), "extra");
        File.WriteAllText(Path.Combine(backups, "keep.txt"), "leave me");
        File.WriteAllText(Path.Combine(backups, "app-2026-01-01.db"), "old");
        File.WriteAllText(Path.Combine(backups, "app-2026-01-02.db"), "old");
        File.WriteAllText(Path.Combine(backups, "extra-2026-01-03.db"), "keep");

        var result = DatabaseBackup.Copy(appData, backups, keep: 2, utcNow: new DateTime(2026, 1, 4, 8, 0, 0, DateTimeKind.Utc));

        Assert.That(result.Created, Is.EquivalentTo(new[]
        {
            Path.Combine(backups, "app-2026-01-04.db"),
            Path.Combine(backups, "extra-2026-01-04.db"),
        }));
        Assert.That(ReadName(Path.Combine(backups, "app-2026-01-04.db")), Is.EqualTo("paper"));
        Assert.That(ReadName(Path.Combine(backups, "extra-2026-01-04.db")), Is.EqualTo("extra"));
        Assert.That(File.Exists(Path.Combine(backups, "app-2026-01-01.db")), Is.False);
        Assert.That(File.Exists(Path.Combine(backups, "app-2026-01-02.db")), Is.False);
        Assert.That(File.Exists(Path.Combine(backups, "extra-2026-01-03.db")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(backups, "keep.txt")), Is.EqualTo("leave me"));
        Assert.That(result.Removed, Does.Contain(Path.Combine(backups, "app-2026-01-01.db")));
    }

    [Test]
    public void Same_utc_date_replaces_the_dated_file()
    {
        var appData = Path.Combine(root, "App_Data");
        var backups = Path.Combine(root, "backups");
        var source = Path.Combine(appData, "app.db");
        Directory.CreateDirectory(appData);
        CreateDatabase(source, "paper");
        var when = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        DatabaseBackup.Copy(appData, backups, keep: 14, utcNow: when);
        using (var connection = Open(source))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE item SET name = 'real' WHERE name = 'paper'";
            Assert.That(command.ExecuteNonQuery(), Is.EqualTo(1));
        }

        DatabaseBackup.Copy(appData, backups, keep: 14, utcNow: when);

        var dated = Directory.GetFiles(backups, "app-2026-06-01.db");
        Assert.That(dated, Has.Length.EqualTo(1));
        Assert.That(ReadName(dated[0]), Is.EqualTo("real"));
    }

    [Test]
    public void Retention_below_one_is_rejected()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DatabaseBackup.Copy(root, Path.Combine(root, "backups"), keep: 0));
        Assert.That(error!.ParamName, Is.EqualTo("keep"));
    }

    [Test]
    public void Http_only_urls_do_not_count_as_https()
    {
        var httpOnly = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["urls"] = "http://127.0.0.1:5050",
        }).Build();
        var mixed = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["urls"] = "http://127.0.0.1:5050;https://127.0.0.1:5051",
        }).Build();

        Assert.That(LoopbackBinding.UsesHttps(httpOnly), Is.False);
        Assert.That(LoopbackBinding.UsesHttps(mixed), Is.True);
    }

    static void CreateDatabase(string path, string name)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE item (name TEXT NOT NULL); INSERT INTO item (name) VALUES ($name);";
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    static string ReadName(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM item";
        return (string)command.ExecuteScalar()!;
    }

    static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString);
        connection.Open();
        return connection;
    }
}
